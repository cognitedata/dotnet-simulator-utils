using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using NCrontab;

using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// A wrapper around the .NET Task.Delay method.
    /// This is useful for testing purposes, where the delay can be faked.
    /// </summary>
    public interface ITimeManager
    {
        /// <summary>
        /// Delays the current thread for a specified time.
        /// </summary>
        Task Delay(TimeSpan delay, CancellationToken token);
    }

    /// <summary>
    /// Default implementation of the time manager.
    /// </summary>
    public class TimeManager : ITimeManager
    {
        /// <summary>
        /// Delays the current thread for a specified time.
        /// </summary>
        public async Task Delay(TimeSpan delay, CancellationToken token)
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Represents a scheduled job for simulation.
    /// (No longer used since scheduling is now done in a single loop.)
    /// </summary>
    /// <typeparam name="V">Type of the simulator routine revision</typeparam>
    public class ScheduledJob<V> where V : SimulatorRoutineRevision
    {
        public CrontabSchedule Schedule { get; set; }
        public Task Task { get; set; }
        public CancellationTokenSource TokenSource { get; set; }
        public bool Scheduled { get; set; }
        public long CreatedTime { get; set; }
        public V RoutineRevision { get; set; }
    }

    /// <summary>
    /// This class implements a basic simulation scheduler. Instead of creating a separate task
    /// per routine revision it now runs one task that triggers every minute.
    /// In each iteration the scheduler checks all routine configurations, and if the current time (with
    /// a given tolerance) is at or past the next scheduled occurrence and the routine has not been
    /// triggered yet, it creates a simulation run in CDF.
    /// </summary>
    public class SimulationSchedulerBase<V>
        where V : SimulatorRoutineRevision
    {
        private readonly ConnectorConfig _config;
        private readonly IRoutineProvider<V> _configLib;
        private readonly ILogger _logger;
        private readonly CogniteDestination _cdf;
        private readonly ITimeManager _timeManager;
        /// <summary>
        /// Creates a new instance of a simulation scheduler.
        /// </summary>
        /// <param name="config">Connector configuration</param>
        /// <param name="configLib">Simulation configuration library</param>
        /// <param name="logger">Logger</param>
        /// <param name="timeManager">Time manager. Not required, will default to <see cref="TimeManager"/></param>
        /// <param name="cdf">CDF client</param>
        /// <param name="timeManager">Time manager. Not required, will default to <see cref="TimeManager"/></param>
        public SimulationSchedulerBase(
            ConnectorConfig config,
            IRoutineProvider<V> configLib,
            ILogger logger,
            CogniteDestination cdf,
            ITimeManager timeManager = null)
        {
            _configLib = configLib;
            _logger = logger;
            _cdf = cdf;
            _config = config;
            _timeManager = timeManager ?? new TimeManager();
        }

        /// <summary>
        /// Parses a cron expression into a CrontabSchedule.
        /// </summary>
        /// <param name="cronExpression">The cron expression to parse.</param>
        /// <param name="logger">The logger to log errors.</param>
        /// <returns>The parsed CrontabSchedule, or null if parsing fails.</returns>
        public static CrontabSchedule ParseCronTabSchedule(string cronExpression, ILogger logger)
        {
            try
            {
                return CrontabSchedule.Parse(cronExpression);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error parsing cron expression {cronExpression} : error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the next occurrence of the schedule after the specified last run time.
        /// </summary>
        /// <param name="schedule">The cron schedule.</param>
        /// <param name="lastRun">The last run time.</param>
        /// <returns>The next occurrence of the schedule.</returns>
        public static DateTime GetNextOccurrence(CrontabSchedule schedule, DateTime lastRun) {
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }
            // Get the next occurrence of the schedule.
            var nextOccurrence = schedule.GetNextOccurrence(lastRun);
            return nextOccurrence;
        }

        /// <summary>
        /// Starts the scheduler loop. Every minute, this loop inspects all simulation configurations.
        /// If the cron expression indicates that a routine revision should run (within the allowed tolerance),
        /// a simulation run will be created in CDF.
        /// 
        /// To make sure a routine revision is not triggered multiple times for the same occurrence,
        /// a dictionary stores the last scheduled run time per routine revision.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Run(CancellationToken token)
        {
            // Run every minute.
            var schedulerInterval = TimeSpan.FromMinutes(1);
            var tolerance = TimeSpan.FromSeconds(_config.SchedulerTolerance);

            var connectorExternalId = _config.GetConnectorName();

            // This dictionary tracks, for each routine, the last time a simulation run was created.
            Dictionary<string, DateTime> lastRunTimes = new Dictionary<string, DateTime>();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Get latest routine revisions: for each routine external id, take the most recent revision.
                    var routineRevisions = _configLib.RoutineRevisions.Values
                        .GroupBy(c => c.RoutineExternalId)
                        .Select(x => x.OrderByDescending(c => c.CreatedTime).First());

                    foreach (var routineRev in routineRevisions)
                    {
                        // Check if the configuration has a schedule for this connector.
                        if (connectorExternalId != routineRev.SimulatorIntegrationExternalId || routineRev.Configuration.Schedule == null)
                        {
                            continue;
                        }

                        // Parse the cron schedule.
                        CrontabSchedule schedule;
                        try
                        {
                            schedule = ParseCronTabSchedule(routineRev.Configuration.Schedule.CronExpression, _logger);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error parsing cron expression {routineRev.Configuration.Schedule.CronExpression} " +
                                $"for routine: {routineRev.RoutineExternalId}, error: {ex.Message}");
                            continue;
                        }

                        // Get the last time this routine was triggered.
                        DateTime lastRun;
                        if (!lastRunTimes.TryGetValue(routineRev.RoutineExternalId, out lastRun))
                        {
                            lastRun = DateTime.MinValue;
                        }

                        // Compute the next scheduled occurrence from the last run time.
                        var nextOccurrence = GetNextOccurrence(schedule, lastRun);

                        // If the next occurrence is less or equal than the current UTC time (plus tolerance)
                        // and is greater than the last run time, trigger the simulation run.
                        if (nextOccurrence <= DateTime.UtcNow.Add(tolerance) && nextOccurrence > lastRun)
                        {
                            // Verify the routine revision exists in the in-memory cache.
                            bool revisionExists = await _configLib.VerifyInMemoryCache(routineRev, token)
                                .ConfigureAwait(false);
                            if (!revisionExists)
                            {
                                _logger.LogDebug($"Routine revision: {routineRev.RoutineExternalId} not found in cache, skipping.");
                                continue;
                            }

                            // Floor the run time to the nearest minute.
                            long runTime = nextOccurrence.ToUnixTimeMilliseconds();
                            runTime -= runTime % 60000;

                            var runEvent = new SimulationRunCreate
                            {
                                RoutineExternalId = routineRev.RoutineExternalId,
                                RunType = SimulationRunType.scheduled,
                                RunTime = runTime,
                            };

                            await _cdf.CogniteClient.Alpha.Simulators.CreateSimulationRunsAsync(
                                items: new List<SimulationRunCreate> { runEvent },
                                token: token
                            ).ConfigureAwait(false);

                            _logger.LogDebug($"Scheduled simulation run created for routine revision: {routineRev.ExternalId} at {nextOccurrence}.");

                            // Mark this routine revision as having run at the next occurrence.
                            lastRunTimes[routineRev.RoutineExternalId] = nextOccurrence;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error during scheduler loop: {ex.Message}");
                }

                try
                {
                    // Wait until the next minute tick.
                    await _timeManager.Delay(schedulerInterval, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    // Likely due to cancellation. Break out of the loop.
                    break;
                }
            }
        }

        /// <summary>
        /// (No longer used)
        /// Gets the next job delay and run time in milliseconds.
        /// </summary>
        public static (TimeSpan, long) GetNextJobDelayAndRunTimeMs(CrontabSchedule schedule)
        {
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }
            var now = DateTime.UtcNow;
            var nextOccurrence = schedule.GetNextOccurrence(now);
            var delay = nextOccurrence - now;

            // Floor run time to the nearest minute.
            var nextOccurrenceMs = nextOccurrence.ToUnixTimeMilliseconds();
            var nextJobRunTimeMs = nextOccurrenceMs - (nextOccurrenceMs % 60000);

            return (delay, nextJobRunTimeMs);
        }

        /// <summary>
        /// (No longer used)
        /// This method was originally used to run an individual scheduled job.
        /// </summary>
        private async Task RunJob(ScheduledJob<V> job, CancellationToken mainToken)
        {
            // This method is no longer used as the scheduler now runs in a single loop.
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    public class DefaultSimulationScheduler<TAutomationConfig> : SimulationSchedulerBase<SimulatorRoutineRevision>
        where TAutomationConfig : AutomationConfig, new()
    {
        public DefaultSimulationScheduler(
            DefaultConfig<TAutomationConfig> config,
            DefaultRoutineLibrary<TAutomationConfig> configLib,
            ILogger<DefaultSimulationScheduler<TAutomationConfig>> logger,
            CogniteDestination cdf)
            : base(config.Connector, configLib, logger, cdf)
        {
        }
    }
}