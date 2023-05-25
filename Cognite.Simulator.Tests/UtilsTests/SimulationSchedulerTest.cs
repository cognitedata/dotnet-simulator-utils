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
    }

    public class SampleSimulationScheduler :
        SimulationSchedulerBase<TestConfigurationState, SimulationConfigurationWithRoutine>
    {
        public SampleSimulationScheduler(
            ConfigurationLibraryTest configLib, 
            ILogger<SampleSimulationScheduler> logger, 
            CogniteDestination cdf) : base(
                new ConnectorConfig
                {
                    NamePrefix = "scheduler-test-connector",
                    AddMachineNameSuffix = false,
                    SchedulerUpdateInterval = 2
                }, 
                configLib, 
                logger, 
                cdf)
        {
        }
    }
}
