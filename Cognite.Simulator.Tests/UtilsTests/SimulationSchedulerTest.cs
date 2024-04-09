﻿using Cognite.Extractor.StateStorage;
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
using Moq;

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
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<ConfigurationLibraryTest>();
            services.AddSingleton(new ConnectorConfig
            {
                NamePrefix = SeedData.TestIntegrationExternalId,
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
            var FileStorageClient = provider.GetRequiredService<FileStorageClient>();
            
            await SeedData.GetOrCreateSimulator(cdf, SeedData.SimulatorCreate).ConfigureAwait(false);
            await TestHelpers.SimulateASimulatorRunning(cdf, SeedData.TestIntegrationExternalId).ConfigureAwait(false);

            /// prepopulate the routine revision
            var revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                cdf,
                FileStorageClient,
                SeedData.SimulatorRoutineCreateScheduled,
                SeedData.SimulatorRoutineRevisionCreateScheduled
            ).ConfigureAwait(false);

            // this helps diagnose issues where the above function is giving an old revision
            Assert.Equal(SeedData.SimulatorRoutineRevisionCreateScheduled.Configuration.Schedule.CronExpression, revision.Configuration.Schedule.CronExpression);
            try
            {
                var mockTimeManager = new Mock<ITimeManager>();
                mockTimeManager.Setup(m => m.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                    .Returns<TimeSpan, CancellationToken>((delay, token) => Task.Delay(1000).ContinueWith(_ => { }, token));



                var desiredCurrentTime = DateTime.Now; // Example desired current time
                    mockTimeManager.Setup(m => m.GetCurrentTime()).Returns(desiredCurrentTime);

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                var configLib = provider.GetRequiredService<ConfigurationLibraryTest>();
                var scheduler = provider.GetRequiredService<SampleSimulationScheduler>();

                await configLib.Init(source.Token).ConfigureAwait(false);

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5));
                var taskList = new List<Task> { scheduler.Run(linkedToken, mockTimeManager.Object) };
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
                            SimulatorIntegrationExternalIds = new List<string> { SeedData.TestIntegrationExternalId },
                            SimulatorExternalIds = new List<string> { SeedData.TestSimulatorExternalId },
                            Status = SimulationRunStatus.ready,
                            // ModelRevisionExternalIds = new List<string> { "PETEX-Connector_Test_Model" },
                        },
                        Sort = new List<SimulatorSortItem>
                        {
                            new SimulatorSortItem
                            {
                                Property = "createdTime",
                                Order = SimulatorSortOrder.desc,
                            }
                        },
                        Limit = 20,
                    }, source.Token).ConfigureAwait(false);
                Assert.NotEmpty(simRuns.Items);

                var firstEvent = simRuns.Items.First();

                Assert.Equal("PETEX-Connector_Test_Model" , firstEvent.ModelExternalId);

                // check if there are any simulation runs in the time span of the test
                // with the run type set to scheduled
                var latestEventsFiltered = simRuns.Items.Where(
                    r => r.CreatedTime >= testStartTimeMillis && r.RunType == SimulationRunType.scheduled
                );
                // should create atleast 4 events IN 5 seconds
                Assert.Equal(5, latestEventsFiltered.Count());
                Assert.NotEmpty(latestEventsFiltered);
                Assert.Contains(latestEventsFiltered, e => e.RunType == SimulationRunType.scheduled);
            }
            finally
            {
                await SeedData.DeleteSimulator(cdf, SeedData.SimulatorCreate.ExternalId ).ConfigureAwait(false);

                if (eventIds.Any())
                {
                    await cdf.Events.DeleteAsync(eventIds, source.Token)
                        .ConfigureAwait(false);
                }
                provider.Dispose(); // Dispose provider to also dispose managed services
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
                null,
                cdf)
        {
        }
    }
}
