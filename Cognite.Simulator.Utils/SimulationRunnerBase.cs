using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using CogniteSdk.Alpha;
using CogniteSdk.Resources;
using CogniteSdk.Resources.Alpha;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents the connector's simulation runner process. This base class can
    /// fetch simulation events from CDF that are ready to run, validate them and find
    /// the time range to sample data where the process is in steady state.
    /// </summary>
    /// <typeparam name="T">Type of model state objects</typeparam>
    /// <typeparam name="U">Type of simulation configuration state objects</typeparam>
    /// <typeparam name="V">Type of simulation configuration objects</typeparam>
    public abstract class SimulationRunnerBase<T, U, V>
        where T : ModelStateBase
        where U : FileState
        where V : SimulatorRoutineRevision
    {
        private readonly ConnectorConfig _connectorConfig;
        private readonly IList<SimulatorConfig> _simulators;
        private readonly EventsResource _cdfEvents;
        private readonly SimulatorsResource _cdfSimulators;
        private readonly SequencesResource _cdfSequences;
        private readonly DataPointsResource _cdfDataPoints;
        private readonly ILogger _logger;

        /// <summary>
        /// Library containing the simulator model files
        /// </summary>
        protected IModelProvider<T> ModelLibrary { get; }

        /// <summary>
        /// Library containing the simulation configuration files
        /// </summary>
        protected IRoutineProvider<V> RoutineLibrary { get; }

        private long? simulatorIntegrationId;


        /// <summary>
        /// Create a new instance of the runner with the provided parameters
        /// </summary>
        /// <param name="connectorConfig">Connector configuration</param>
        /// <param name="simulators">List of simulators</param>
        /// <param name="cdf">CDF client</param>
        /// <param name="modelLibrary">Model library</param>
        /// <param name="routineLibrary">Configuration library</param>
        /// <param name="logger">Logger</param>
        public SimulationRunnerBase(
            ConnectorConfig connectorConfig,
            IList<SimulatorConfig> simulators,
            CogniteDestination cdf,
            IModelProvider<T> modelLibrary,
            IRoutineProvider<V> routineLibrary,
            ILogger logger)
        {
            if (cdf == null)
            {
                throw new ArgumentNullException(nameof(cdf));
            }
            _connectorConfig = connectorConfig;
            _simulators = simulators;
            _cdfEvents = cdf.CogniteClient.Events;
            _cdfSimulators = cdf.CogniteClient.Alpha.Simulators;
            _cdfSequences = cdf.CogniteClient.Sequences;
            _cdfDataPoints = cdf.CogniteClient.DataPoints;
            _logger = logger;
            ModelLibrary = modelLibrary;
            RoutineLibrary = routineLibrary;
        }

        private async Task<SimulationRun> UpdateSimulationRunStatus(
            long runId,
            SimulationRunStatus status,
            string statusMessage,
            CancellationToken token,
            Dictionary<string, long> runConfiguration = null)
        {

            long? simulationTime = null;
            if(runConfiguration != null && runConfiguration.TryGetValue("simulationTime", out var simTime))
            {
                simulationTime = simTime;
            }

            var res = await _cdfSimulators.SimulationRunCallbackAsync(
                new SimulationRunCallbackItem()
                {
                    Id = runId,
                    Status = status,
                    StatusMessage = statusMessage,
                    SimulationTime = simulationTime
                }, token).ConfigureAwait(false);

            return res.Items.First();
        }

        private async Task<IEnumerable<SimulationRun>> FindSimulationRunsWithStatus(
            Dictionary<string, long> simulators,
            SimulationRunStatus status,
            CancellationToken token)
        {
            if (simulators == null || !simulators.Any())
            {
                return new List<SimulationRun>();
            }

            var connectorName = _connectorConfig.GetConnectorName();
            var listOfIntegrations = CommonUtils.ConnectorsToExternalIds(simulators, connectorName);

            var query = new SimulationRunQuery()
            {
                Filter = new SimulationRunFilter()
                {
                    Status = status,
                    SimulatorExternalIds = simulators.Keys.ToList(),
                    SimulatorIntegrationExternalIds = listOfIntegrations
                }
            };
            var runsResult = await _cdfSimulators
                .ListSimulationRunsAsync(query, token)
                .ConfigureAwait(false);

            return runsResult.Items;
        }

        private async Task<IEnumerable<SimulationRunEvent>> FindSimulationEvents(
            Dictionary<string, long> simulatorDataSetMap,
            SimulationRunStatus status,
            CancellationToken token)
        {
            var simulationRuns = await FindSimulationRunsWithStatus(
                simulatorDataSetMap,
                status, token).ConfigureAwait(false);
            return simulationRuns.Select(r => new SimulationRunEvent(r)).ToList();
        }

        /// <summary>
        /// Start the loop for fetching and processing simulation events from CDF
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Run(CancellationToken token)
        {
            var interval = TimeSpan.FromSeconds(_connectorConfig.FetchEventsInterval);
            while (!token.IsCancellationRequested)
            {
                var simulators = _simulators.ToDictionary(s => s.Name, s => s.DataSetId);
                // Find events that are ready to run
                var simulationEvents = await FindSimulationEvents(
                    simulators,
                    SimulationRunStatus.ready,
                    token).ConfigureAwait(false);
                if (simulationEvents.Any())
                {
                    _logger.LogInformation(
                        "{Number} simulation event(s) ready to run found in CDF",
                        simulationEvents.Count());
                }

                // Find events that are running. Should not have any, as the connector runs events in sequence.
                // Any running events indicates that the connector went down during the run, and the event should fail
                var simulationRunningEvents = await FindSimulationEvents(
                    simulators,
                    SimulationRunStatus.running,
                    token).ConfigureAwait(false);
                if (simulationRunningEvents.Any())
                {
                    _logger.LogWarning(
                        "{Number} simulation event(s) that are running (but should have finished) found in CDF",
                        simulationRunningEvents.Count());
                }
                var allEvents = new List<SimulationRunEvent>(simulationEvents);
                allEvents.AddRange(simulationRunningEvents);

                // sort by event time
                allEvents.Sort((e1, e2) =>
                {
                    return e1.Run.CreatedTime > e2.Run.CreatedTime ? -1 : 1;
                });
                foreach (SimulationRunEvent e in allEvents)
                {
                    var runId = e.Run.Id;
                    var startTime = DateTime.UtcNow;
                    T modelState = null;
                    V routineRev = null;
                    bool skipped = false;

                    var connectorIdList = CommonUtils.ConnectorsToExternalIds(simulators, _connectorConfig.GetConnectorName());

                    using (LogContext.PushProperty("LogId", e.Run.LogId)) {
                        try
                        {
                            (modelState, routineRev) = ValidateEventMetadata(e, connectorIdList);
                            if (routineRev == null || !connectorIdList.Contains(routineRev.SimulatorIntegrationExternalId) )
                            {
                                _logger.LogError("Skip simulation run that belongs to another connector: {Id} {Connector}",
                                runId,
                                routineRev?.SimulatorIntegrationExternalId);
                                skipped = true;
                                continue;
                            }

                            var metadata = new Dictionary<string, string>();
                            InitSimulationEventMetadata(
                                modelState,
                                routineRev,
                                metadata);
                            PublishSimulationRunStatus("RUNNING_CALCULATION", token);

                            await InitSimulationRun(
                                e,
                                startTime,
                                modelState,
                                routineRev,
                                metadata,
                                token)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            if (ex is ConnectorException ce && ce.Errors != null)
                            {
                                foreach (var error in ce.Errors)
                                {
                                    _logger.LogError(error.Message);
                                }
                            }
                            _logger.LogError("Calculation run failed with error: {Message}", ex);
                            e.Run = await UpdateSimulationRunStatus(
                                runId,
                                SimulationRunStatus.failure,
                                ex.Message == null || ex.Message.Length < 255 ? ex.Message : ex.Message.Substring(0, 254),
                                token,
                                e.RunConfiguration
                                ).ConfigureAwait(false);
                        }
                        finally
                        {
                            // the following check was added because the code below was running even for skipped events
                            if (!skipped)
                            {
                                _logger.LogDebug("Calculation run finished for run {Id}", runId);
                                PublishSimulationRunStatus("IDLE", token);
                            }
                        }
                    }
                }

                await Task.Delay(interval, token).ConfigureAwait(false);
            }
        }

        private (T, V) ValidateEventMetadata(SimulationRunEvent simEv, List<string> integrations)
        {
            string modelName = simEv.Run.ModelName;
            string simulator = simEv.Run.SimulatorName;
            string calcTypeUserDefined = simEv.Run.RoutineName;
            string eventId = simEv.Run.Id.ToString();
         
            var model = ModelLibrary.GetLatestModelVersion(simulator, modelName);
            if (model == null)
            {
                _logger.LogError("Could not find a local model file to run Simulation Event {Id}", eventId);
                throw new SimulationException($"Could not find a model file for {modelName}");
            }
            // U calcState = ConfigurationLibrary.GetSimulationConfigurationState(simEv.Run.RoutineRevisionExternalId);
            V calcConfig = RoutineLibrary.GetRoutineRevision(simEv.Run.RoutineRevisionExternalId);

            if (calcConfig == null)
            {
                _logger.LogError("Could not find a local configuration to run Simulation Event {Id}", eventId);
                throw new SimulationException($"Could not find a routine revision for model: {modelName} routineRevision: {calcTypeUserDefined}");
            }

            if (!integrations.Contains(calcConfig.SimulatorIntegrationExternalId))
            {
                return (model, null);
            }
            if (simEv.Run.Status == SimulationRunStatus.running)
            {
                throw new ConnectorException("Calculation failed due to connector error");
            }
            return (model, calcConfig);
        }

        /// <summary>
        /// Before running the simulation, the CDF Event that triggered it is changed from
        /// <see cref="SimulationEventStatusValues.Ready"/> to <see cref="SimulationEventStatusValues.Running"/>.
        /// At this point, any simulator specific metadata that needs to be added to the event, should be initialized here.
        /// </summary>
        /// <param name="modelState">Model state</param>
        /// <param name="configObj">Simulation configuration object</param>
        /// <param name="metadata">Metadata to be added to the CDF event</param>
        protected abstract void InitSimulationEventMetadata(
            T modelState,
            V configObj,
            Dictionary<string, string> metadata);

        async void PublishSimulationRunStatus(string runStatus, CancellationToken token)
        {
            try
            {
                if (!simulatorIntegrationId.HasValue && _simulators.Count > 0)
                {
                    SimulatorConfig simulator = _simulators[0]; // Retrieve the first item
                    var integrationRes = await _cdfSimulators.ListSimulatorIntegrationsAsync(
                        new SimulatorIntegrationQuery() {
                            Filter = new SimulatorIntegrationFilter() {
                                simulatorExternalIds = new List<string>() { simulator.Name },
                            }
                        },
                        token).ConfigureAwait(false);
                    var integration = integrationRes.Items.FirstOrDefault(i => i.ExternalId == _connectorConfig.GetConnectorName());
                    if (integration == null)
                    {
                        throw new ConnectorException($"Simulator integration for {simulator.Name} not found");
                    }
                    simulatorIntegrationId = integration.Id;
                }
                var now = DateTime.UtcNow.ToUnixTimeMilliseconds();
                var simulatorIntegrationUpdate = new SimulatorIntegrationUpdate
                    {
                        ConnectorStatus = new Update<string>(runStatus),
                        ConnectorStatusUpdatedTime = new Update<long>(now)
                    };
                await _cdfSimulators.UpdateSimulatorIntegrationAsync(
                    new [] {
                        new SimulatorIntegrationUpdateItem(simulatorIntegrationId.Value) {
                            Update = simulatorIntegrationUpdate
                        }
                    },
                    token
                ).ConfigureAwait(false);
            }

            catch (Exception e)
            {
                // throw new ConnectorException(e.Message);
            }
        }

        /// <summary>
        /// Initialize the simulation event execution
        /// </summary>
        /// <param name="simEv">Simulation event</param>
        /// <param name="startTime">Simulation start time</param>
        /// <param name="modelState">Model state object</param>
        /// <param name="routineRevision">Routine revision object</param>
        /// <param name="metadata">Metadata to add to the event</param>
        /// <param name="token">Cancellation token</param>
        protected virtual async Task InitSimulationRun(
            SimulationRunEvent simEv,
            DateTime startTime,
            T modelState,
            V routineRevision,
            Dictionary<string, string> metadata,
            CancellationToken token)
        {

            if (modelState == null)
            {
                throw new ArgumentNullException(nameof(modelState));
            }
            if (simEv == null)
            {
                throw new ArgumentNullException(nameof(simEv));
            }
            if (routineRevision == null)
            {
                throw new ArgumentNullException(nameof(routineRevision));
            }

            simEv.Run = await UpdateSimulationRunStatus(
                simEv.Run.Id,
                SimulationRunStatus.running,
                null,
                token).ConfigureAwait(false);

            SamplingRange samplingRange = null;
            var validationEnd = startTime;
            var configObj = routineRevision.Configuration;
            try
            {
                if (configObj.DataSampling == null)
                {
                    throw new SimulationException($"Data sampling configuration for {routineRevision.ExternalId} missing");
                }
                // Determine the validation end time
                if (simEv.Run.RunTime.HasValue)
                {
                    // If the event contains a validation end overwrite, use that instead of
                    // the current time
                    validationEnd = CogniteTime.FromUnixTimeMilliseconds(simEv.Run.RunTime.Value);
                }

                // Find the sampling configuration results
                samplingRange = await SimulationUtils.RunSteadyStateAndLogicalCheck(
                    _cdfDataPoints,
                    configObj,
                    validationEnd,
                    token).ConfigureAwait(false);

                _logger.LogInformation("Running routine revision {ExternalId} for model {ModelExternalId}. Calculation time: {Time}",
                    routineRevision.ExternalId,
                    routineRevision.ModelExternalId,
                    CogniteTime.FromUnixTimeMilliseconds(samplingRange.Midpoint));
            }
            catch (SimulationException ex)
            {
                _logger.LogError("Logical check or steady state detection failed: {Message}", ex.Message);
                throw;
            }
            finally
            {
                // Create the run configuration dictionary
                BuildRunConfiguration(
                    samplingRange,
                    simEv,
                    configObj,
                    validationEnd);
            }
            await RunSimulation(
                simEv,
                startTime,
                modelState,
                routineRevision,
                samplingRange,
                token).ConfigureAwait(false);

                simEv.Run = await UpdateSimulationRunStatus(
                    simEv.Run.Id,
                    SimulationRunStatus.success,
                    "Calculation ran to completion",
                    token,
                    simEv.RunConfiguration
                ).ConfigureAwait(false);

            await EndSimulationRun(simEv, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Called after the simulation run has finished. This method can be used to
        /// perform any cleanup or other actions that need to be done after the run.
        /// </summary>
        protected abstract Task EndSimulationRun(SimulationRunEvent simEv,
            CancellationToken token) ;

        /// <summary>
        /// Run a simulation and saves the results back to CDF. Different simulators
        /// will implement different patterns of interaction when running simulations
        /// </summary>
        /// <param name="e">Simulation event</param>
        /// <param name="startTime">Simulation start time</param>
        /// <param name="modelState">Model state object</param>
        /// <param name="configObj">Configuration object</param>
        /// <param name="samplingRange">Selected simulation sampling range</param>
        /// <param name="token">Cancellation token</param>
        protected abstract Task RunSimulation(
            SimulationRunEvent e,
            DateTime startTime,
            T modelState,
            V configObj,
            SamplingRange samplingRange,
            CancellationToken token);

        /// <summary>
        /// Builds the run configuration dictionary to be stored in CDF upon simulations is finished
        /// At this point we only store the simulation time on the simulation run object
        /// </summary>
        /// <param name="samplingRange">Selected simulation sampling range</param>
        /// <param name="simEv">Simulation event</param>
        /// <param name="configObj">Configuration object</param>
        /// <param name="validationEnd">End of the validation period</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are missing</exception>
        protected virtual void BuildRunConfiguration(
            SamplingRange samplingRange,
            SimulationRunEvent simEv,
            SimulatorRoutineRevisionConfiguration configObj,
            DateTime validationEnd)
        {
            if (simEv == null)
            {
                throw new ArgumentNullException(nameof(simEv));
            }

            if (configObj == null)
            {
                throw new ArgumentNullException(nameof(configObj));
            }

            simEv.RunConfiguration.Add("validationStart", validationEnd.AddMinutes(-configObj.DataSampling.ValidationWindow).ToUnixTimeMilliseconds());
            simEv.RunConfiguration.Add("validationEnd", validationEnd.ToUnixTimeMilliseconds());

            if (samplingRange != null)
            {
                simEv.RunConfiguration.Add("simulationTime", samplingRange.Midpoint);
                simEv.RunConfiguration.Add("samplingStart", samplingRange.Start.Value);
                simEv.RunConfiguration.Add("samplingEnd", samplingRange.End.Value);
            }
        }
    }

    /// <summary>
    /// Wrapper class for <see cref="SimulationRun"/> entity.
    /// Contains the simulation run configuration as a dictionary of key-value pairs
    /// </summary>
    public class SimulationRunEvent
    {
        /// <summary>
        /// CDF SimulationRun resource representing a simulation run
        /// </summary>
        public SimulationRun Run { get; set; }

        /// <summary>
        /// Run configuration as a dictionary of key-value pairs
        /// </summary>
        public Dictionary<string, long> RunConfiguration { get; } = new Dictionary<string, long>();

        /// <summary>
        /// Creates a new simulation run event based on simulation run CDF resource
        /// </summary>
        public SimulationRunEvent(SimulationRun r)
        {
            Run = r;
        }
    }

    /// <summary>
    /// Represents errors related to running simulations
    /// </summary>
    public class SimulationException : Exception
    {
        /// <summary>
        /// Creates a new simulation exception
        /// </summary>
        public SimulationException()
        {
        }

        /// <summary>
        /// Creates a new simulation exception with the given message
        /// </summary>
        /// <param name="message">Error message</param>
        public SimulationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a new simulation exception with the given message and inner exception
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="innerException">Inner exception</param>
        public SimulationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

}