using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Extensions;
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
using CogniteSdk.Alpha;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class SimulationSchedulerTest
    {
        [Fact]
        public async Task TestSimulationSchedulerBase()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileDownloadClient>();
            services.AddSingleton<ConfigurationLibraryTest>();
            services.AddSingleton(new ConnectorConfig
            {
                NamePrefix = "scheduler-test-connector",
                AddMachineNameSuffix = false,
                SchedulerUpdateInterval = 2
            });
            services.AddSingleton<SampleSimulationScheduler>();

            StateStoreConfig stateConfig = null;

            List<string> eventIds = new List<string>();
            
            using var source = new CancellationTokenSource();
            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            try
            {
                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                var configLib = provider.GetRequiredService<ConfigurationLibraryTest>();
                var scheduler = provider.GetRequiredService<SampleSimulationScheduler>();

                await configLib.Init(source.Token).ConfigureAwait(false);

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(10));
                var taskList = new List<Task> { scheduler.Run(linkedToken) };
                taskList.AddRange(configLib.GetRunTasks(linkedToken));
                await taskList.RunAll(linkedTokenSource).ConfigureAwait(false);

                Assert.NotEmpty(configLib.State);
                var configState = Assert.Contains(
                    "PROSPER-SC-UserDefined-SST-Connector_Test_Model", // This simulator configuration should exist in CDF
                    (IReadOnlyDictionary<string, TestConfigurationState>)configLib.State);
                var configObj = configLib.GetSimulationConfiguration(
                    "PROSPER", "Connector Test Model", "UserDefined", "SST");
                Assert.NotNull(configObj);

                // Should have created at least one simulation event ready to run
                var events = await cdf.Events.FindSimulationEventsReadyToRun(
                    new Dictionary<string, long>
                    {
                        { configObj.Simulator, CdfTestClient.TestDataset }
                    },
                    configObj.Connector,
                    source.Token
                    ).ConfigureAwait(false);
                Assert.NotEmpty(events);
                Assert.Contains(events, e => e.Metadata.ContainsKey(
                    SimulationEventMetadata.RunTypeKey) && e.Metadata[SimulationEventMetadata.RunTypeKey] == "scheduled");
                eventIds.AddRange(events.Select(e => e.ExternalId));
            }
            finally
            {
                if (eventIds.Any())
                {
                    await cdf.Events.DeleteAsync(eventIds, source.Token)
                        .ConfigureAwait(false);
                }
                provider.Dispose(); // Dispose provider to also dispose managed services
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
        public async Task TestSimulationSchedulerBaseWithApi()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileDownloadClient>();
            services.AddSingleton<ConfigurationLibraryTest>();
            services.AddSingleton(new ConnectorConfig
            {
                NamePrefix = "scheduler-test-connector",
                AddMachineNameSuffix = false,
                UseSimulatorsApi = true,
                SchedulerUpdateInterval = 2,
            });
            services.AddSingleton<SampleSimulationScheduler>();

            StateStoreConfig stateConfig = null;

            List<string> eventIds = new List<string>();
            
            var testStartTimeMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using var source = new CancellationTokenSource();
            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var simint = new SimulatorIntegration () {
                Simulator = "PROSPER",
                DataSetId = CdfTestClient.TestDataset,
                ConnectorName = "scheduler-test-connector",
            };
            var simulators = new List<SimulatorIntegration> { simint };
            try
            {
                var integrations = await cdf.Sequences.GetOrCreateSimulatorIntegrations(
                    simulators,
                    CancellationToken.None).ConfigureAwait(false);

                var sequenceExternalId = integrations.First().ExternalId;
                
                // Update the sequence with connector heartbeat
                await cdf.Sequences.UpdateSimulatorIntegrationsHeartbeat(
                    sequenceExternalId,
                    true,
                    new SimulatorIntegrationUpdate
                    {
                        Simulator = simint.Simulator,
                        DataSetId = simint.DataSetId,
                        ConnectorName = simint.ConnectorName,
                        SimulatorApiEnabled = true,
                    },
                    CancellationToken.None).ConfigureAwait(false);

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                var configLib = provider.GetRequiredService<ConfigurationLibraryTest>();
                var scheduler = provider.GetRequiredService<SampleSimulationScheduler>();

                await configLib.Init(source.Token).ConfigureAwait(false);

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(10));
                var taskList = new List<Task> { scheduler.Run(linkedToken) };
                taskList.AddRange(configLib.GetRunTasks(linkedToken));
                await taskList.RunAll(linkedTokenSource).ConfigureAwait(false);

                Assert.NotEmpty(configLib.State);
                var configState = Assert.Contains(
                    "PROSPER-SC-UserDefined-SST-Connector_Test_Model",
                    (IReadOnlyDictionary<string, TestConfigurationState>)configLib.State);
                var configObj = configLib.GetSimulationConfiguration(
                    "PROSPER", "Connector Test Model", "Simulation Scheduler Test");
                Assert.NotNull(configObj);

                // Should have created at least one simulation event ready to run
                var simRuns = await cdf.Alpha.Simulators.ListSimulationRunsAsync(
                    new SimulationRunQuery
                    {
                        Filter = new SimulationRunFilter
                        {
                            ModelName = configObj.ModelName,
                            RoutineName = configObj.CalculationName,
                            SimulatorName = configObj.Simulator,
                            Status = SimulationRunStatus.ready
                        }
                    }, source.Token).ConfigureAwait(false);
                Assert.NotEmpty(simRuns.Items);

                // check if there are any simulation runs in the time span of the test
                Assert.Contains(simRuns.Items, r => r.CreatedTime > testStartTimeMillis && r.CreatedTime < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
            finally
            {
                if (eventIds.Any())
                {
                    await cdf.Events.DeleteAsync(eventIds, source.Token)
                        .ConfigureAwait(false);
                }
                provider.Dispose(); // Dispose provider to also dispose managed services
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
    }

    public class SampleSimulationScheduler :
        SimulationSchedulerBase<TestConfigurationState, SimulationConfigurationWithRoutine>
    {
        public SampleSimulationScheduler(
            ConfigurationLibraryTest configLib, 
            ConnectorConfig config,
            ILogger<SampleSimulationScheduler> logger, 
            CogniteDestination cdf) : base(
                config,
                configLib, 
                logger,
                cdf)
        {
        }
    }
}
