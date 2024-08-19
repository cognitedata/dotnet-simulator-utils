﻿using Cognite.Extractor.Common;
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
                // // 1. timeseries inputs
                // new object[] {
                //     SeedData.SimulatorRoutineCreateWithTsAndExtendedIO,
                //     SeedData.SimulatorRoutineRevisionWithTsAndExtendedIO,
                //     new List<SimulationInputOverride>(),
                //     SimulatorValue.Create(2037.478183282069),
                //     true // check for timeseries
                // },
                // // 2. constant inputs
                // new object[] {
                //     SeedData.SimulatorRoutineCreateWithExtendedIO,
                //     SeedData.SimulatorRoutineRevisionWithExtendedIO,
                //     new List<SimulationInputOverride>(),
                //     SimulatorValue.Create(142),
                //     true
                // },
                // // 3. constant inputs with override
                // new object[] {
                //     SeedData.SimulatorRoutineCreateWithExtendedIO,
                //     SeedData.SimulatorRoutineRevisionWithExtendedIO,
                //     new List<SimulationInputOverride> {
                //         new SimulationInputOverride {
                //             ReferenceId = "IC1",
                //             Value = new SimulatorValue.Double(-42),
                //         },
                //     },
                //     SimulatorValue.Create(58),
                //     true
                // },
                // // 4. constant string inputs
                // new object[] {
                //     SeedData.SimulatorRoutineCreateWithStringsIO,
                //     SeedData.SimulatorRoutineRevisionWithStringsIO,
                //     new List<SimulationInputOverride>(),
                //     SimulatorValue.Create("42"),
                //     false
                // },
                // // 5. timeseries inputs with override
                // new object[] {
                //     SeedData.SimulatorRoutineCreateWithTsAndExtendedIO,
                //     SeedData.SimulatorRoutineRevisionWithTsAndExtendedIO,
                //     new List<SimulationInputOverride> {
                //         new SimulationInputOverride {
                //             ReferenceId = "IT1",
                //             Value = new SimulatorValue.Double(2345),
                //         },
                //     },
                //     SimulatorValue.Create(2345),
                //     true
                // },
                // // 6. timeseries inputs with disabled data sampling (used latest value)
                // new object[] {
                //     SeedData.SimulatorRoutineCreateWithTsNoDataSampling,
                //     SeedData.SimulatorRoutineRevisionWithTsNoDataSampling,
                //     new List<SimulationInputOverride>(),
                //     SimulatorValue.Create(2037.7438329838599),
                //     false
                // },
            };

        private const long validationEndOverwrite = 1631304000000L;

        [Theory]
        [MemberData(nameof(InputParams))]
        public async Task TestSimulationRunnerBase(
            SimulatorRoutineCreateCommandItem createRoutineItem, 
            SimulatorRoutineRevisionCreate createRevisionItem,
            IEnumerable<SimulationInputOverride> inputOverrides,
            SimulatorValue expectedResult,
            bool checkTs
        ) {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<SeedData>();
            services.AddSingleton<ModeLibraryTest>();
            services.AddSingleton<ModelParsingInfo>();
            services.AddSingleton<RoutineLibraryTest>();
            services.AddSingleton<SampleSimulationRunner>();
            services.AddSingleton<SampleSimulatorClient>();
            services.AddSingleton(services => {
                var seed = services.GetRequiredService<SeedData>();
                return new ConnectorConfig
                {
                    NamePrefix = seed.TestIntegrationExternalId,
                    AddMachineNameSuffix = false,
                };
            });

            StateStoreConfig stateConfig = null;

            var tsToDelete = new List<string>();

            using var source = new CancellationTokenSource();
            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            // var FileStorageClient = provider.GetRequiredService<FileStorageClient>();
            var seed = provider.GetRequiredService<SeedData>();

            // await SeedData.GetOrCreateSimulator(cdf, SeedData.SimulatorCreate).ConfigureAwait(false);

            // await TestHelpers.SimulateASimulatorRunning(cdf, SeedData.TestIntegrationExternalId).ConfigureAwait(true);

            // prepopulate routine in CDF
            SimulatorRoutineRevision revision = await seed.GetOrCreateSimulatorRoutineRevision(
                // cdf,
                // FileStorageClient,
                createRoutineItem,
                createRevisionItem
            ).ConfigureAwait(false);

            try
            {
                stateConfig = provider.GetRequiredService<StateStoreConfig>();

                var modelLib = provider.GetRequiredService<ModeLibraryTest>();
                var configLib = provider.GetRequiredService<RoutineLibraryTest>();
                var runner = provider.GetRequiredService<SampleSimulationRunner>();
                var sink = provider.GetRequiredService<ScopedRemoteApiSink>();

                // Run model and configuration libraries to fetch the test model and
                // test simulation configuration from CDF
                await modelLib.Init(source.Token).ConfigureAwait(false);
                await configLib.Init(source.Token).ConfigureAwait(false);

                // models are only processed right before the run happens (because we don't run the tasks from ModelLibrary)
                // so this should be empty
                var processedModels = modelLib._state.Values.Where(m => m.FilePath != null && m.Processed);
                Assert.Empty(processedModels);

                var routineRevision = await configLib.GetRoutineRevision(revision.ExternalId).ConfigureAwait(false);
                Assert.NotNull(routineRevision);
                var configObj = routineRevision.Configuration;
                Assert.NotNull(configObj);

                var outTsIds = configObj.Outputs.Where(o => !string.IsNullOrEmpty(o.SaveTimeseriesExternalId)).Select(o => o.SaveTimeseriesExternalId).ToList();
                tsToDelete.AddRange(outTsIds);
                var inTsIds = configObj.Inputs.Where(o => !string.IsNullOrEmpty(o.SaveTimeseriesExternalId)).Select(o => o.SaveTimeseriesExternalId).ToList();
                tsToDelete.AddRange(inTsIds);

                var runs = await cdf.Alpha.Simulators.CreateSimulationRunsAsync(
                    new List<SimulationRunCreate>
                    {
                        new SimulationRunCreate
                        {
                            RoutineExternalId = routineRevision.RoutineExternalId,
                            RunType = SimulationRunType.external,
                            RunTime = validationEndOverwrite,
                            Inputs = inputOverrides.Any() ? inputOverrides : null
                        }
                    }, source.Token).ConfigureAwait(false);
                Assert.NotEmpty(runs);
                var run = runs.First();

                var modelRevisionRes = await cdf.Alpha.Simulators.RetrieveSimulatorModelRevisionsAsync(
                    new List<Identity> { new Identity(run.ModelRevisionExternalId) }, source.Token).ConfigureAwait(false);

                var modelRevision = modelRevisionRes.First();

                // Run the simulation runner and verify that the run above was picked up for execution
                using var linkedTokenSource2 = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken2 = linkedTokenSource2.Token;
                linkedTokenSource2.CancelAfter(TimeSpan.FromSeconds(10));
                // Task[] taskList2 = new [] { runner.Run(linkedToken2) };
                // var tempStatus = ;
                // TestHelpers.WaitUntilFinalStatus(cdf, run.Id), 
                await Task.WhenAny(runner.Run(linkedToken2), TestHelpers.WaitUntilFinalStatus(cdf, run.Id)).ConfigureAwait(false);

                Assert.Empty(modelLib._temporaryState); // temporary state should be empty after running the model as it cleans up automatically
                Assert.Empty(Directory.GetFiles("./files/temp"));

                var runUpdatedRes = await cdf.Alpha.Simulators.RetrieveSimulationRunsAsync(
                    new List<long> { run.Id }, source.Token).ConfigureAwait(false);

                Assert.NotEmpty(runUpdatedRes);
                var runUpdated = runUpdatedRes.First();
                Assert.Equal(SimulationRunStatus.success, runUpdated.Status);
                Assert.Equal("Simulation ran to completion", runUpdated.StatusMessage);
                Assert.NotNull(runUpdated.SimulationTime);
                var simulationTime = runUpdated.SimulationTime.Value;

                var logsRes = await cdf.Alpha.Simulators.RetrieveSimulatorLogsAsync(
                    new List<Identity> { new Identity(runUpdated.LogId.Value) }, source.Token).ConfigureAwait(false);

                // this test is not running the full connector runtime
                // so logs are not being automatically sent to CDF
                var logData = logsRes.First().Data;
                Assert.Empty(logData);

                await sink.Flush(cdf.Alpha.Simulators, CancellationToken.None).ConfigureAwait(false);

                // check logs again after flushing
                logsRes = await cdf.Alpha.Simulators.RetrieveSimulatorLogsAsync(
                    new List<Identity> { new Identity(runUpdated.LogId.Value) }, source.Token).ConfigureAwait(false);

                logData = logsRes.First().Data;
                Assert.NotNull(logData.First().Message);

                // check inputs/outputs from the /runs/data/list endpoint
                var runDataRes = await cdf.Alpha.Simulators.ListSimulationRunsDataAsync(
                    new List<long> { run.Id }, source.Token).ConfigureAwait(false);
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
                Assert.Equal(expectedResult?.Type, resultValue.Type);
                Assert.Equal(expectedResult, resultValue);

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
                await seed.DeleteSimulator();
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

        public override void RunCommand(Dictionary<string, string> arguments)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }
            if(arguments.TryGetValue("command", out var cmd)) {
                if (cmd == "Simulate")
                {
                    _output = _inputs.Sum();
                }
            } else {
                throw new InvalidOperationException("No command provided");
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
        public Task ExtractModelInformation(TestFileState state, CancellationToken _token)
        {
            throw new NotImplementedException();
        }

        public string GetConnectorVersion()
        {
            throw new NotImplementedException();
        }

        public string GetSimulatorVersion()
        {
            throw new NotImplementedException();
        }

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
        RoutineRunnerBase<DefaultAutomationConfig,TestFileState, SimulatorRoutineRevision>
    {
        internal const string connectorName = "integration-tests-connector";
        private ILogger<SampleSimulationRunner> _logger;

        public SampleSimulationRunner(
            CogniteDestination cdf,
            ModeLibraryTest modelLibrary,
            RoutineLibraryTest configLibrary,
            SampleSimulatorClient client,
            ConnectorConfig config,
            SeedData seed,
            Microsoft.Extensions.Logging.ILogger<SampleSimulationRunner> logger
        ) :
            base(config,
                new List<SimulatorConfig>
                {
                    new SimulatorConfig
                    {
                        Name = seed.TestSimulatorExternalId,
                        DataSetId = SeedData.TestDataSetId
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
    }
}