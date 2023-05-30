using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils;
using CogniteSdk;
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

            StateStoreConfig stateConfig = null;

            string eventId = "";
            string sequenceId = "";

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
                Assert.True(runner.SimulationEventExecuted);

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

    public class SampleSimulationRunner :
        SimulationRunnerBase<TestFileState, TestConfigurationState, SimulationConfigurationWithDataSampling>
    {
        private const string connectorName = "integration-tests-connector";
        public bool MetadataInitialized { get; private set; }
        public bool SimulationEventExecuted { get; private set; }

        public Dictionary<string, long> AlreadyProcessed => EventsAlreadyProcessed;

        public SampleSimulationRunner(
            CogniteDestination cdf,
            ModeLibraryTest modelLibrary,
            ConfigurationLibraryTest configLibrary,
            ILogger<SampleSimulationRunner> logger) :
            base(
                new ConnectorConfig
                {
                    NamePrefix = connectorName,
                    AddMachineNameSuffix = false
                },
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
                logger)
        {
        }

        protected override void InitSimulationEventMetadata(
            TestFileState modelState,
            TestConfigurationState configState,
            SimulationConfigurationWithDataSampling configObj,
            Dictionary<string, string> metadata)
        {
            MetadataInitialized = true;
        }

        protected override Task RunSimulation(
            Event e,
            DateTime startTime,
            TestFileState modelState,
            TestConfigurationState configState,
            SimulationConfigurationWithDataSampling configObj,
            SamplingRange samplingRange,
            CancellationToken token)
        {
            // Real connectors should implement the actual simulation run here
            // and save the results back to CDF
            SimulationEventExecuted = true;
            return Task.CompletedTask;
        }
    }
}