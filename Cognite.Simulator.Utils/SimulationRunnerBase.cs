﻿using Cognite.Extensions;
using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using CogniteSdk.Resources;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    public abstract class SimulationRunnerBase<T, U, V>
        where T : ModelStateBase
        where U : ConfigurationStateBase
        where V : SimulationConfigurationWithDataSampling
    {
        private readonly ConnectorConfig _connectorConfig;
        private readonly IList<SimulatorConfig> _simulators;
        private readonly EventsResource _cdfEvents;
        private readonly SequencesResource _cdfSequences;
        private readonly DataPointsResource _cdfDataPoints;
        private readonly ILogger _logger;
        private readonly IModelProvider<T> _modelLib;
        private readonly IConfigurationProvider<U, V> _configLib;

        public SimulationRunnerBase(
            ConnectorConfig connectorConfig,
            IList<SimulatorConfig> simulators,
            CogniteDestination cdf,
            IModelProvider<T> modelLibrary,
            IConfigurationProvider<U, V> configLibrary,
            ILogger logger)
        {
            _connectorConfig = connectorConfig;
            _simulators = simulators;
            _cdfEvents = cdf.CogniteClient.Events;
            _cdfSequences = cdf.CogniteClient.Sequences;
            _cdfDataPoints = cdf.CogniteClient.DataPoints;
            _logger = logger;
            _modelLib = modelLibrary;
            _configLib = configLibrary;
        }

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
                if (simulationEvents.Any())
                {
                    _logger.LogInformation(
                        "{Number} simulation event(s) ready to run found in CDF",
                        simulationEvents.Count());
                }

                // Find events that are running. Should not have any, as the connector runs events in sequence.
                // Any running events indicates that the connector went down during the run, and the event should fail
                var simulationRunningEvents = await _cdfEvents.FindSimulationEventsRunning(
                    simulators,
                    _connectorConfig.GetConnectorName(),
                    token).ConfigureAwait(false);
                if (simulationRunningEvents.Any())
                {
                    _logger.LogWarning(
                        "{Number} simulation event(s) that are running (but should have finished) found in CDF",
                        simulationRunningEvents.Count());
                }
                var allEvents = new List<Event>(simulationEvents);
                allEvents.AddRange(simulationRunningEvents);

                foreach (Event e in allEvents)
                {
                    var startTime = DateTime.UtcNow;
                    try
                    {
                        if (e.Metadata[SimulationEventMetadata.StatusKey] == SimulationEventStatusValues.Running)
                        {
                            throw new ConnectorException("Calculation failed due to connector error");
                        }
                        var eventAge = startTime - CogniteTime.FromUnixTimeMilliseconds(e.LastUpdatedTime);
                        if (eventAge >= TimeSpan.FromSeconds(_connectorConfig.SimulationEventTolerance))
                        {
                            throw new TimeoutException("Timeout: The connector could not run the calculation on time");
                        }
                        var (modelState, calcState, calcObj) = ValidateEventMetadata(e);
                        var metadata = new Dictionary<string, string>();
                        InitSimulationEventMetadata(
                            modelState,
                            calcState,
                            calcObj,
                            metadata);
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
                        await _cdfEvents.UpdateSimulationEventToFailure(
                            e.ExternalId,
                            startTime,
                            null,
                            ex.Message.LimitUtf8ByteCount(Sanitation.EventMetadataMaxPerValue),
                            token).ConfigureAwait(false);
                    }
                }

                await Task.Delay(interval, token).ConfigureAwait(false);
            }
        }

        private (T, U, V) ValidateEventMetadata(Event e)
        {
            // Check for the needed files before start, fail the run if anything missing
            if (!e.Metadata.TryGetValue(ModelMetadata.NameKey, out string modelName))
            {
                _logger.LogError("Event {Id} does not indicate the model name to use", e.ExternalId);
                throw new SimulationException("Model name missing");
            }
            if (!e.Metadata.TryGetValue(CalculationMetadata.TypeKey, out string calcType))
            {
                _logger.LogError("Event {Id} does not indicate the calculation type to use", e.ExternalId);
                throw new SimulationException("Calculation type missing");
            }
            string calcTypeUserDefined = null;
            if (calcType == "UserDefined" && !e.Metadata.TryGetValue(CalculationMetadata.UserDefinedTypeKey, out calcTypeUserDefined))
            {
                _logger.LogError("Event {Id} is user-defined, but is missing the calculation type property", e.ExternalId);
                throw new SimulationException("Type of user-defined calculation missing");
            }
            if (!e.Metadata.TryGetValue(BaseMetadata.SimulatorKey, out string simulator))
            {
                _logger.LogError("Event {Id} does not indicate the simulator to use", e.ExternalId);
                throw new SimulationException("Simulator missing");
            }
            var model = _modelLib.GetLatestModelVersion(simulator, modelName);
            if (model == null)
            {
                _logger.LogError("Could not find a local model file to run Event {Id}", e.ExternalId);
                throw new SimulationException($"Could not find a model file for {modelName}");
            }
            var calcConfig = _configLib.GetSimulationConfiguration(simulator, modelName, calcType, calcTypeUserDefined);
            var calcState = _configLib.GetSimulationConfigurationState(simulator, modelName, calcType, calcTypeUserDefined);
            if (calcConfig == null || calcState == null)
            {
                _logger.LogError("Could not find a local configuration to run Event {Id}", e.ExternalId);
                throw new SimulationException($"Could not find a simulation configuration for {modelName}");
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

        protected virtual async Task InitSimulationRun(
            Event e,
            DateTime startTime,
            T modelState,
            U configState,
            V configObj,
            Dictionary<string, string> metadata,
            CancellationToken token)
        {
            await _cdfEvents.UpdateSimulationEventToRunning(
                e.ExternalId,
                startTime,
                metadata,
                modelState.Version,
                token).ConfigureAwait(false);

            SamplingRange samplingRange = null;
            var validationEnd = startTime;
            try
            {
                // Determine the validation end time
                if (e.Metadata.TryGetValue(SimulationEventMetadata.ValidationEndOverwriteKey, out string validationEndOverwrite)
                    && long.TryParse(validationEndOverwrite, out long overwriteValue))
                {
                    // If the event contains a validation end overwrite, use that instead of
                    // the current time
                    validationEnd = CogniteTime.FromUnixTimeMilliseconds(overwriteValue);
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
                // Save run configuration
                await StoreRunConfigurationInCdf(
                    samplingRange,
                    modelState,
                    configState,
                    configObj,
                    e,
                    startTime,
                    validationEnd,
                    token).ConfigureAwait(false);
            }

            // Run the calculation
            await RunSimulation(
                e,
                startTime,
                modelState,
                configState,
                configObj, 
                samplingRange, 
                token).ConfigureAwait(false);

            // Update event with success status
            await _cdfEvents.UpdateSimulationEventToSuccess(
                e.ExternalId,
                startTime,
                null,
                "Calculation ran to completion",
                token).ConfigureAwait(false);
        }

        protected abstract Task RunSimulation(
            Event e, 
            DateTime startTime,
            T modelState,
            U configState,
            V configObj,
            SamplingRange samplingRange,
            CancellationToken token);

        protected virtual async Task StoreRunConfigurationInCdf(
            SamplingRange samplingRange,
            T modelState,
            U configState,
            V configObj,
            Event runEvent,
            DateTime eventStartTime,
            DateTime validationEnd,
            CancellationToken token)
        {
            if (runEvent == null)
            {
                throw new ArgumentNullException(nameof(runEvent));
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
            // Create a dictionary with the run details
            var runDetails = new Dictionary<string, string>
            {
                { "runEventId", runEvent.ExternalId }
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
            runDetails.Add("logicalCheckEnabled", configObj.LogicalCheck.Enabled.ToString());
            if (configObj.LogicalCheck.Enabled)
            {
                runDetails.Add("logicalCheckTimeSeries", configObj.LogicalCheck.ExternalId);
                runDetails.Add("logicalCheckSamplingMethod", configObj.LogicalCheck.AggregateType);
                runDetails.Add("logicalCheckOperation", configObj.LogicalCheck.Check);
                runDetails.Add("logicalCheckThresholdValue", configObj.LogicalCheck.Value.ToString());
            }

            // Steady state details
            runDetails.Add("ssdEnabled", configObj.SteadyStateDetection.Enabled.ToString());
            if (configObj.SteadyStateDetection.Enabled)
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
            var calculation = new SimulatorCalculation
            {
                Model = new SimulatorModel
                {
                    Name = modelState.ModelName,
                    Simulator = configObj.Simulator
                },
                Name = configObj.CalculationName,
                Type = configObj.CalculationType,
                UserDefinedType = configObj.CalcTypeUserDefined
            };

            // Update the local state with the sequence ID and the last row number
            configState.RunDataSequence = sequenceId;
            configState.RunSequenceLastRow = runDetails.Count + rowStart - 1;
            await _configLib.StoreLibraryState(token).ConfigureAwait(false);

            // Make sure the sequence exists in CDF
            try
            {
                var seq = await _cdfSequences.StoreRunConfiguration(
                    sequenceId,
                    rowStart,
                    runEvent.DataSetId,
                    calculation,
                    runDetails,
                    token).ConfigureAwait(false);

                // Update the event with calculation time and run details sequence
                Dictionary<string, string> eventMetaData = new Dictionary<string, string>()
                {
                    { "runConfigurationSequence", seq.ExternalId },
                    { "runConfigurationRowStart", rowStart.ToString() },
                    { "runConfigurationRowEnd", configState.RunSequenceLastRow.ToString() }
                };
                if (samplingRange != null)
                {
                    eventMetaData.Add("calcTime", samplingRange.Midpoint.ToString());
                }
                await _cdfEvents.UpdateSimulationEvent(
                    runEvent.ExternalId,
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

    public class SimulationException : Exception
    {
        public SimulationException()
        {
        }

        public SimulationException(string message) : base(message)
        {
        }

        public SimulationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

}
