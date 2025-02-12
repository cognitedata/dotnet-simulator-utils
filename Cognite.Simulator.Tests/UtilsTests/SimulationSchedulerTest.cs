using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NCrontab;
using Xunit;

using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;
using CogniteSdk;
using CogniteSdk.Alpha;
using System.Diagnostics;

namespace Cognite.Simulator.Tests.UtilsTests
{
    /// <summary>
    /// Fake time manager for testing purposes
    /// Made so that the tests don't have to wait for the delay
    /// </summary>
    class FakeTimeManager : ITimeManager
    {
        ILogger<FakeTimeManager> _logger;

        public FakeTimeManager(
            ILogger<FakeTimeManager> logger
        )
        {
            _logger = logger;
        }

        // Only delay for 1000ms instead of the given delay
        public Task Delay(TimeSpan delay, CancellationToken token)
        {
            _logger.LogWarning("Using fake delay, delaying for 1000ms instead of {delay}ms", delay.TotalMilliseconds);
            return Task.Delay(1000, token);
        }
    }

    /// <summary>
    /// Fake local time zone for testing purposes
    /// </summary>
    public class FakeLocalTimeZone : IDisposable
    {
        private readonly TimeZoneInfo _actualLocalTimeZoneInfo;
        private static void SetLocalTimeZone(TimeZoneInfo timeZoneInfo)
        {
            var cachedDataField = typeof(TimeZoneInfo).GetField("s_cachedData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var cachedData = cachedDataField?.GetValue(null);
            var localTimeZoneField = cachedData?.GetType().GetField("_localTimeZone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            localTimeZoneField?.SetValue(cachedData, timeZoneInfo);
        }

        public FakeLocalTimeZone(TimeZoneInfo timeZoneInfo)
        {
            _actualLocalTimeZoneInfo = TimeZoneInfo.Local;
            SetLocalTimeZone(timeZoneInfo);
        }

        public void Dispose()
        {
            SetLocalTimeZone(_actualLocalTimeZoneInfo);
        }
    }

    [Collection(nameof(SequentialTestCollection))]
    public class SimulationSchedulerTest
    {

        [Fact]
        public void TestNextRunTimeAndDelay_Midnight() {
            using (new FakeLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("US/Eastern"))) {
                // every day at 00:00
                var schedule = CrontabSchedule.Parse("0 0 * * *");
                var (delay, runTimeMs) = SimulationSchedulerBase<SimulatorRoutineRevision>.GetNextJobDelayAndRunTimeMs(schedule);

                // get the next midnight
                var now = DateTime.UtcNow;
                var nowDate = DateTime.UtcNow.Date;
                var nextMidnight =  nowDate.AddDays(1);
                var nextMidnightMs = nextMidnight.ToUnixTimeMilliseconds();
                var expectedDelay = nextMidnight - now;

                var diffBetweenExpectedAndActual = (expectedDelay - delay).TotalMilliseconds;
                
                Assert.True(Math.Abs(diffBetweenExpectedAndActual) < 60 * 1000); // less than a minute
                Assert.Equal(nextMidnightMs, runTimeMs);
            }
        }

        [Fact]
        public void TestNextRunTimeAndDelay_In15Minutes() {
            using (new FakeLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"))) {
                // get current time
                var now = DateTime.UtcNow;
                var nowPlus15Minutes = now.AddMinutes(15);
                
                // cron expression at every day current UTC time + 15 minutes
                var schedule = CrontabSchedule.Parse($"{nowPlus15Minutes.Minute} {nowPlus15Minutes.Hour} * * *");
                var (delay, runTimeMs) = SimulationSchedulerBase<SimulatorRoutineRevision>.GetNextJobDelayAndRunTimeMs(schedule);

                // get the next midnight
                var expectedRunTime = nowPlus15Minutes.ToUnixTimeMilliseconds() / 60000 * 60000; // round to the nearest minute
                var expectedDelay = nowPlus15Minutes - now;

                var diffBetweenExpectedAndActual = (expectedDelay - delay).TotalMilliseconds;
                
                Assert.True(Math.Abs(diffBetweenExpectedAndActual) < 1000 * 60); // less than a minute
                Assert.Equal(expectedRunTime, runTimeMs);
            }
        }

        [Fact]
        public void ParseCronExpression_ShouldCompleteWithinThreshold()
        {
            const int iterations = 1000;
            // Example cron expression: "*/5 * * * *" means every 5 minutes.
            string cronExpression = "*/5 * * * *";
            
            // Warm-up: parse once so that any JIT overhead is minimized.
            var schedule = CrontabSchedule.Parse(cronExpression);

            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                schedule = CrontabSchedule.Parse(cronExpression);
            }

            stopwatch.Stop();

            // Output the performance measurement
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            Console.WriteLine($"Parsed cron expression {iterations} times in {elapsedMs}ms.");

            // Example assertion: if parsing takes more than 1000ms overall, the test will fail.
            // You can set this threshold to any value that meets your performance requirements.
            Assert.True(elapsedMs < 1000, $"Parsing took too long: {elapsedMs}ms.");
        }

        [Fact]
        public async Task TestSimulationSchedulerBase()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<RoutineLibraryTest>();
            services.AddSingleton(new ConnectorConfig
            {
                NamePrefix = SeedData.TestIntegrationExternalId,
                AddMachineNameSuffix = false,
                SchedulerUpdateInterval = 2,
                DataSetId = SeedData.TestDataSetId,
            });
            services.AddSingleton(SeedData.SimulatorCreate);
            services.AddSingleton<ITimeManager, FakeTimeManager>();
            services.AddSingleton<SampleSimulationScheduler>();

            StateStoreConfig stateConfig = null;

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
                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                var configLib = provider.GetRequiredService<RoutineLibraryTest>();
                var scheduler = provider.GetRequiredService<SampleSimulationScheduler>();

                await configLib.Init(source.Token).ConfigureAwait(false);

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(6));
                var taskList = new List<Task> { scheduler.Run(linkedToken) };
                taskList.AddRange(configLib.GetRunTasks(linkedToken));
                await taskList.RunAll(linkedTokenSource).ConfigureAwait(false);

                var configObj = configLib.GetRoutineRevision(revision.ExternalId);
                Assert.NotNull(configObj);

                // Should have created at least one simulation run ready to be executed
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

                Assert.Equal(SeedData.TestModelExternalId, firstEvent.ModelExternalId);

                // check if there are any simulation runs in the time span of the test
                // with the run type set to scheduled
                var latestEventsFiltered = simRuns.Items.Where(
                    r => r.CreatedTime >= testStartTimeMillis && r.RunType == SimulationRunType.scheduled
                );
                // should create at least 4 events IN 5 seconds
                Assert.NotEmpty(latestEventsFiltered);
                Assert.InRange(latestEventsFiltered.Count(), 4, 6);
            }
            finally
            {
                await SeedData.DeleteSimulator(cdf, SeedData.SimulatorCreate.ExternalId ).ConfigureAwait(false);

                provider.Dispose(); // Dispose provider to also dispose managed services
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
            }
        }
    }

    public class SampleSimulationScheduler :
        SimulationSchedulerBase<SimulatorRoutineRevision>
    {
        public SampleSimulationScheduler(
            RoutineLibraryTest configLib, 
            ConnectorConfig config,
            ILogger<SampleSimulationScheduler> logger, 
            CogniteDestination cdf,
            ITimeManager timeManager
            ) : base(
                config,
                configLib, 
                logger,
                cdf,
                timeManager)
        {
        }
    }
}
