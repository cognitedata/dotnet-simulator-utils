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
        /// Keeps a list of events already processed by the connector. Since updates to CDF
        /// Events are eventually consistent, there is a risk of fetching and processing events
        /// already processed. Caching the processed events locally prevents that
        /// </summary>
        protected Dictionary<string, long> EventsAlreadyProcessed { get; }

        /// <summary>
        /// Library containing the simulator model files
        /// </summary>
        protected IModelProvider<T> ModelLibrary { get; }

        /// <summary>
        /// Library containing the simulation configuration files
        /// </summary>
        protected IConfigurationProvider<U, V> ConfigurationLibrary { get; }

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
            EventsAlreadyProcessed = new Dictionary<string, long>();
            ModelLibrary = modelLibrary;
            ConfigurationLibrary = configLibrary;
        }

        // TODO: this used to save model version and 
        private async Task<SimulationRun> UpdateSimulationRunStatus(long SimulatuonRunId, SimulationRunStatus status, string statusMessage, CancellationToken token)
        {
            var res = await _cdfSimulators.SimulationRunCallback(new SimulationRunCallbackItem(){
                Id = SimulatuonRunId,
                Status = status,
                StatusMessage = statusMessage
            }).ConfigureAwait(false);

            return res.Items.First();
        }
        
        private async Task<IEnumerable<SimulationRun>> FindSimulationRunsWithStatus(Dictionary<string, long> simulators, SimulationRunStatus status, CancellationToken token)
        {
            var result = new List<SimulationRun>();
            if (simulators == null || !simulators.Any())
            {
                return result;
            }
            
            foreach (var source in simulators)
            {
                var query = new SimulationRunQuery() {
                    Filter = new SimulationRunFilter() {
                        SimulatorName = source.Key,
                        Status = status
                    }
                };

                var runsResult = await _cdfSimulators
                    .ListSimulationRuns(query, token)
                    .ConfigureAwait(false);

                result.AddRange(runsResult.Items);
            }
            return result;
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
                var simulationEvents = await _cdfEvents.FindSimulationEventsReadyToRun(
                    simulators,
                    _connectorConfig.GetConnectorName(),
                    token).ConfigureAwait(false);

                var simulationRuns = await this.FindSimulationRunsWithStatus(simulators, SimulationRunStatus.ready, token).ConfigureAwait(false);
                if (simulationRuns.Any())
                {
                    _logger.LogInformation(
                        "{Number} ready simulation runs found in CDF",
                        simulationRuns.Count());
                }

                // Find events that are running. Should not have any, as the connector runs events in sequence.
                // Any running events indicates that the connector went down during the run, and the event should fail
                var runningSimulationRuns = await this.FindSimulationRunsWithStatus(simulators, SimulationRunStatus.running, token).ConfigureAwait(false);

                if (runningSimulationRuns.Any())
                {
                    _logger.LogWarning(
                        "{Number} simulation runs in progress (but should have finished) found in CDF",
                        runningSimulationRuns.Count());
                }

                var allSimulationRuns = new List<SimulationRun>(simulationRuns);
                allSimulationRuns.AddRange(runningSimulationRuns);

                foreach (SimulationRun run in allSimulationRuns) {
                    var startTime = DateTime.UtcNow;
                    try {
                        // TODO: add the lastUpdated time
                        // var simulationRunAge = startTime - CogniteTime.FromUnixTimeMilliseconds(run.Status);
                        // if (simulationRunAge >= TimeSpan.FromSeconds(_connectorConfig.SimulationEventTolerance))
                        // {
                        //     throw new TimeoutException("Timeout: The connector could not run the calculation on time");
                        // }
                        var (modelState, calcState, calcObj) = GetModelAndSimulationConfig(run);
                        
                        if (calcObj.Connector != _connectorConfig.GetConnectorName()) {
                            _logger.LogError("Skip simulation run that belongs to another connector: {Id} {Connector}", run.Id, calcObj.Connector);
                            continue;
                        }

                        if (run.Status == SimulationRunStatus.running)
                        {
                            throw new ConnectorException("Calculation failed due to connector error");
                        }

                        await InitSimulationRun(
                            run,
                            startTime,
                            modelState,
                            calcState,
                            calcObj,
                            token)
                            .ConfigureAwait(false);
                    } catch (Exception ex) {
                        if (ex is ConnectorException ce && ce.Errors != null)
                        {
                            foreach (var error in ce.Errors)
                            {
                                _logger.LogError(error.Message);
                            }
                        }
                        _logger.LogError("Calculation run failed with error: {Message}", ex.Message);
                        await _cdfSimulators.SimulationRunCallback(new SimulationRunCallbackItem(){
                            Id = run.Id,
                            Status = SimulationRunStatus.failure,
                            StatusMessage = ex.Message.Substring(0, 100)
                        }).ConfigureAwait(false);
                    }
                }   

                await Task.Delay(interval, token).ConfigureAwait(false);
            }
        }
        private (T, U, V) GetModelAndSimulationConfig(SimulationRun run)
        {
            var simulatorName = run.SimulatorName;
            var modelName = run.ModelName;
            var calcType = "User defined";
            // Check for the needed files before start, fail the run if anything missing
            var model = ModelLibrary.GetLatestModelVersion(simulatorName, run.ModelName);
            if (model == null)
            {
                _logger.LogError("Could not find a local model file for simulation run {Id}", run.Id);
                throw new SimulationException($"Could not find a model file for {run.ModelName}");
            }
            var calcConfig = ConfigurationLibrary.GetSimulationConfiguration(simulatorName, modelName, calcType, run.RoutineName);
            var calcState = ConfigurationLibrary.GetSimulationConfigurationState(simulatorName, modelName, calcType, run.RoutineName);
            if (calcConfig == null || calcState == null)
            {
                _logger.LogError("Could not find a local configuration for simulation run {Id}", run.Id);
                throw new SimulationException($"Could not find a simulation configuration for {modelName}");
            }
            return (model, calcState, calcConfig);
        }


        /// <summary>
        /// Initialize the simulation event execution
        /// </summary>
        /// <param name="run">Simulation run</param>
        /// <param name="startTime">Simulation start time</param>
        /// <param name="modelState">Model state object</param>
        /// <param name="configState">Configuration state object</param>
        /// <param name="configObj">Configuration object</param>
        /// <param name="token">Cancellation token</param>
        protected virtual async Task InitSimulationRun(
            SimulationRun run,
            DateTime startTime,
            T modelState,
            U configState,
            V configObj,
            CancellationToken token)
        {
            if (modelState == null)
            {
                throw new ArgumentNullException(nameof(modelState));
            }
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }
            if (configObj == null)
            {
                throw new ArgumentNullException(nameof(configObj));
            }

            await UpdateSimulationRunStatus(run.Id, SimulationRunStatus.running, null, token).ConfigureAwait(false);

            SamplingRange samplingRange = null;
            var validationEnd = startTime;
            try
            {
                if (configObj.DataSampling == null)
                {
                    throw new SimulationException($"Data sampling configuration for {configObj.CalculationName} missing");
                }
                // TODO: we should support the end time override here
                // Determine the validation end time
                // If the validation end time should be in the past, subtract the 
                // configured offset
                var offset = SimulationUtils.ConfigurationTimeStringToTimeSpan(
                    configObj.DataSampling.ValidationEndOffset);
                validationEnd = startTime - offset;

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
                // Save run configuration
                await StoreRunConfigurationInCdf( // can we do this after RunSimulation?
                    samplingRange,
                    modelState,
                    configState,
                    configObj,
                    run,
                    startTime,
                    validationEnd,
                    modelState.DataSetId,
                    token).ConfigureAwait(false);
            }

            // Run the simulation
            await RunSimulation(
                run,
                startTime,
                modelState,
                configState,
                configObj,
                samplingRange,
                token).ConfigureAwait(false);

            await UpdateSimulationRunStatus(run.Id, SimulationRunStatus.success, "Calculation ran to completion", token).ConfigureAwait(false);
        }

        /// <summary>
        /// Run a simulation and saves the results back to CDF. Different simulators
        /// will implement different patterns of interaction when running simulations
        /// </summary>
        /// <param name="run">Simulation run</param>
        /// <param name="startTime">Simulation start time</param>
        /// <param name="modelState"></param>
        /// <param name="configState">Configuration state object</param>
        /// <param name="configObj">Configuration object</param>
        /// <param name="samplingRange">Selected simulation sampling range</param>
        /// <param name="token">Cancellation token</param>
        protected abstract Task RunSimulation(
            SimulationRun run,
            DateTime startTime,
            T modelState,
            U configState,
            V configObj,
            SamplingRange samplingRange,
            CancellationToken token);

        /// <summary>
        /// Store the run configuration information as a CDF sequence
        /// </summary>
        /// <param name="samplingRange">Selected simulation sampling range</param>
        /// <param name="modelState">Model state object</param>
        /// <param name="configState">Configuration state object</param>
        /// <param name="configObj">Configuration object</param>
        /// <param name="run">Simulation Run</param>
        /// <param name="eventStartTime">Event start time</param>
        /// <param name="validationEnd">End of the validation period</param>
        /// <param name="dataSetId">Data set id to save the sequence to</param>
        /// <param name="token">Cancellation token</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are missing</exception>
        /// <exception cref="ConnectorException">Thrown when it is not possible to save the sequence</exception>
        protected virtual async Task StoreRunConfigurationInCdf(
            SamplingRange samplingRange,
            T modelState,
            U configState,
            V configObj,
            SimulationRun run,
            DateTime eventStartTime,
            DateTime validationEnd,
            long? dataSetId,
            CancellationToken token)
        {
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }
            if (modelState == null)
            {
                throw new ArgumentNullException(nameof(modelState));
            }
            if (configObj == null)
            {
                throw new ArgumentNullException(nameof(configObj));
            }
            if (configState == null)
            {
                throw new ArgumentNullException(nameof(configState));
            }

            _logger.LogDebug("Storing run configuration in CDF");

            // Create a dictionary with the run details
            var runDetails = new Dictionary<string, string>
            {
                { "runEventId", null },
                { "simulationRunId", run.Id.ToString() }
            };
            if (samplingRange != null)
            {
                runDetails.Add("calcTime", samplingRange.Midpoint.ToString());
            }
            runDetails.Add("modelVersion", modelState.Version.ToString());

            // Validation range details
            runDetails.Add("validationWindow", configObj.DataSampling.ValidationWindow.ToString());
            runDetails.Add("validationStart", validationEnd.AddMinutes(-configObj.DataSampling.ValidationWindow).ToUnixTimeMilliseconds().ToString());
            runDetails.Add("validationEnd", validationEnd.ToUnixTimeMilliseconds().ToString());
            runDetails.Add("validationEndOffset", configObj.DataSampling.ValidationEndOffset);

            // Sampling range details
            runDetails.Add("samplingWindow", configObj.DataSampling.SamplingWindow.ToString());
            if (samplingRange != null)
            {
                runDetails.Add("samplingStart", samplingRange.Start.Value.ToString());
                runDetails.Add("samplingEnd", samplingRange.End.Value.ToString());
            }
            runDetails.Add("samplingGranularity", configObj.DataSampling.Granularity.ToString());

            // Logical check details
            bool logicalCheckEnabled = configObj.LogicalCheck != null && configObj.LogicalCheck.Enabled;
            runDetails.Add("logicalCheckEnabled", logicalCheckEnabled.ToString());
            if (logicalCheckEnabled)
            {
                runDetails.Add("logicalCheckTimeSeries", configObj.LogicalCheck.ExternalId);
                runDetails.Add("logicalCheckSamplingMethod", configObj.LogicalCheck.AggregateType);
                runDetails.Add("logicalCheckOperation", configObj.LogicalCheck.Check);
                runDetails.Add("logicalCheckThresholdValue", configObj.LogicalCheck.Value.ToString());
            }

            // Steady state details
            bool ssdEnabled = configObj.SteadyStateDetection != null && configObj.SteadyStateDetection.Enabled;
            runDetails.Add("ssdEnabled", ssdEnabled.ToString());
            if (ssdEnabled)
            {
                runDetails.Add("ssdTimeSeries", configObj.SteadyStateDetection.ExternalId);
                runDetails.Add("ssdSamplingMethod", configObj.SteadyStateDetection.AggregateType);
                runDetails.Add("ssdMinSectionSize", configObj.SteadyStateDetection.MinSectionSize.ToString());
                runDetails.Add("ssdVarThreshold", configObj.SteadyStateDetection.VarThreshold.ToString());
                runDetails.Add("ssdSlopeThreshold", configObj.SteadyStateDetection.SlopeThreshold.ToString());
            }

            // Input time series details
            foreach (var input in configObj.InputTimeSeries)
            {
                runDetails.Add($"inputTimeSeries{input.Type}", input.SensorExternalId);
                runDetails.Add($"inputSamplingMethod{input.Type}", input.AggregateType);
            }
            // Determine what is the sequence id and the row number to start inserting data
            var sequenceId = configState.RunDataSequence;
            long rowStart = 0;
            if (sequenceId != null)
            {
                rowStart = configState.RunSequenceLastRow + 1;

                // Create a new sequence if reached the configured row limit
                if (runDetails.Count + rowStart > _connectorConfig.MaximumNumberOfSequenceRows)
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
                    dataSetId,
                    configObj.Calculation,
                    runDetails,
                    token).ConfigureAwait(false);

                if (string.IsNullOrEmpty(sequenceId))
                {
                    sequenceId = seq.ExternalId;
                }

                // Update the local state with the sequence ID and the last row number
                configState.RunDataSequence = sequenceId;
                configState.RunSequenceLastRow = runDetails.Count + rowStart - 1;
                await ConfigurationLibrary.StoreLibraryState(token).ConfigureAwait(false);

                // Update the event with calculation time and run details sequence
                
                // Dictionary<string, string> eventMetaData = new Dictionary<string, string>()
                // {
                //     { "runConfigurationSequence", seq.ExternalId },
                //     { "runConfigurationRowStart", rowStart.ToString() },
                //     { "runConfigurationRowEnd", configState.RunSequenceLastRow.ToString() }
                // };
                // if (samplingRange != null)
                // {
                //     eventMetaData.Add("calcTime", samplingRange.Midpoint.ToString());
                // }
                // await _cdfEvents.UpdateSimulationEvent(
                //     runEvent.ExternalId,
                //     eventStartTime,
                //     eventMetaData,
                //     token).ConfigureAwait(false);
            }
            catch (SimulationRunConfigurationException e)
            {
                throw new ConnectorException(e.Message, e.CogniteErrors);
            }
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
