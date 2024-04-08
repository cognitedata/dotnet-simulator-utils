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

namespace Cognite.Simulator.Utils
{
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
        where V : SimulatorRoutineRevision
    {
        private readonly ConnectorConfig _config;
        private readonly IConfigurationProvider<U, V> _configLib;
        private readonly ILogger _logger;
        private readonly CogniteDestination _cdf;


        /// <summary>
        /// Creates a new instance of a simulation scheduler
        /// </summary>
        /// <param name="config">Connector configuration</param>
        /// <param name="configLib">Simulation configuration library</param>
        /// <param name="logger">Logger</param>
        /// <param name="cdf">CDF client</param>
        public SimulationSchedulerBase(
            ConnectorConfig config,
            IConfigurationProvider<U, V> configLib,
            ILogger logger,
            CogniteDestination cdf)
        {
            _configLib = configLib;
            _logger = logger;
            _cdf = cdf;
            _config = config;
        }

        private async Task CreateSimulationRunsReadyToRun(
            IEnumerable<SimulationRunCreate> runsToCreate,
            CancellationToken token)
        {
            if (runsToCreate == null || !runsToCreate.Any())
            {
                return;// Enumerable.Empty<SimulationRun>();
            }

        //     var runsToCreate = simulationEvents.Select(e => {
        //         var runType = e.RunType == "scheduled" ? SimulationRunType.scheduled : e.RunType == "manual" ? SimulationRunType.manual : SimulationRunType.external;
        //         return new SimulationRunCreate(){
        //             RoutineExternalId = e.Calculation.Name,
        //             RunType = runType,
        //         };
        // }).ToList();
            // List<SimulationRun> runs = new List<SimulationRun>();

            foreach (SimulationRunCreate runToCreate in runsToCreate)
            {
                var run = await _cdf.CogniteClient.Alpha.Simulators.CreateSimulationRunsAsync(
                    items: new List<SimulationRunCreate> { runToCreate },
                    token: token
                ).ConfigureAwait(false);
                // runs.AddRange(run);
            }

            // return runs;
        }


        /// <summary>
        /// Starts the scheduler loop. For the existing simulation configuration files,
        /// check the schedule and create simulation events in CDF accordingly
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Run(CancellationToken token)
        {
            var interval = TimeSpan.FromSeconds(_config.SchedulerUpdateInterval);
            var tolerance = TimeSpan.FromSeconds(_config.SchedulerTolerance);
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var eventsToCreate = new List<SimulationRunCreate>();
                var configurations = _configLib.SimulationConfigurations.Values
                    .GroupBy(c => c.RoutineExternalId)
                    .Select(x => x.OrderByDescending(c => c.CreatedTime).First());
                foreach (var configObj in configurations)
                {
                    U configState = _configLib.GetSimulationConfigurationState(
                        configObj.ExternalId
                    );

                    // Check if the configuration has a schedule enabled for this connector.
                    if (configState == null ||
                        configObj.SimulatorIntegrationExternalId != _config.GetConnectorName() ||
                        configObj.Configuration.Schedule == null ||
                        configObj.Configuration.Schedule.Enabled == false)
                    {
                        continue;
                    }
                    // var repeat = SimulationUtils.ConfigurationTimeStringToTimeSpan(configObj.Configuration.Schedule.Repeat); // frequency of simulations

                    // Retrieve the last run time saved in the calculation state, or use the start date
                    // // if no run was saved in the state
                    // var startDateTime = CogniteTime.FromUnixTimeMilliseconds(configObj.Configuration.Schedule.StartTime.Value); // TODO what's the default value if not set?
                    // var lastRun = configState.LastRun.HasValue ?
                    //     CogniteTime.FromUnixTimeMilliseconds(configState.LastRun.Value) : startDateTime - repeat;
                    // var nextRun = lastRun;

                    // // Determine if it is time to trigger the calculation. The calculation is triggered
                    // // if the deadline has passed, given the tolerance set 
                    // while (nextRun + repeat <= now)
                    // {
                    //     nextRun += repeat;
                    //     if (now >= nextRun && now <= (nextRun + tolerance))
                    //     {
                    //         bool calcExists = await _configLib
                    //             .VerifyLocalConfigurationState(configState, configObj, token)
                    //             .ConfigureAwait(false);
                    //         if (!calcExists)
                    //         {
                    //             break;
                    //         }
                    //         _logger.LogInformation("Scheduled simulation ready to run: {CalcName} - {CalcModel}",
                    //             configObj.RoutineExternalId,
                    //             configObj.ModelExternalId);

                    //         configState.LastRun = nextRun.ToUnixTimeMilliseconds(); // store state
                    //         // var runEvent = CreateRunEvent(configState, configObj); // create CDF event body
                    //         var runEvent = new SimulationRunCreate
                    //         {
                    //             RoutineExternalId = configObj.RoutineExternalId,
                    //             RunType = SimulationRunType.scheduled
                    //         };
                    //         eventsToCreate.Add(runEvent);
                    //         break;
                    //     }
                    // }
                }
                // create runs related to all scheduled routines in this iteration.
                try
                {
                    await CreateSimulationRunsReadyToRun(eventsToCreate, token).ConfigureAwait(false);
                }
                catch (CogniteException ex)
                {
                    _logger.LogError("Failed to create simulation run events in CDF: {Errors}",
                            string.Join(". ", ex.CogniteErrors.Select(e => e.Message)));
                }
                await Task.Delay(interval, token).ConfigureAwait(false);
            }
        }
        // private SimulationRunCreate CreateRunEvent(U calcState, V calcConfig)
        // {
        //     // return new SimulationEvent
        //     // {
        //     //     Calculation = calcConfig.RoutineExternalId,
        //     //     Connector = _config.GetConnectorName(),
        //     //     CalculationId = calcState.Id,
        //     //     DataSetId = calcState.DataSetId,
        //     //     RunType = "scheduled",
        //     //     UserEmail = calcConfig.UserEmail
        //     // };
        //     return 
        // }
    }
}
