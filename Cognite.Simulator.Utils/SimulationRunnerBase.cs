using Cognite.Extensions;
using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using CogniteSdk.Alpha;
using CogniteSdk.Resources;
using CogniteSdk.Resources.Alpha;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
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
        where U : ConfigurationStateBase
        where V : SimulationConfigurationWithDataSampling
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
        protected IConfigurationProvider<U, V> ConfigurationLibrary { get; }

        private long? simulatorIntegrationId;


        /// <summary>
        /// Create a new instance of the runner with the provided parameters
        /// </summary>
        /// <param name="connectorConfig">Connector configuration</param>
        /// <param name="simulators">List of simulators</param>
        /// <param name="cdf">CDF client</param>
        /// <param name="modelLibrary">Model library</param>
        /// <param name="configLibrary">Configuration library</param>
        /// <param name="logger">Logger</param>
        public SimulationRunnerBase(
            ConnectorConfig connectorConfig,
            IList<SimulatorConfig> simulators,
            CogniteDestination cdf,
            IModelProvider<T> modelLibrary,
            IConfigurationProvider<U, V> configLibrary,
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
            ConfigurationLibrary = configLibrary;
        }

        private async Task<SimulationRun> UpdateSimulationRunStatus(
            long runId,
            SimulationRunStatus status,
            string statusMessage,
            CancellationToken token)
        {
            var res = await _cdfSimulators.SimulationRunCallbackAsync(
                new SimulationRunCallbackItem()
                {
                    Id = runId,
                    Status = status,
                    StatusMessage = statusMessage
                }, token).ConfigureAwait(false);

            return res.Items.First();
        }

        private async Task<IEnumerable<SimulationRun>> FindSimulationRunsWithStatus(
            Dictionary<string, long> simulators,
            SimulationRunStatus status,
            CancellationToken token)
        {
            var result = new List<SimulationRun>();
            if (simulators == null || !simulators.Any())
            {
                return result;
            }

            foreach (var source in simulators)
            {
                var query = new SimulationRunQuery()
                {
                    Filter = new SimulationRunFilter()
                    {
                        SimulatorName = source.Key,
                        Status = status
                    }
                };

                var runsResult = await _cdfSimulators
                    .ListSimulationRunsAsync(query, token)
                    .ConfigureAwait(false);

                result.AddRange(runsResult.Items);
            }
            return result;
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
                allEvents = allEvents
                    .Where(e => e.HasSimulationRun)
                    .ToList();

                // sort by event time
                allEvents.Sort((e1, e2) =>
                {
                    return e1.Run.CreatedTime > e2.Run.CreatedTime ? -1 : 1;
                });
                foreach (SimulationRunEvent e in allEvents)
                {
                    var eventId = e.HasSimulationRun ? e.Run.Id.ToString() : e.Event.ExternalId;
                    var startTime = DateTime.UtcNow;
                    T modelState = null;
                    U calcState = null;
                    V calcObj = null;
                    bool skipped = false;
                    try
                    {
                        (modelState, calcState, calcObj) = ValidateEventMetadata(e);

                        if (calcState == null || calcObj == null || calcObj.Connector != _connectorConfig.GetConnectorName())
                        {
                            _logger.LogError("Skip simulation run that belongs to another connector: {Id} {Connector}",
                               eventId,
                               calcObj?.Connector);
                            skipped = true;
                            continue;
                        }

                        var metadata = new Dictionary<string, string>();
                        InitSimulationEventMetadata(
                            modelState,
                            calcState,
                            calcObj,
                            metadata);
                        PublishSimulationRunStatus("RUNNING_CALCULATION", token);
                        await InitSimulationRun(
                            e,
                            startTime,
                            modelState,
                            calcState,
                            calcObj,
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
                        _logger.LogError("Calculation run failed with error: {Message}", ex.Message);
                        if (e.HasSimulationRun)
                        {
                            e.Run = await UpdateSimulationRunStatus(
                                e.Run.Id,
                                SimulationRunStatus.failure,
                                ex.Message == null || ex.Message.Length < 255 ? ex.Message : ex.Message.Substring(0, 254),
                                token).ConfigureAwait(false);
                        }
                        else
                        {
                            e.Event = await _cdfEvents.UpdateSimulationEventToFailure(
                                e.Event.ExternalId,
                                startTime,
                                null,
                                ex.Message.LimitUtf8ByteCount(Sanitation.EventMetadataMaxPerValue),
                                token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        // the following check was added because the code below was running even for skipped events
                        if (!skipped)
                        {
                            await StoreRunConfiguration(
                                calcState,
                                calcObj,
                                startTime,
                                e,
                                token).ConfigureAwait(false);
                            _logger.LogDebug("Calculation run finished for event {Id}", eventId);
                            PublishSimulationRunStatus("IDLE", token);
                        }

                    }

                }

                await Task.Delay(interval, token).ConfigureAwait(false);
            }
        }

        private (T, U, V) ValidateEventMetadata(SimulationRunEvent simEv)
        {
            string modelName;
            string calcType;
            string simulator;
            string calcTypeUserDefined = null;
            string eventId = "";
            if (simEv.HasSimulationRun)
            {
                eventId = simEv.Run.Id.ToString();
                simulator = simEv.Run.SimulatorName;
                modelName = simEv.Run.ModelName;
                calcType = "UserDefined";
                calcTypeUserDefined = simEv.Run.RoutineName;
            }
            else
            {
                var e = simEv.Event;
                eventId = e.ExternalId;
                if (e.Metadata[SimulationEventMetadata.StatusKey] == SimulationEventStatusValues.Running)
                {
                    throw new ConnectorException("Calculation failed due to connector error");
                }
                var eventAge = DateTime.UtcNow - CogniteTime.FromUnixTimeMilliseconds(e.LastUpdatedTime);
                if (eventAge >= TimeSpan.FromSeconds(_connectorConfig.SimulationEventTolerance))
                {
                    throw new TimeoutException("Timeout: The connector could not run the calculation on time");
                }

                // Check for the needed files before start, fail the run if anything missing
                if (!e.Metadata.TryGetValue(ModelMetadata.NameKey, out modelName))
                {
                    _logger.LogError("Event {Id} does not indicate the model name to use", e.ExternalId);
                    throw new SimulationException("Model name missing");
                }
                if (!e.Metadata.TryGetValue(CalculationMetadata.TypeKey, out calcType))
                {
                    _logger.LogError("Event {Id} does not indicate the calculation type to use", e.ExternalId);
                    throw new SimulationException("Calculation type missing");
                }
                if (calcType == "UserDefined" && !e.Metadata.TryGetValue(CalculationMetadata.UserDefinedTypeKey, out calcTypeUserDefined))
                {
                    _logger.LogError("Event {Id} is user-defined, but is missing the calculation type property", e.ExternalId);
                    throw new SimulationException("Type of user-defined calculation missing");
                }
                if (!e.Metadata.TryGetValue(BaseMetadata.SimulatorKey, out simulator))
                {
                    _logger.LogError("Event {Id} does not indicate the simulator to use", e.ExternalId);
                    throw new SimulationException("Simulator missing");
                }
            }
            var model = ModelLibrary.GetLatestModelVersion(simulator, modelName);
            if (model == null)
            {
                _logger.LogError("Could not find a local model file to run Simulation Event {Id}", eventId);
                throw new SimulationException($"Could not find a model file for {modelName}");
            }
            U calcState;
            V calcConfig;
            if (simEv.HasSimulationRun)
            {
                calcState = ConfigurationLibrary.GetSimulationConfigurationState(simulator, modelName, calcTypeUserDefined);
                calcConfig = ConfigurationLibrary.GetSimulationConfiguration(simulator, modelName, calcTypeUserDefined);
            }
            else
            {
                calcState = ConfigurationLibrary.GetSimulationConfigurationState(simulator, modelName, calcType, calcTypeUserDefined);
                calcConfig = ConfigurationLibrary.GetSimulationConfiguration(simulator, modelName, calcType, calcTypeUserDefined);
            }
            if (calcConfig == null || calcState == null)
            {
                _logger.LogError("Could not find a local configuration to run Simulation Event {Id}", eventId);
                throw new SimulationException($"Could not find a simulation configuration for model: {modelName} calcType: {calcType} calcTypeUserDefined: {calcTypeUserDefined}");
            }

            if (calcConfig.Connector != _connectorConfig.GetConnectorName())
            {
                return (model, null, null);
            }
            if (simEv.HasSimulationRun)
            {
                if (simEv.Run.Status == SimulationRunStatus.running)
                {
                    throw new ConnectorException("Calculation failed due to connector error");
                }
            }
            return (model, calcState, calcConfig);
        }

        /// <summary>
        /// Before running the simulation, the CDF Event that triggered it is changed from
        /// <see cref="SimulationEventStatusValues.Ready"/> to <see cref="SimulationEventStatusValues.Running"/>.
        /// At this point, any simulator specific metadata that needs to be added to the event, should be initialized here.
        /// </summary>
        /// <param name="modelState">Model state</param>
        /// <param name="configState">Simulation configuration state</param>
        /// <param name="configObj">Simulation configuration object</param>
        /// <param name="metadata">Metadata to be added to the CDF event</param>
        protected abstract void InitSimulationEventMetadata(
            T modelState,
            U configState,
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
        /// <param name="configState">Configuration state object</param>
        /// <param name="configObj">Configuration object</param>
        /// <param name="metadata">Metadata to add to the event</param>
        /// <param name="token">Cancellation token</param>
        protected virtual async Task InitSimulationRun(
            SimulationRunEvent simEv,
            DateTime startTime,
            T modelState,
            U configState,
            V configObj,
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
            if (configObj == null)
            {
                throw new ArgumentNullException(nameof(configObj));
            }

            if (simEv.HasSimulationRun)
            {
                simEv.Run = await UpdateSimulationRunStatus(
                    simEv.Run.Id,
                    SimulationRunStatus.running,
                    null,
                    token).ConfigureAwait(false);
            }
            else
            {
                simEv.Event = await _cdfEvents.UpdateSimulationEventToRunning(
                    simEv.Event.ExternalId,
                    startTime,
                    metadata,
                    modelState.Version,
                    token).ConfigureAwait(false);
            }

            SamplingRange samplingRange = null;
            var validationEnd = startTime;
            try
            {
                if (configObj.DataSampling == null)
                {
                    throw new SimulationException($"Data sampling configuration for {configObj.CalculationName} missing");
                }
                // Determine the validation end time
                if (!simEv.HasSimulationRun
                    && simEv.Event.Metadata.TryGetValue(SimulationEventMetadata.ValidationEndOverwriteKey, out string validationEndOverwrite)
                    && long.TryParse(validationEndOverwrite, out long overwriteValue))
                {
                    // If the event contains a validation end overwrite, use that instead of
                    // the current time
                    validationEnd = CogniteTime.FromUnixTimeMilliseconds(overwriteValue);
                }
                else if (simEv.Run.ValidationEndTime.HasValue)
                {
                    // If the event contains a validation end overwrite, use that instead of
                    // the current time
                    validationEnd = CogniteTime.FromUnixTimeMilliseconds(simEv.Run.ValidationEndTime.Value);
                }
                else
                {
                    // If the validation end time should be in the past, subtract the 
                    // configured offset
                    var offset = SimulationUtils.ConfigurationTimeStringToTimeSpan(
                        configObj.DataSampling.ValidationEndOffset);
                    validationEnd = startTime - offset;
                }

                // Find the sampling configuration results
                samplingRange = await SimulationUtils.RunSteadyStateAndLogicalCheck(
                    _cdfDataPoints,
                    configObj,
                    validationEnd,
                    token).ConfigureAwait(false);

                _logger.LogInformation("Running calculation {Type} for model {ModelName}. Calculation time: {Time}",
                    configObj.CalculationType,
                    configObj.ModelName,
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
                    modelState,
                    configObj,
                    simEv,
                    validationEnd);
            }
            // Run the simulation
            await RunSimulation(
                simEv,
                startTime,
                modelState,
                configState,
                configObj,
                samplingRange,
                token).ConfigureAwait(false);

             // Update event with success status
            if (simEv.HasSimulationRun)
            {
                simEv.Run = await UpdateSimulationRunStatus(
                    simEv.Run.Id,
                    SimulationRunStatus.success,
                    "Calculation ran to completion",
                    token).ConfigureAwait(false);
            }
            else
            {
                simEv.Event = await _cdfEvents.UpdateSimulationEventToSuccess(
                    simEv.Event.ExternalId,
                    startTime,
                    null,
                    "Calculation ran to completion",
                    token).ConfigureAwait(false);
            }

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
        /// <param name="configState">Configuration state object</param>
        /// <param name="configObj">Configuration object</param>
        /// <param name="samplingRange">Selected simulation sampling range</param>
        /// <param name="token">Cancellation token</param>
        protected abstract Task RunSimulation(
            SimulationRunEvent e,
            DateTime startTime,
            T modelState,
            U configState,
            V configObj,
            SamplingRange samplingRange,
            CancellationToken token);

        /// <summary>
        /// Builds the run configuration dictionary to be stored in CDF as a sequence
        /// </summary>
        /// <param name="samplingRange">Selected simulation sampling range</param>
        /// <param name="modelState">Model state object</param>
        /// <param name="configObj">Configuration object</param>
        /// <param name="simEv">Simulation event</param>
        /// <param name="validationEnd">End of the validation period</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are missing</exception>
        /// <exception cref="ConnectorException">Thrown when it is not possible to save the sequence</exception>
        protected virtual void BuildRunConfiguration(
            SamplingRange samplingRange,
            T modelState,
            V configObj,
            SimulationRunEvent simEv,
            DateTime validationEnd)
        {
            if (simEv == null)
            {
                throw new ArgumentNullException(nameof(simEv));
            }
            if (modelState == null)
            {
                throw new ArgumentNullException(nameof(modelState));
            }
            if (configObj == null)
            {
                throw new ArgumentNullException(nameof(configObj));
            }

            _logger.LogDebug("Storing run configuration in CDF");
            if (simEv.HasSimulationRun)
            {
                simEv.RunConfiguration.Add("runId", simEv.Run.Id.ToString());
            }
            else
            {
                simEv.RunConfiguration.Add("runEventId", simEv.Event.ExternalId);
            }

            // Create a dictionary with the run details
            if (samplingRange != null)
            {
                simEv.RunConfiguration.Add("calcTime", samplingRange.Midpoint.ToString());
            }
            simEv.RunConfiguration.Add("modelVersion", modelState.Version.ToString());

            // Validation range details
            simEv.RunConfiguration.Add("validationWindow", configObj.DataSampling.ValidationWindow.ToString());
            simEv.RunConfiguration.Add("validationStart", validationEnd.AddMinutes(-configObj.DataSampling.ValidationWindow).ToUnixTimeMilliseconds().ToString());
            simEv.RunConfiguration.Add("validationEnd", validationEnd.ToUnixTimeMilliseconds().ToString());
            simEv.RunConfiguration.Add("validationEndOffset", configObj.DataSampling.ValidationEndOffset);

            // Sampling range details
            simEv.RunConfiguration.Add("samplingWindow", configObj.DataSampling.SamplingWindow.ToString());
            if (samplingRange != null)
            {
                simEv.RunConfiguration.Add("samplingStart", samplingRange.Start.Value.ToString());
                simEv.RunConfiguration.Add("samplingEnd", samplingRange.End.Value.ToString());
            }
            simEv.RunConfiguration.Add("samplingGranularity", configObj.DataSampling.Granularity.ToString());

            // Logical check details
            bool logicalCheckEnabled = configObj.LogicalCheck != null && configObj.LogicalCheck.Enabled;
            simEv.RunConfiguration.Add("logicalCheckEnabled", logicalCheckEnabled.ToString());
            if (logicalCheckEnabled)
            {
                simEv.RunConfiguration.Add("logicalCheckTimeSeries", configObj.LogicalCheck.ExternalId);
                simEv.RunConfiguration.Add("logicalCheckSamplingMethod", configObj.LogicalCheck.AggregateType);
                simEv.RunConfiguration.Add("logicalCheckOperation", configObj.LogicalCheck.Check);
                simEv.RunConfiguration.Add("logicalCheckThresholdValue", configObj.LogicalCheck.Value.ToString());
            }

            // Steady state details
            bool ssdEnabled = configObj.SteadyStateDetection != null && configObj.SteadyStateDetection.Enabled;
            simEv.RunConfiguration.Add("ssdEnabled", ssdEnabled.ToString());
            if (ssdEnabled)
            {
                simEv.RunConfiguration.Add("ssdTimeSeries", configObj.SteadyStateDetection.ExternalId);
                simEv.RunConfiguration.Add("ssdSamplingMethod", configObj.SteadyStateDetection.AggregateType);
                simEv.RunConfiguration.Add("ssdMinSectionSize", configObj.SteadyStateDetection.MinSectionSize.ToString());
                simEv.RunConfiguration.Add("ssdVarThreshold", configObj.SteadyStateDetection.VarThreshold.ToString());
                simEv.RunConfiguration.Add("ssdSlopeThreshold", configObj.SteadyStateDetection.SlopeThreshold.ToString());
            }

            // Input time series details
            foreach (var input in configObj.InputTimeSeries)
            {
                simEv.RunConfiguration.Add($"inputTimeSeries{input.Type}", input.SensorExternalId);
                simEv.RunConfiguration.Add($"inputSamplingMethod{input.Type}", input.AggregateType);
            }
        }

        private async Task StoreRunConfiguration(
             U configState,
             V configObj,
             DateTime eventStartTime,
             SimulationRunEvent simEv,
             CancellationToken token)
        {
            if (configState == null || configObj == null)
            {
                return;
            }

            Event simEvent;

            if (simEv.HasSimulationRun)
            {
                if (!simEv.Run.EventId.HasValue)
                {
                    _logger.LogDebug("Simulation run has no Event associated with it {Id}", simEv.Run.Id);
                    return;
                }
                simEvent = await _cdfEvents.GetAsync(simEv.Run.EventId.Value, token).ConfigureAwait(false);
            }
            else
            {
                simEvent = simEv.Event;
            }

            // Determine what is the sequence id and the row number to start inserting data
            var sequenceId = configState.RunDataSequence;
            long rowStart = 0;
            if (sequenceId != null)
            {
                rowStart = configState.RunSequenceLastRow + 1;

                // Create a new sequence if reached the configured row limit
                if (simEv.RunConfiguration.Count + rowStart > _connectorConfig.MaximumNumberOfSequenceRows)
                {
                    sequenceId = null;
                    rowStart = 0;
                }
            }

            // Make sure the sequence exists in CDF
            try
            {
                var seq = await _cdfSequences.StoreRunConfiguration(
                    sequenceId,
                    rowStart,
                    configState.DataSetId,
                    configObj.Calculation,
                    simEv.RunConfiguration,
                    token).ConfigureAwait(false);

                if (string.IsNullOrEmpty(sequenceId))
                {
                    sequenceId = seq.ExternalId;
                }

                // Update the local state with the sequence ID and the last row number
                configState.RunDataSequence = sequenceId;
                configState.RunSequenceLastRow = simEv.RunConfiguration.Count + rowStart - 1;
                await ConfigurationLibrary.StoreLibraryState(token).ConfigureAwait(false);

                // Update the event with calculation time and run details sequence
                Dictionary<string, string> eventMetaData = new Dictionary<string, string>()
                {
                    { "runConfigurationSequence", seq.ExternalId },
                    { "runConfigurationRowStart", rowStart.ToString() },
                    { "runConfigurationRowEnd", configState.RunSequenceLastRow.ToString() }
                };
                if (simEv.RunConfiguration.TryGetValue("calcTime", out var calcTime))
                {
                    eventMetaData.Add("calcTime", calcTime);
                }
                await _cdfEvents.UpdateSimulationEvent(
                    simEvent.ExternalId,
                    eventStartTime,
                    eventMetaData,
                    token).ConfigureAwait(false);
            }
            catch (SimulationRunConfigurationException e)
            {
                throw new ConnectorException(e.Message, e.CogniteErrors);
            }
        }
    }

    /// <summary>
    /// Wrapper class for simulation run entities. There are two entities in CDF as of now
    /// that can represent a simulation run: <see cref="CogniteSdk.Event"/> and <see cref="SimulationRun"/>.
    /// Eventually support for representing run as CDF Events will be discontinued.
    /// </summary>
    public class SimulationRunEvent
    {
        /// <summary>
        /// CDF Event representing a simulation run
        /// </summary>
        public Event Event { get; set; }
        /// <summary>
        /// CDF SimulationRun resource representing a simulation run
        /// </summary>
        public SimulationRun Run { get; set; }

        /// <summary>
        /// Run configuration as a dictionary of key-value pairs
        /// </summary>
        public Dictionary<string, string> RunConfiguration { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Whether the simulation run is represented as a SimulationRun entity or a CDF Event
        /// </summary>
        public bool HasSimulationRun => Run != null;

        /// <summary>
        /// Creates a new simulation run event based on CDF event
        /// </summary>
        public SimulationRunEvent(Event e)
        {
            Event = e;
        }

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
