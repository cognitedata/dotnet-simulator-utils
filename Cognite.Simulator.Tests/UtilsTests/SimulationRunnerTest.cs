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
        private const long validationEndOverwrite = 1631304000000L;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TestSimulationRunnerBase(bool useConstInputs)
        {
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
                NamePrefix = SampleSimulationRunner.connectorName,
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

            // prepopulate routine in CDF
            SimulatorRoutineRevision revision;

            if (useConstInputs) {
                revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                    cdf,
                    FileStorageClient,
                    SeedData.SimulatorRoutineCreateWithExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithExtendedIO
                ).ConfigureAwait(false);
            } else {
                revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                    cdf,
                    FileStorageClient,
                    SeedData.SimulatorRoutineCreateWithTsAndExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithTsAndExtendedIO
                ).ConfigureAwait(false);
            }

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
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(10));
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

                // if (configObj.InputConstants != null) {
                //     var inConstTsIds = configObj.InputConstants.Select(o => o.SaveTimeseriesExternalId).ToList();
                //     tsToDelete.AddRange(inConstTsIds);
                //     inTsIds.AddRange(inConstTsIds);
                // }
                Assert.True(outTsIds.Any());
                Assert.True(inTsIds.Any());

                await TestHelpers.SimulateProsperRunningAsync(cdf, "integration-tests-connector").ConfigureAwait(true);

                var runs = await cdf.Alpha.Simulators.CreateSimulationRunsAsync(
                    new List<SimulationRunCreate>
                    {
                        new SimulationRunCreate
                        {
                            RoutineExternalId = routineRevision.RoutineExternalId,
                            RunType = SimulationRunType.external,
                            ValidationEndTime = validationEndOverwrite
                        }
                    }, source.Token).ConfigureAwait(false);
                Assert.NotEmpty(runs);
                var runId = runs.First().Id;

                // Run the simulation runner and verify that the event above was picked up for execution
                using var linkedTokenSource2 = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken2 = linkedTokenSource2.Token;
                linkedTokenSource2.CancelAfter(TimeSpan.FromSeconds(15));
                var taskList2 = new List<Task> { runner.Run(linkedToken2) };
                await taskList2.RunAll(linkedTokenSource2).ConfigureAwait(false);

                Assert.True(runner.MetadataInitialized);
                

                // Check that output time series were created
                var outTs = await cdf.TimeSeries.RetrieveAsync(outTsIds, true, source.Token).ConfigureAwait(false);
                Assert.True(outTs.Any(), $"No output time series were created [{string.Join(",", outTsIds)}]");
                Assert.Equal(outTsIds.Count, outTs.Count());

                // Check that input time series were created
                var inTs = await cdf.TimeSeries.RetrieveAsync(inTsIds, true, source.Token).ConfigureAwait(false);
                Assert.True(inTs.Any(), $"No input time series were created [{string.Join(",", inTsIds)}]");
                Assert.Equal(inTsIds.Count, inTs.Count());

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
            if (outputConfig == null)
            {
                throw new ArgumentNullException(nameof(outputConfig));
            }
            if (outputConfig.ValueType != SimulatorValueType.DOUBLE)
            {
                throw new ArgumentException("Output value type must be double");
            }
            return new SimulatorValueItem() {
                Value = new SimulatorValue.Double(_output.Value),
                Overridden = false,
                ReferenceId = outputConfig.ReferenceId,
                TimeseriesExternalId = outputConfig.SaveTimeseriesExternalId,
                Unit = outputConfig.Unit,
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
            _inputs.Add((input.Value as SimulatorValue.Double).Value);
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
                        Name = "PROSPER",
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