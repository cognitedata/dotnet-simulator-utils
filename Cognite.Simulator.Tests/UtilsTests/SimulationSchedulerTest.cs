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

            /// prepopulate the routine revision
            var revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                cdf,
                SeedData.SimulatorRoutineCreateScheduled,
                SeedData.SimulatorRoutineRevisionCreateScheduled
            ).ConfigureAwait(false);

            try
            {
                await TestHelpers.SimulateProsperRunningAsync(cdf, "scheduler-test-connector").ConfigureAwait(false);

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
                    revision.Id.ToString(),
                    (IReadOnlyDictionary<string, TestConfigurationState>)configLib.State);
                var configObj = configLib.GetSimulationConfiguration(revision.ExternalId);
                Assert.NotNull(configObj);

                // Should have created at least one simulation event ready to run
                var simRuns = await cdf.Alpha.Simulators.ListSimulationRunsAsync(
                    new SimulationRunQuery
                    {
                        Filter = new SimulationRunFilter
                        {
                            // TODO: apply new filters
                            Status = SimulationRunStatus.ready,
                        },
                        Sort = new List<SimulatorSortItem>
                        {
                            new SimulatorSortItem
                            {
                                Property = "createdTime",
                                Order = SimulatorSortOrder.desc,
                            }
                        },
                    }, source.Token).ConfigureAwait(false);
                Assert.NotEmpty(simRuns.Items);

                // check if there are any simulation runs in the time span of the test
                // with the run type set to scheduled
                var latestEventsFiltered = simRuns.Items.Where(
                    r => r.CreatedTime >= testStartTimeMillis &&
                    r.SimulatorIntegrationExternalId == "scheduler-test-connector" && r.ModelRevisionExternalId == "PROSPER-Connector_Test_Model-2"
                );
                Assert.NotEmpty(latestEventsFiltered);
                Assert.Contains(latestEventsFiltered, e => e.RunType == SimulationRunType.scheduled);
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
