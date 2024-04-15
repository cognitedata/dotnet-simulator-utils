using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;
using CogniteSdk;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public class FactIfAttribute : FactAttribute
    {
        public FactIfAttribute(string envVar, string skipReason)
        {
            var envFlag = Environment.GetEnvironmentVariable(envVar);
            if (envFlag == null || envFlag != "true")
            {
                Skip = skipReason;
            }
        }
    }

    [Collection(nameof(SequentialTestCollection))]
    public class SimulationRunnerTest
    {

        public static IEnumerable<object[]> InputParams => 
            new List<object[]>
            {
                // 1. timeseries inputs
                new object[] {
                    SeedData.SimulatorRoutineCreateWithTsAndExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithTsAndExtendedIO,
                    new List<SimulationInputOverride>(),
                    SimulatorValue.Create(2037.478183282069),
                    true // check for timeseries
                },
                // 2. constant inputs
                new object[] {
                    SeedData.SimulatorRoutineCreateWithExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithExtendedIO,
                    new List<SimulationInputOverride>(),
                    SimulatorValue.Create(142),
                    true
                },
                // 3. constant inputs with override
                new object[] {
                    SeedData.SimulatorRoutineCreateWithExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithExtendedIO,
                    new List<SimulationInputOverride> {
                        new SimulationInputOverride {
                            ReferenceId = "IC1",
                            Value = new SimulatorValue.Double(-42),
                        },
                    },
                    SimulatorValue.Create(58),
                    true
                },
                // 4. constant string inputs
                new object[] {
                    SeedData.SimulatorRoutineCreateWithStringsIO,
                    SeedData.SimulatorRoutineRevisionWithStringsIO,
                    new List<SimulationInputOverride>(),
                    SimulatorValue.Create("42"),
                    false
                }
            };

        private const long validationEndOverwrite = 1631304000000L;

        [Theory]
        [MemberData(nameof(InputParams))]
        public async Task TestSimulationRunnerBase(
            SimulatorRoutineCreateCommandItem createRoutineItem, 
            SimulatorRoutineRevisionCreate createRevisionItem,
            IEnumerable<SimulationInputOverride> inputOverrides,
            SimulatorValue result,
            bool checkTs
        ) {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<ModeLibraryTest>();
            services.AddSingleton<StagingArea<ModelParsingInfo>>();
            services.AddSingleton<ConfigurationLibraryTest>();
            services.AddSingleton<SampleSimulationRunner>();
            services.AddSingleton<SampleSimulatorClient>();
            services.AddSingleton(new ConnectorConfig
            {
                NamePrefix = SeedData.TestIntegrationExternalId,
                AddMachineNameSuffix = false,
                UseSimulatorsApi = true
            });

            StateStoreConfig stateConfig = null;

            long? eventId = null;
            var tsToDelete = new List<string>();

            using var source = new CancellationTokenSource();
            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var FileStorageClient = provider.GetRequiredService<FileStorageClient>();

            await SeedData.GetOrCreateSimulator(cdf, SeedData.SimulatorCreate).ConfigureAwait(false);

            await TestHelpers.SimulateASimulatorRunning(cdf, SeedData.TestIntegrationExternalId).ConfigureAwait(true);

            // prepopulate routine in CDF
            SimulatorRoutineRevision revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                cdf,
                FileStorageClient,
                createRoutineItem,
                createRevisionItem
            ).ConfigureAwait(false);

            try
            {
                stateConfig = provider.GetRequiredService<StateStoreConfig>();

                var modelLib = provider.GetRequiredService<ModeLibraryTest>();
                var configLib = provider.GetRequiredService<ConfigurationLibraryTest>();
                var runner = provider.GetRequiredService<SampleSimulationRunner>();
                var sink = provider.GetRequiredService<ScopedRemoteApiSink>();

                // Run model and configuration libraries to fetch the test model and
                // test simulation configuration from CDF
                await modelLib.Init(source.Token).ConfigureAwait(false);
                await configLib.Init(source.Token).ConfigureAwait(false);

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5));
                var taskList = new List<Task>(modelLib.GetRunTasks(linkedToken));
                taskList.AddRange(configLib.GetRunTasks(linkedToken));
                await taskList.RunAll(linkedTokenSource).ConfigureAwait(false);

                Assert.NotEmpty(configLib.State);
                var configState = Assert.Contains(
                    revision.Id.ToString(), // This simulator configuration should exist in CDF
                    (IReadOnlyDictionary<string, TestConfigurationState>)configLib.State);
                var routineRevision = configLib.GetSimulationConfiguration(revision.ExternalId);
                Assert.NotNull(routineRevision);
                var configObj = routineRevision.Configuration;
                Assert.NotNull(configObj);

                var outTsIds = configObj.Outputs.Where(o => !String.IsNullOrEmpty(o.SaveTimeseriesExternalId)).Select(o => o.SaveTimeseriesExternalId).ToList();
                tsToDelete.AddRange(outTsIds);
                var inTsIds = configObj.Inputs.Where(o => !String.IsNullOrEmpty(o.SaveTimeseriesExternalId)).Select(o => o.SaveTimeseriesExternalId).ToList();
                tsToDelete.AddRange(inTsIds);

                var runs = await cdf.Alpha.Simulators.CreateSimulationRunsAsync(
                    new List<SimulationRunCreate>
                    {
                        new SimulationRunCreate
                        {
                            RoutineExternalId = routineRevision.RoutineExternalId,
                            RunType = SimulationRunType.external,
                            ValidationEndTime = validationEndOverwrite,
                            Inputs = inputOverrides.Any() ? inputOverrides : null
                        }
                    }, source.Token).ConfigureAwait(false);
                Assert.NotEmpty(runs);
                var runId = runs.First().Id;

                // Run the simulation runner and verify that the event above was picked up for execution
                using var linkedTokenSource2 = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken2 = linkedTokenSource2.Token;
                linkedTokenSource2.CancelAfter(TimeSpan.FromSeconds(5));
                var taskList2 = new List<Task> { runner.Run(linkedToken2) };
                await taskList2.RunAll(linkedTokenSource2).ConfigureAwait(false);

                Assert.True(runner.MetadataInitialized);

                var runUpdated = await cdf.Alpha.Simulators.RetrieveSimulationRunsAsync(
                    new List<long> { runId }, source.Token).ConfigureAwait(false);

                Assert.NotEmpty(runUpdated);
                eventId = runUpdated.First().EventId;
                Assert.NotNull(eventId);
                Assert.NotNull(runUpdated.First().SimulationTime);

                var retryCount = 0;
                Event? cdfEvent = null;

                while (retryCount < 20)
                {
                    var cdfEvents = await cdf.Events.RetrieveAsync(
                        new List<long> { eventId.Value },
                        true,
                        source.Token).ConfigureAwait(false);
                    if (cdfEvents.Any())
                    {
                        cdfEvent = cdfEvents.First();
                        break;
                    } else {
                        retryCount++;
                        await Task.Delay(100);
                    }
                }

                Assert.NotNull(cdfEvent);
                    
                var eventMetadata = cdfEvent.Metadata;
                Assert.True(eventMetadata.ContainsKey("simulationTime"));
                Assert.True(long.TryParse(eventMetadata["simulationTime"], out var simulationTime));
                Assert.True(simulationTime <= validationEndOverwrite);
                Assert.True(eventMetadata.TryGetValue("status", out var eventStatus));
                Assert.Equal("success", eventStatus);
                Assert.Equal(runUpdated.First().SimulationTime, simulationTime);

                var logsRes = await cdf.Alpha.Simulators.RetrieveSimulatorLogsAsync(
                    new List<Identity> { new Identity(runUpdated.First().LogId.Value) }, source.Token).ConfigureAwait(false);

                // this test is not running the full connector runtime
                // so logs are not being automatically sent to CDF
                var logData = logsRes.First().Data;
                Assert.Empty(logData);

                await sink.Flush(cdf.Alpha.Simulators, CancellationToken.None).ConfigureAwait(false);

                // check logs again after flushing
                logsRes = await cdf.Alpha.Simulators.RetrieveSimulatorLogsAsync(
                    new List<Identity> { new Identity(runUpdated.First().LogId.Value) }, source.Token).ConfigureAwait(false);

                logData = logsRes.First().Data;
                Assert.NotNull(logData.First().Message);

                // check inputs/outputs from the /runs/data/list endpoint
                var runDataRes = await cdf.Alpha.Simulators.ListSimulationRunsDataAsync(
                    new List<long> { runId }, source.Token).ConfigureAwait(false);
                Assert.NotEmpty(runDataRes);
                var inputs = runDataRes.First().Inputs;
                Assert.NotEmpty(inputs);
                foreach (var input in inputs)
                {
                    Assert.Contains(input.ReferenceId, configObj.Inputs.Select(i => i.ReferenceId));
                    if (input.TimeseriesExternalId != null)
                    {
                        Assert.Contains(input.TimeseriesExternalId, inTsIds);
                    }
                    if (inputOverrides.Any(i => i.ReferenceId == input.ReferenceId)) {
                        Assert.True(input.Overridden);
                        var inputValue = input.Value as SimulatorValue.Double;
                        var inputOverride = inputOverrides.First(i => i.ReferenceId == input.ReferenceId).Value as SimulatorValue.Double;
                        Assert.Equal(inputValue?.Value, inputOverride?.Value);
                    } else {
                        Assert.NotEqual(input.Overridden, true);
                    }
                }

                Assert.NotEmpty(runDataRes);
                Assert.NotEmpty(runDataRes.First().Outputs);

                var resultValue = runDataRes.First().Outputs.First().Value;
                Assert.Equal(resultValue.Type, result?.Type);
                Assert.Equal(resultValue, result);

                if (checkTs) {
                    Assert.True(outTsIds.Any());
                    Assert.True(inTsIds.Any());

                    // Check that output time series were created
                    var outTs = await cdf.TimeSeries.RetrieveAsync(outTsIds, true, source.Token).ConfigureAwait(false);
                    Assert.True(outTs.Any(), $"No output time series were created [{string.Join(",", outTsIds)}]");
                    Assert.Equal(outTsIds.Count, outTs.Count());

                    // Check that input time series were created
                    var inTs = await cdf.TimeSeries.RetrieveAsync(inTsIds, true, source.Token).ConfigureAwait(false);
                    Assert.True(inTs.Any(), $"No input time series were created [{string.Join(",", inTsIds)}]");
                    Assert.Equal(inTsIds.Count, inTs.Count());

                    // Check that the correct output was added as a data point
                    var outDps = await cdf.DataPoints.ListAsync(
                        new DataPointsQuery
                        {
                            Start = simulationTime.ToString(),
                            End = (simulationTime + 1).ToString(),
                            Items = outTs.Select(o => new DataPointsQueryItem
                            {
                                ExternalId = o.ExternalId
                            })
                        }, source.Token).ConfigureAwait(false);
                    Assert.True(outDps.Items.Any());
                    Assert.NotNull(SampleRoutine._output);
                    Assert.Equal(SampleRoutine._output, outDps.Items.First().NumericDatapoints.Datapoints.First().Value);

                    // Check that the correct input sample was added as a data point
                    var inDps = await cdf.DataPoints.ListAsync(
                        new DataPointsQuery
                        {
                            Start = simulationTime.ToString(),
                            End = (simulationTime + 1).ToString(),
                            Items = inTs.Select(i => new DataPointsQueryItem
                            {
                                ExternalId = i.ExternalId
                            })
                        }, source.Token).ConfigureAwait(false);
                    Assert.True(inDps.Items.Any());
                    Assert.NotEmpty(SampleRoutine._inputs);
                    Assert.Contains(inDps.Items.First().NumericDatapoints.Datapoints.First().Value, SampleRoutine._inputs);
                }
            }
            finally
            {
                if (eventId.HasValue)
                {

                    await cdf.Events.DeleteAsync(
                        new List<long> { eventId.Value }, source.Token).ConfigureAwait(false);
                }
                if (tsToDelete.Any())
                {
                    await cdf.TimeSeries.DeleteAsync(new TimeSeriesDelete
                    {
                        IgnoreUnknownIds = true,
                        Items = tsToDelete.Select(i => new Identity(i)).ToList()
                    }, source.Token).ConfigureAwait(false);
                }
                provider.Dispose(); // Dispose provider to also dispose managed services
                if (Directory.Exists("./files"))
                {
                    Directory.Delete("./files", true);
                }
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
                await SeedData.DeleteSimulator(cdf, SeedData.SimulatorCreate.ExternalId);
            }

        }
    }

    public class SampleRoutine : RoutineImplementationBase
    {
        public static List<double> _inputs;
        public static double? _output;
        public SampleRoutine(SimulatorRoutineRevision config, Dictionary<string, SimulatorValueItem> inputData)
            :base(config, inputData)
        {
            _inputs = new List<double>();
            _output = null;
        }
        
        public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments)
        {
            SimulatorValue outputValue;
            if (outputConfig == null)
            {
                throw new ArgumentNullException(nameof(outputConfig));
            }
            if (_output == null)
            {
                throw new InvalidOperationException("Output value not set");
            }
            if (outputConfig.ValueType == SimulatorValueType.DOUBLE)
            {
                outputValue = new SimulatorValue.Double(_output.Value);   
            } else if (outputConfig.ValueType == SimulatorValueType.STRING)
            {
                outputValue = new SimulatorValue.String(_output.ToString());
            } else {
                throw new InvalidOperationException("Unsupported value type");
            }
            return new SimulatorValueItem() {
                Value = outputValue,
                ReferenceId = outputConfig.ReferenceId,
                TimeseriesExternalId = outputConfig.SaveTimeseriesExternalId,
                Unit = outputConfig.Unit != null ? new SimulatorValueUnit() {
                    Name = outputConfig.Unit.Name,
                } : null,
                ValueType = outputConfig.ValueType
            };
        }

        public override void RunCommand(string command, Dictionary<string, string> arguments)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }
            if (command == "Simulate")
            {
                _output = _inputs.Sum();
            }
        }

        public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments)
        {
            double value;
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            if (input.Value == null)
            {
                throw new ArgumentNullException(nameof(input.Value));
            }
            if (input.Value.Type == SimulatorValueType.DOUBLE)
            {
                var doubleValue = input.Value as SimulatorValue.Double;
                if (doubleValue == null)
                {
                    throw new InvalidOperationException("Could not parse input value");
                }
                value = doubleValue.Value;
            }
            else
            {
                var stringValue = input.Value as SimulatorValue.String;
                if (stringValue == null || !double.TryParse(stringValue.Value, out value))
                {
                    throw new InvalidOperationException("Could not parse input value");
                }
            }
            _inputs.Add(value);
        }
    }

    public class SampleSimulatorClient : ISimulatorClient<TestFileState, SimulatorRoutineRevision>
    {
        public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(
            TestFileState modelState, 
            SimulatorRoutineRevision simulationConfiguration, 
            Dictionary<string, SimulatorValueItem> inputData)
        {
            var routine = new SampleRoutine(simulationConfiguration, inputData);
            return Task.FromResult(routine.PerformSimulation());
        }
    }

    public class SampleSimulationRunner :
        RoutineRunnerBase<TestFileState, TestConfigurationState, SimulatorRoutineRevision>
    {
        internal const string connectorName = "integration-tests-connector";
        public bool MetadataInitialized { get; private set; }
        private ILogger<SampleSimulationRunner> _logger;

        public SampleSimulationRunner(
            CogniteDestination cdf,
            ModeLibraryTest modelLibrary,
            ConfigurationLibraryTest configLibrary,
            SampleSimulatorClient client,
            ConnectorConfig config,
            Microsoft.Extensions.Logging.ILogger<SampleSimulationRunner> logger
        ) :
            base(config,
                new List<SimulatorConfig>
                {
                    new SimulatorConfig
                    {
                        Name = SeedData.TestSimulatorExternalId,
                        DataSetId = CdfTestClient.TestDataset
                    }
                },
                cdf,
                modelLibrary,
                configLibrary,
                client,
                logger)
        {
            _logger = logger;
        }

        protected override async Task EndSimulationRun(SimulationRunEvent simEv,
            CancellationToken token)
        {
            _logger.LogWarning("A warning to test remote logging. No actions needed, not a real connector");
        }

        protected override void InitSimulationEventMetadata(
            TestFileState modelState,
            TestConfigurationState configState,
            SimulatorRoutineRevision configObj,
            Dictionary<string, string> metadata)
        {
            MetadataInitialized = true;
        }
    }
}