using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognite.Simulator.Utils;
using NCrontab;
using System.Runtime.InteropServices.ComTypes;

namespace Cognite.Simulator.Utils
{

    /// <summary>
    /// Represents a scheduled job for simulation.
    /// </summary>
    /// <typeparam name="U">The type of configuration state.</typeparam>
    /// <typeparam name="V">The type of simulation configuration with data sampling.</typeparam>
    public class ScheduledJob<U, V> where U : ConfigurationStateBase where V : SimulationConfigurationWithDataSampling
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
        /// The calculation name
        /// </summary>
        public string CalculationName { get; set; }

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
        /// The configuration state.
        /// </summary>
        public U ConfigState { get; set; }

        /// <summary>
        /// The calculation configuration.
        /// </summary>
        public V Config { get; set; }
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
    public class SimulationSchedulerBase<U, V> 
        where U : ConfigurationStateBase
        where V : SimulationConfigurationWithDataSampling
    {
        private readonly ConnectorConfig _config;
        private readonly IConfigurationProvider<U, V> _configLib;
        private readonly ILogger _logger;
        private readonly CogniteDestination _cdf;
        private readonly IList<SimulatorConfig> _simulators;
        /// <summary>
        /// Creates a new instance of a simulation scheduler
        /// </summary>
        /// <param name="config">Connector configuration</param>
        /// <param name="configLib">Simulation configuration library</param>
        /// <param name="logger">Logger</param>
        /// <param name="simulators">List of simulators</param>
        /// <param name="cdf">CDF client</param>
        public SimulationSchedulerBase(
            ConnectorConfig config,
            IConfigurationProvider<U, V> configLib,
            ILogger logger,
            IList<SimulatorConfig> simulators,
            CogniteDestination cdf)
        {
            _configLib = configLib;
            _logger = logger;
            _simulators = simulators;
            _cdf = cdf;
            _config = config;
        }

        private async Task<IEnumerable<SimulationRun>> CreateSimulationEventReadyToRun(
            IEnumerable<SimulationEvent> simulationEvents,
            CancellationToken token)
        {
            if (simulationEvents == null || !simulationEvents.Any())
            {
                return Enumerable.Empty<SimulationRun>();
            }

            var runsToCreate = simulationEvents.Select(e => {
                var runType = e.RunType == "scheduled" ? SimulationRunType.scheduled : e.RunType == "manual" ? SimulationRunType.manual : SimulationRunType.external;
                return new SimulationRunCreate(){
                    RoutineExternalId = e.Calculation.Name,
                    RunType = runType,
                };
        }).ToList();
            List<SimulationRun> runs = new List<SimulationRun>();

            foreach (SimulationRunCreate runToCreate in runsToCreate)
            {
                var run = await _cdf.CogniteClient.Alpha.Simulators.CreateSimulationRunsAsync(
                    items: new List<SimulationRunCreate> { runToCreate },
                    token: token
                ).ConfigureAwait(false);
                runs.AddRange(run);
            }

            return runs;
        }

        public async Task RunCron(CancellationToken token) {
            var interval = TimeSpan.FromSeconds(_config.SchedulerUpdateInterval);
            Dictionary<string,ScheduledJob<U,V>> scheduledJobs = new Dictionary<string, ScheduledJob<U,V>>();
            var tolerance = TimeSpan.FromSeconds(_config.SchedulerTolerance);
            
            var simulatorsDictionary = _simulators?.ToDictionary(s => s.Name, s => s.DataSetId);
            var connectorIdList = CommonUtils.ConnectorsToExternalIds(simulatorsDictionary, _config.GetConnectorName());
        
            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    // Check for new schedules
                    var configurations = _configLib.SimulationConfigurations.Values
                        .GroupBy(c => c.CalculationName)
                        .Select(x => x.OrderByDescending(c => c.CreatedTime).First());
                    
                    foreach (var config in configurations)
                    {
                        U configState = _configLib.GetSimulationConfigurationState(
                            config.ExternalId
                        );

                        // Check if the configuration has a schedule for this connector.
                        if (configState == null ||
                            !connectorIdList.Contains(config.Connector) ||
                            config.Schedule == null )
                        {
                            continue;
                        }

                        // Check if the job already exists and if it should be cancelled due 
                        // to a new configuration on the API
                        if (scheduledJobs.TryGetValue(config.CalculationName, out var job))
                        {
                            if (config.CreatedTime > job.CreatedTime && job.TokenSource != null && !job.TokenSource.Token.IsCancellationRequested)
                            {
                                _logger.LogDebug($"Cancelling job for Calculation : {config.CalculationName} due to new configuration detected.");
                                job.TokenSource.Cancel();
                                scheduledJobs.Remove(config.CalculationName);
                            }
                        }

                        if ( !scheduledJobs.TryGetValue(config.CalculationName, out var existingJob)) {
                            try
                            {
                                if (config.Schedule.Enabled == false)
                                {
                                    continue;   
                                }
                                // Create new job
                                // */5 * * * * =>
                                var schedule = CrontabSchedule.Parse(config.Schedule.Repeat);
                                var newJob = new ScheduledJob<U, V>
                                {
                                    Schedule = schedule ,
                                    CalculationName = config.CalculationName,
                                    TokenSource = new CancellationTokenSource(),
                                    CreatedTime = config.CreatedTime,
                                    ConfigState = configState,
                                    Config = config

                                };
                                _logger.LogDebug("Created new job for schedule: {0} with id {1}", config.Schedule.Repeat, config.ExternalId);
                                scheduledJobs.Add(config.CalculationName, newJob);
                            }
                            catch (Exception e)
                            {
                               _logger.LogError($"Exception while scheduling job for Calculation : {job.CalculationName} Error: {e.Message}. Skipping.");
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
                        Task.WhenAll(tasks);
                    } 
                    // Wait for 10 seconds before checking again
                    await Task.Delay(interval);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs a scheduled job.
        /// </summary>
        /// <param name="job">The scheduled job to run.</param>
        /// <param name="mainToken">The cancellation token.</param>
        public async Task RunJob(ScheduledJob<U,V> job, CancellationToken mainToken)
        {
            if (job == null)
            {
                _logger.LogError($"Scheduled Job is null. Exiting.");
                return;
            }
            while (!mainToken.IsCancellationRequested || !job.TokenSource.Token.IsCancellationRequested)
            {
                var nextOccurrence = job.Schedule.GetNextOccurrence(DateTime.Now);
                var delay = nextOccurrence - DateTime.Now;
                // Retrieve the last run time saved in the calculation state, or use the start date
                // if no run was saved in the state
                // if (job.ConfigState.LastRun.HasValue ) {
                //     var lastRun = CogniteTime.FromUnixTimeMilliseconds(job.ConfigState.LastRun.Value) ;
                    
                // }
                if (delay.TotalMilliseconds > 0)
                {
                    bool calcExists = await _configLib
                        .VerifyLocalConfigurationState(job.ConfigState, job.Config, mainToken)
                        .ConfigureAwait(false);
                    if (!calcExists)
                    {
                        _logger.LogDebug($"Job not found for calculation name: {job.CalculationName} breaking out of loop");
                        break;
                    }
                    var runEvent = CreateRunEvent(job.ConfigState, job.Config);
                    await CreateSimulationEventReadyToRun(new List<SimulationEvent> { runEvent }, job.TokenSource.Token).ConfigureAwait(false);
                    try
                    {
                        await Task.Delay(delay, job.TokenSource.Token).ConfigureAwait(false);
                        _logger.LogDebug($"Job executed at: {DateTime.Now} for calculation: {job.CalculationName}");
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.LogDebug($"Job cancelled for calculation: {job.CalculationName} breaking out of loop");
                        break;
                    }
                }
            }
        }


        /// <summary>
        /// Starts the scheduler loop. For the existing simulation configuration files,
        /// check the schedule and create simulation events in CDF accordingly
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Run(CancellationToken token)
        {
            _logger.LogInformation("Starting simulation scheduler");
            await RunCron(token);
        }
        private SimulationEvent CreateRunEvent(U calcState, V calcConfig)
        {
            // print the SimulationEvent object
            _logger.LogInformation($"Calculation : {calcConfig.Calculation}");
            _logger.LogInformation($"Connector : {_config.GetConnectorName()}");
            _logger.LogInformation($"Calculation Id : {calcState.Id}");
            _logger.LogInformation($"DataSet Id : {calcState.DataSetId}");
            _logger.LogInformation($"Run Type : scheduled");
            _logger.LogInformation($"User Email : {calcConfig.UserEmail}");
            _logger.LogInformation($"Simulator : {calcConfig.Calculation.Model.Simulator}");
            return new SimulationEvent
            {
                Calculation = calcConfig.Calculation,
                Connector = _config.GetConnectorName(),
                CalculationId = calcState.Id,
                DataSetId = calcState.DataSetId,
                RunType = "scheduled",
                UserEmail = calcConfig.UserEmail
            };
        }
    }
}
