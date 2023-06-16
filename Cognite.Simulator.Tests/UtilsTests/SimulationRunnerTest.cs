using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils;
using CogniteSdk;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        [Fact]
        public async Task TestSimulationRunnerBase()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileDownloadClient>();
            services.AddSingleton<ModeLibraryTest>();
            services.AddSingleton<StagingArea<ModelParsingInfo>>();
            services.AddSingleton<ConfigurationLibraryTest>();
            services.AddSingleton<SampleSimulationRunner>();
            services.AddSingleton<SampleSimulatorClient>();
            services.AddSingleton(new ConnectorConfig
            {
                NamePrefix = SampleSimulationRunner.connectorName,
                AddMachineNameSuffix = false,
                UseSimulatorsApi = false
            });

            StateStoreConfig stateConfig = null;

            string eventId = "";
            string sequenceId = "";
            var tsToDelete = new List<string>();

            using var source = new CancellationTokenSource();
            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            try
            {
                stateConfig = provider.GetRequiredService<StateStoreConfig>();

                var modelLib = provider.GetRequiredService<ModeLibraryTest>();
                var configLib = provider.GetRequiredService<ConfigurationLibraryTest>();
                var runner = provider.GetRequiredService<SampleSimulationRunner>();

                // Run model and configuration libraries to fetch the test model and
                // test simulation configuration from CDF
                await modelLib.Init(source.Token).ConfigureAwait(false);
                await configLib.Init(source.Token).ConfigureAwait(false);

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(6));
                var taskList = new List<Task>(modelLib.GetRunTasks(linkedToken));
                taskList.AddRange(configLib.GetRunTasks(linkedToken));
                await taskList.RunAll(linkedTokenSource).ConfigureAwait(false);

                Assert.NotEmpty(configLib.State);
                var configState = Assert.Contains(
                    "PROSPER-SC-UserDefined-SRT-Connector_Test_Model", // This simulator configuration should exist in CDF
                    (IReadOnlyDictionary<string, TestConfigurationState>)configLib.State);
                var configObj = configLib.GetSimulationConfiguration(
                    "PROSPER", "Connector Test Model", "UserDefined", "SRT");
                Assert.NotNull(configObj);

                var outTsIds = configObj.OutputTimeSeries.Select(o => o.ExternalId).ToList();
                tsToDelete.AddRange(outTsIds);
                var inTsIds = configObj.InputTimeSeries.Select(o => o.SampleExternalId).ToList();
                tsToDelete.AddRange(inTsIds);

                // Create a simulation event ready to run for the test configuration
                var events = await cdf.Events.CreateSimulationEventReadyToRun(
                    new List<SimulationEvent>
                    {
                        new SimulationEvent
                        {
                            Calculation = configObj.Calculation,
                            CalculationId = configState.Id,
                            Connector = configObj.Connector,
                            DataSetId = CdfTestClient.TestDataset,
                            RunType = "manual",
                            UserEmail = configObj.UserEmail,
                            ValidationEndOverwrite = validationEndOverwrite
                        }

                    },
                    source.Token).ConfigureAwait(false);
                Assert.NotEmpty(events);
                eventId = events.First().ExternalId;

                // Run the simulation runner and verify that the event above was picked up for execution
                using var linkedTokenSource2 = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken2 = linkedTokenSource2.Token;
                linkedTokenSource2.CancelAfter(TimeSpan.FromSeconds(15));
                var taskList2 = new List<Task> { runner.Run(linkedToken2) };
                await taskList2.RunAll(linkedTokenSource2).ConfigureAwait(false);

                Assert.True(runner.MetadataInitialized);
                
                // Check that output time series were created
                var outTs = await cdf.TimeSeries.RetrieveAsync(outTsIds, true, source.Token).ConfigureAwait(false);
                Assert.True(outTs.Any());
                Assert.Equal(outTsIds.Count, outTs.Count());

                // Check that input time series were created
                var inTs = await cdf.TimeSeries.RetrieveAsync(inTsIds, true, source.Token).ConfigureAwait(false);
                Assert.True(inTs.Any());
                Assert.Equal(inTsIds.Count, inTs.Count());

                var eventUpdated = await cdf.Events.RetrieveAsync(
                    new List<string> { eventId },
                    true,
                    source.Token).ConfigureAwait(false);
                Assert.NotEmpty(eventUpdated);
                var eventMetadata = eventUpdated.First().Metadata;
                Assert.True(eventMetadata.ContainsKey("calcTime"));
                Assert.True(long.TryParse(eventMetadata["calcTime"], out var eventCalcTime));
                Assert.True(eventCalcTime <= validationEndOverwrite);
                Assert.True(eventMetadata.TryGetValue("status", out var eventStatus));
                Assert.Equal("success", eventStatus);

                // Check that the correct output was added as a data point
                var outDps = await cdf.DataPoints.ListAsync(
                    new DataPointsQuery
                    {
                        Start = eventCalcTime.ToString(),
                        End = (eventCalcTime + 1).ToString(),
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
                        Start = eventCalcTime.ToString(),
                        End = (eventCalcTime + 1).ToString(),
                        Items = inTs.Select(i => new DataPointsQueryItem
                        {
                            ExternalId = i.ExternalId
                        })
                    }, source.Token).ConfigureAwait(false);
                Assert.True(inDps.Items.Any());
                Assert.NotEmpty(SampleRoutine._inputs);
                Assert.Contains(inDps.Items.First().NumericDatapoints.Datapoints.First().Value, SampleRoutine._inputs);

                // ID of events already processed should be cached in the runner
                Assert.Contains(runner.AlreadyProcessed, e => e.Key == eventId);

                // A sequence should have been created in CDF with the run configuration data
                // and one with the simulation results (system curves).
                Assert.True(eventMetadata.TryGetValue("runConfigurationSequence", out var runSequenceId));
                Assert.True(eventMetadata.TryGetValue("runConfigurationRowStart", out var runSequenceRowStart));
                Assert.True(eventMetadata.TryGetValue("runConfigurationRowEnd", out var runSequenceRowEnd));
                sequenceId = runSequenceId;

                // Verify a sequence was created in CDF with the run configuration
                // Check sampling results
                var data = await cdf.Sequences.ListRowsAsync(
                    new SequenceRowQuery
                    {
                        ExternalId = runSequenceId,
                        Start = long.Parse(runSequenceRowStart),
                        End = long.Parse(runSequenceRowEnd) + 1
                    },
                    source.Token).ConfigureAwait(false);
                var dictResult = ToRowDictionary(data);
                Assert.True(dictResult.ContainsKey("runEventId"));
                Assert.Equal(eventId, dictResult["runEventId"]);
                Assert.True(dictResult.ContainsKey("calcTime"));
                Assert.Equal(eventCalcTime.ToString(), dictResult["calcTime"]);

                // Verify sampling start, end and calculation time values
                Assert.True(dictResult.ContainsKey("samplingEnd"));
                Assert.True(dictResult.ContainsKey("samplingStart"));
                Assert.True(dictResult.ContainsKey("validationEndOffset"));

                SamplingRange range = new TimeRange()
                {
                    Min = long.Parse(dictResult["samplingStart"]),
                    Max = long.Parse(dictResult["samplingEnd"])
                };
                Assert.Equal(eventCalcTime, range.Midpoint);
            }
            finally
            {
                if (!string.IsNullOrEmpty(eventId))
                {
                    await cdf.Events.DeleteAsync(
                        new List<string> { eventId }, source.Token).ConfigureAwait(false);
                }
                if (!string.IsNullOrEmpty(sequenceId))
                {
                    await cdf.Sequences.DeleteAsync(
                        new List<string> { sequenceId }, source.Token).ConfigureAwait(false);
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
                if (Directory.Exists("./configurations"))
                {
                    Directory.Delete("./configurations", true);
                }
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
            }

        }

        [FactIf(envVar: "ENABLE_SIMULATOR_API_TESTS", skipReason: "Immature Simulator APIs")]
        [Trait("Category", "API")]
        public async Task TestSimulationRunnerBaseWithApi()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileDownloadClient>();
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

            long? runId;
            string sequenceId = "";
            var tsToDelete = new List<string>();

            using var source = new CancellationTokenSource();
            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            try
            {
                stateConfig = provider.GetRequiredService<StateStoreConfig>();

                var modelLib = provider.GetRequiredService<ModeLibraryTest>();
                var configLib = provider.GetRequiredService<ConfigurationLibraryTest>();
                var runner = provider.GetRequiredService<SampleSimulationRunner>();

                // Run model and configuration libraries to fetch the test model and
                // test simulation configuration from CDF
                await modelLib.Init(source.Token).ConfigureAwait(false);
                await configLib.Init(source.Token).ConfigureAwait(false);

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(6));
                var taskList = new List<Task>(modelLib.GetRunTasks(linkedToken));
                taskList.AddRange(configLib.GetRunTasks(linkedToken));
                await taskList.RunAll(linkedTokenSource).ConfigureAwait(false);

                Assert.NotEmpty(configLib.State);
                var configState = Assert.Contains(
                    "PROSPER-SC-UserDefined-SRT-Connector_Test_Model", // This simulator configuration should exist in CDF
                    (IReadOnlyDictionary<string, TestConfigurationState>)configLib.State);
                var configObj = configLib.GetSimulationConfiguration(
                    "PROSPER", "Connector Test Model", "UserDefined", "SRT");
                Assert.NotNull(configObj);

                var outTsIds = configObj.OutputTimeSeries.Select(o => o.ExternalId).ToList();
                tsToDelete.AddRange(outTsIds);
                var inTsIds = configObj.InputTimeSeries.Select(o => o.SampleExternalId).ToList();
                tsToDelete.AddRange(inTsIds);

                // Create a simulation event ready to run for the test configuration
                var simRuns = await cdf.Alpha.Simulators.CreateSimulationRunsAsync(
                    new List<SimulationRunCreate>
                    {
                        new SimulationRunCreate
                        {
                            ModelName = configObj.ModelName,
                            RoutineName = configObj.CalculationName,
                            SimulatorName = configObj.Simulator
                        }
                    }, source.Token).ConfigureAwait(false);
                Assert.NotEmpty(simRuns);
                runId = simRuns.First().Id;

                // Run the simulation runner and verify that the event above was picked up for execution
                using var linkedTokenSource2 = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken2 = linkedTokenSource2.Token;
                linkedTokenSource2.CancelAfter(TimeSpan.FromSeconds(15));
                var taskList2 = new List<Task> { runner.Run(linkedToken2) };
                await taskList2.RunAll(linkedTokenSource2).ConfigureAwait(false);

                Assert.True(runner.MetadataInitialized);

                // Uncomment when we have "validation end time" parameter support in the API
                // // Check that output time series were created
                // var outTs = await cdf.TimeSeries.RetrieveAsync(outTsIds, true, source.Token).ConfigureAwait(false);
                // Assert.True(outTs.Any());
                // Assert.Equal(outTsIds.Count, outTs.Count());

                // // Check that input time series were created
                // var inTs = await cdf.TimeSeries.RetrieveAsync(inTsIds, true, source.Token).ConfigureAwait(false);
                // Assert.True(inTs.Any());
                // Assert.Equal(inTsIds.Count, inTs.Count());

                var updatedSimRuns = await cdf.Alpha.Simulators.ListSimulationRunsAsync(
                    new SimulationRunQuery
                    {
                        Filter = new SimulationRunFilter
                        {
                            ModelName = configObj.ModelName,
                            RoutineName = configObj.CalculationName,
                            SimulatorName = configObj.Simulator,
                            Status = SimulationRunStatus.failure // this fails due to the empty input time series at the time range, we need runTime param to fix this
                        }
                    }, source.Token).ConfigureAwait(false);
                
                Assert.NotEmpty(updatedSimRuns.Items);

                var simRunUpdated = updatedSimRuns.Items.FirstOrDefault(s => s.Id == runId);

                Assert.NotNull(simRunUpdated);
                // // Uncomment when we have eventId persisted in the DB
                // Assert.True(simRunUpdated.EventId.HasValue);
                // var simEvent = await cdf.Events.GetAsync(simRunUpdated.EventId.Value, source.Token);
                // Assert.NotNull(simEvent);

                Assert.Equal(configObj.CalculationName, simRunUpdated.RoutineName);
                Assert.Equal("PROSPER", simRunUpdated.SimulatorName);
                Assert.Equal("Connector Test Model", simRunUpdated.ModelName);
                Assert.StartsWith("No data points were found for time series", simRunUpdated.StatusMessage);
            }
            finally
            {
                if (!string.IsNullOrEmpty(sequenceId))
                {
                    await cdf.Sequences.DeleteAsync(
                        new List<string> { sequenceId }, source.Token).ConfigureAwait(false);
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
                if (Directory.Exists("./configurations"))
                {
                    Directory.Delete("./configurations", true);
                }
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
            }

        }

        private static Dictionary<string, string> ToRowDictionary(SequenceData data)
        {
            Dictionary<string, string> result = new();
            foreach (var row in data.Rows)
            {
                var cells = row.Values.ToArray();
                var key = ((MultiValue.String)cells[0]).Value;
                var value = ((MultiValue.String)cells[1]).Value;
                result.Add(key, value);
            }
            return result;
        }
    }

    public class SampleRoutine : RoutineImplementationBase
    {
        public static List<double> _inputs;
        public static double? _output;
        public SampleRoutine(SimulationConfigurationWithRoutine config, Dictionary<string, double> inputData)
            :base(config, inputData)
        {
            _inputs = new List<double>();
            _output = null;
        }
        
        public override double GetTimeSeriesOutput(OutputTimeSeriesConfiguration outputConfig, Dictionary<string, string> arguments)
        {
            return _output.Value;
        }

        public override void RunCommand(string command, Dictionary<string, string> arguments)
        {
            if (command == "Simulate")
            {
                _output = _inputs.Sum();
            }
        }

        public override void SetManualInput(string value, Dictionary<string, string> arguments)
        {
            _inputs.Add(double.Parse(value));
        }

        public override void SetTimeSeriesInput(InputTimeSeriesConfiguration inputConfig, double value, Dictionary<string, string> arguments)
        {
            _inputs.Add(value);
        }
    }

    public class SampleSimulatorClient : ISimulatorClient<TestFileState, SimulationConfigurationWithRoutine>
    {
        public Task<Dictionary<string, double>> RunSimulation(
            TestFileState modelState, 
            SimulationConfigurationWithRoutine simulationConfiguration, 
            Dictionary<string, double> inputData)
        {
            var routine = new SampleRoutine(simulationConfiguration, inputData);
            return Task.FromResult(routine.PerformSimulation());
        }
    }

    public class SampleSimulationRunner :
        RoutineRunnerBase<TestFileState, TestConfigurationState, SimulationConfigurationWithRoutine>
    {
        internal const string connectorName = "integration-tests-connector";
        public bool MetadataInitialized { get; private set; }

        public Dictionary<string, long> AlreadyProcessed => EventsAlreadyProcessed;

        public SampleSimulationRunner(
            CogniteDestination cdf,
            ModeLibraryTest modelLibrary,
            ConfigurationLibraryTest configLibrary,
            SampleSimulatorClient client,
            ConnectorConfig config,
            ILogger<SampleSimulationRunner> logger) :
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
        }

        protected override void InitSimulationEventMetadata(
            TestFileState modelState,
            TestConfigurationState configState,
            SimulationConfigurationWithRoutine configObj,
            Dictionary<string, string> metadata)
        {
            MetadataInitialized = true;
        }
    }
}