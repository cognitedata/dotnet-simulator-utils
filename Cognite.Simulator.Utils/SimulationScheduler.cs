using Cognite.Extractor.Utils;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NCrontab;

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
    /// </summary>
    /// <typeparam name="V">Type of the simulator routine revision</typeparam>
    public class ScheduledJob<V> where V : SimulatorRoutineRevision
    {
        /// <summary>
        /// The schedule for the job.
        /// </summary>        
        public CrontabSchedule Schedule { get; set; }

        /// <summary>
        /// The Task of the scheduled job.
        /// </summary>
        public Task Task { get; set; }

        /// <summary>
        /// The token source for the job, will be used to cancel it.
        /// </summary>
        public CancellationTokenSource TokenSource { get; set; }

        /// <summary>
        /// Whether the job was started on the scheduler or not.
        /// </summary>
        public bool Scheduled { get; set; }

        /// <summary>
        /// The time the job was created.
        /// </summary>
        public long CreatedTime { get; set; }

        /// <summary>
        /// Routine revision.
        /// </summary>
        public V RoutineRevision { get; set; }
    }
    /// <summary>
    /// This class implements a basic simulation scheduler. It runs a loop on a configurable interval.
    /// Each iteration, it checks the schedules for all configurations and determine if the simulation
    /// should be triggered.
    /// It is assumed that the simulator can only run one simulation at a time, and therefore there is no
    /// need to schedule parallel simulation events.
    /// Alternatives to this implementation include libraries such as Quartz, but the added complexity of
    /// a full fledged scheduling library in not necessary at this point.
    /// Also, at some point scheduling the creation of CDF events should be done by a cloud service, instead
    /// of doing it in the connector.
    /// </summary>
    public class SimulationSchedulerBase<V> 
        where V : SimulatorRoutineRevision
    {
        private readonly ConnectorConfig _config;
        private readonly IRoutineProvider<V> _configLib;
        private readonly ILogger _logger;
        private readonly CogniteDestination _cdf;
        private readonly ITimeManager _timeManager;
        private readonly IEnumerable<SimulatorConfig> _simulators;
        /// <summary>
        /// Creates a new instance of a simulation scheduler
        /// </summary>
        /// <param name="config">Connector configuration</param>
        /// <param name="configLib">Simulation configuration library</param>
        /// <param name="logger">Logger</param>
        /// <param name="simulators">List of simulators</param>
        /// <param name="timeManager">Time manager. Not required, will default to <see cref="TimeManager"/></param>
        /// <param name="cdf">CDF client</param>
        public SimulationSchedulerBase(
            ConnectorConfig config,
            IRoutineProvider<V> configLib,
            ILogger logger,
            IEnumerable<SimulatorConfig> simulators,
            CogniteDestination cdf,
            ITimeManager timeManager = null)
        {
            _configLib = configLib;
            _logger = logger;
            _simulators = simulators;
            _cdf = cdf;
            _config = config;
            _timeManager = timeManager ?? new TimeManager();
        }

        /// <summary>
        /// Starts the scheduler loop. For the existing simulation configuration files,
        /// check the schedule and create simulation events in CDF accordingly
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Run(CancellationToken token) {
            var interval = TimeSpan.FromSeconds(_config.SchedulerUpdateInterval);
            Dictionary<string,ScheduledJob<V>> scheduledJobs = new Dictionary<string, ScheduledJob<V>>();
            var tolerance = TimeSpan.FromSeconds(_config.SchedulerTolerance);
            
            var simulatorsDictionary = _simulators?.ToDictionary(s => s.Name, s => s.DataSetId);
            var connectorIdList = CommonUtils.ConnectorsToExternalIds(simulatorsDictionary, _config.GetConnectorName());
        
            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    // Check for new schedules
                    var routineRevisions = _configLib.RoutineRevisions.Values
                        .GroupBy(c => c.RoutineExternalId)
                        .Select(x => x.OrderByDescending(c => c.CreatedTime).First());
                    
                    foreach (var routineRev in routineRevisions)
                    {
                        // Check if the configuration has a schedule for this connector.
                        if (!connectorIdList.Contains(routineRev.SimulatorIntegrationExternalId) ||
                            routineRev.Configuration.Schedule == null )
                        {
                            continue;
                        }

                        // Check if the job already exists and if it should be cancelled due 
                        // to a new configuration on the API
                        if (scheduledJobs.TryGetValue(routineRev.RoutineExternalId, out var job))
                        {
                            if (routineRev.CreatedTime > job.CreatedTime && job.TokenSource != null && !job.TokenSource.Token.IsCancellationRequested)
                            {
                                _logger.LogDebug($"Cancelling job for Calculation : {routineRev.ExternalId} due to new configuration detected.");
                                job.TokenSource.Cancel();
                                scheduledJobs.Remove(routineRev.RoutineExternalId);
                            }
                        }

                        if (!scheduledJobs.TryGetValue(routineRev.RoutineExternalId, out var existingJob)) {
                            try
                            {
                                if (routineRev.Configuration.Schedule.Enabled == false)
                                {
                                    continue;   
                                }
                                var schedule = CrontabSchedule.Parse(routineRev.Configuration.Schedule.CronExpression);
                                var newJob = new ScheduledJob<V>
                                {
                                    Schedule = schedule,
                                    TokenSource = new CancellationTokenSource(),
                                    CreatedTime = routineRev.CreatedTime,
                                    RoutineRevision = routineRev,
                                };
                                _logger.LogDebug("Created new job for schedule: {0} with id {1}", routineRev.Configuration.Schedule.CronExpression, routineRev.ExternalId);
                                scheduledJobs.Add(routineRev.RoutineExternalId, newJob);
                            }
                            catch (Exception e)
                            {
                               _logger.LogError($"Exception while scheduling job for Calculation : {job.RoutineRevision.ExternalId} Error: {e.Message}. Skipping.");
                            }
                        }
                    }

                    // Schedule new jobs
                    List<Task> tasks = new List<Task>();
                    foreach (var kvp in scheduledJobs)
                    {
                        if (kvp.Value.Scheduled)
                        {
                            continue;
                        }
                        tasks.Add(RunJob(kvp.Value, token));
                        kvp.Value.Scheduled = true;
                    }
                    if (tasks.Count != 0)
                    {
                        _ = Task.WhenAll(tasks);
                    } 
                    // Wait for interval seconds before checking again
                    await Task.Delay(interval).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs a scheduled job.
        /// </summary>
        /// <param name="job">The scheduled job to run.</param>
        /// <param name="mainToken">The cancellation token.</param>
        public async Task RunJob(ScheduledJob<V> job, CancellationToken mainToken)
        {
            if (job == null)
            {
                _logger.LogError($"Scheduled Job is null. Exiting.");
                return;
            }
            while (!mainToken.IsCancellationRequested || !job.TokenSource.Token.IsCancellationRequested)
            {
                var routineRev = job.RoutineRevision;
                var nextOccurrence = job.Schedule.GetNextOccurrence(DateTime.Now);
                var delay = nextOccurrence - DateTime.Now;
                if (delay.TotalMilliseconds > 0)
                {
                    try
                    {
                        await _timeManager.Delay(delay, job.TokenSource.Token).ConfigureAwait(false);
                        _logger.LogDebug($"Job executed at: {DateTime.Now} for routine revision: {routineRev.ExternalId}");
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.LogDebug($"Job cancelled for routine revision: {routineRev.ExternalId} breaking out of loop");
                        break;
                    }
                    bool revisionExists = await _configLib
                        .VerifyInMemoryCache(routineRev, mainToken)
                        .ConfigureAwait(false);
                    if (!revisionExists)
                    {
                        _logger.LogDebug($"Job not found for routine: {routineRev.RoutineExternalId} breaking out of loop");
                        break;
                    }
                    var runEvent = new SimulationRunCreate
                        {
                            RoutineExternalId = routineRev.RoutineExternalId,
                            RunType = SimulationRunType.scheduled,
                            RunTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                    await _cdf.CogniteClient.Alpha.Simulators.CreateSimulationRunsAsync(
                        items: new List<SimulationRunCreate> { runEvent },
                        token: job.TokenSource.Token
                    ).ConfigureAwait(false);
                }
            }
        }
    }
}
