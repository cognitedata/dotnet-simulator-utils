using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognite.Extensions;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Simulation runner for simulation routine revision of type <see cref="SimulatorRoutineRevision"/>
    /// </summary>
    /// <typeparam name="T">Type of model state objects</typeparam>
    /// <typeparam name="V">Type of simulation configuration objects</typeparam>
    public abstract class RoutineRunnerBase<T, V> : SimulationRunnerBase<T, V>
        where T : ModelStateBase
        where V : SimulatorRoutineRevision
    {
        private readonly CogniteDestination _cdf;
        private readonly ILogger _logger;

        /// <summary>
        /// Client that implements the connector with a simulator
        /// </summary>
        protected ISimulatorClient<T, V> SimulatorClient { get; }

        /// <summary>
        /// Creates an instance of the runner with the provided parameters
        /// </summary>
        /// <param name="connectorConfig">Connector configuration</param>
        /// <param name="simulators">List of simulators</param>
        /// <param name="cdf">CDF client</param>
        /// <param name="modelLibrary">Model library</param>
        /// <param name="configLibrary">Configuration library</param>
        /// <param name="simulatorClient">Simulator client</param>
        /// <param name="logger">Logger</param>
        protected RoutineRunnerBase(
            ConnectorConfig connectorConfig, 
            IList<SimulatorConfig> simulators, 
            CogniteDestination cdf,
            IModelProvider<T> modelLibrary, 
            IRoutineProvider<V> configLibrary,
            ISimulatorClient<T, V> simulatorClient,
            ILogger logger) : 
            base(connectorConfig, simulators, cdf, modelLibrary, configLibrary, logger)
        {
            _logger = logger;
            _cdf = cdf;
            SimulatorClient = simulatorClient;
        }

        private async Task<Dictionary<string, SimulatorValueItem>> LoadSimulationInputOverrides(long runId, CancellationToken token) {
            var inputDataOverrides = new Dictionary<string, SimulatorValueItem>();

            var dataRes = await _cdf.CogniteClient.Alpha.Simulators.ListSimulationRunsDataAsync(
                new List<long> { runId },
                token: token).ConfigureAwait(false);
            var dataResItem = dataRes.FirstOrDefault();
            if (dataResItem != null && dataResItem.Inputs != null)
            {
                inputDataOverrides = dataResItem.Inputs.ToDictionarySafe(i => i.ReferenceId, i => i);
            }

            return inputDataOverrides;
        }

        /// <summary>
        /// Run the given simulation event by parsing and executing the simulation routine associated with it
        /// </summary>
        /// <param name="e">Simulation event</param>
        /// <param name="startTime">Simulation start time</param>
        /// <param name="modelState">Model state</param>
        /// <param name="routineRevision">Routine revision object</param>
        /// <param name="samplingRange">Input sampling range</param>
        /// <param name="token">Cancellation token</param>
        /// <exception cref="ArgumentNullException">When one of the arguments is missing</exception>
        /// <exception cref="SimulationException">When it was not possible to sample data points</exception>
        /// <exception cref="ConnectorException">When it was not possible to save the results in CDF</exception>
        protected override async Task RunSimulation(
            SimulationRunEvent e, 
            DateTime startTime, 
            T modelState, 
            V routineRevision, 
            SamplingRange samplingRange, 
            CancellationToken token)
        {
            if (modelState == null)
            {
                throw new ArgumentNullException(nameof(modelState));
            }
            if (routineRevision == null)
            {
                throw new ArgumentNullException(nameof(routineRevision));
            }
            if (samplingRange == null)
            {
                throw new ArgumentNullException(nameof(samplingRange));
            }
            if (e == null)
            {
                throw new ArgumentNullException(nameof(e));
            }
            _logger.LogInformation("Started running simulation event {ID}", e.Run.Id.ToString());

            var timeSeries = _cdf.CogniteClient.TimeSeries;
            var inputData = new Dictionary<string, SimulatorValueItem>();
            var inputDataOverrides = await LoadSimulationInputOverrides(e.Run.Id, token).ConfigureAwait(false);

            var outputTsToCreate = new List<SimulationOutput>();
            var inputTsToCreate = new List<SimulationInput>();
            IDictionary<Identity, IEnumerable<Datapoint>> dpsToCreate = new Dictionary<Identity, IEnumerable<Datapoint>>();
            var routineRevisionInfo = new SimulatorRoutineRevisionInfo()
                {
                    ExternalId = routineRevision.ExternalId,
                    Model = new SimulatorModelInfo
                    {
                        ExternalId = modelState.ModelExternalId,
                        Name = modelState.ModelName,
                        Simulator = routineRevision.SimulatorExternalId,
                    },
                    RoutineExternalId = routineRevision.RoutineExternalId,
                };

            var configObj = routineRevision.Configuration;

            // Collect constant inputs, to run simulations and to store as time series and data points
            if (configObj.Inputs != null) {
                foreach (var originalInput in configObj.Inputs.Where(i => i.IsConstant))
                {
                    // constant values should be read directly from the run data as they may be overridden per run
                    if (!inputDataOverrides.TryGetValue(originalInput.ReferenceId, out var inputValue)) {
                        throw new SimulationException($"Could not find input value for {originalInput.Name} ({originalInput.ReferenceId}).");
                    }
                    
                    var simInput = new SimulationInput
                    {
                        RoutineRevisionInfo = routineRevisionInfo,
                        ReferenceId = inputValue.ReferenceId,
                        Name = originalInput.Name,
                        Unit = inputValue.Unit?.Name,
                        SaveTimeseriesExternalId = originalInput.SaveTimeseriesExternalId
                    };
                    
                    inputData[inputValue.ReferenceId] = inputValue;

                    if (simInput.ShouldSaveToTimeSeries) {
                        if (inputValue.Value.Type == SimulatorValueType.DOUBLE) {
                            var inputConstValue = (inputValue.Value as SimulatorValue.Double).Value;

                            inputTsToCreate.Add(simInput);
                            dpsToCreate.Add(
                                new Identity(simInput.SaveTimeseriesExternalId),
                                new List<Datapoint> 
                                { 
                                    new Datapoint(samplingRange.Midpoint, inputConstValue) 
                                });
                        } else {
                            throw new SimulationException($"Could not save input value for {originalInput.Name} ({originalInput.ReferenceId}). Only double precision values can be saved to time series.");
                        }
                    }
                }
            }

            // Collect sampled inputs, to run simulations and to store as time series and data points
            foreach (var inputTs in configObj.Inputs.Where(i => i.IsTimeSeries))
            {
                var dps = await _cdf.CogniteClient.DataPoints.GetSample(
                    inputTs.SourceExternalId,
                    inputTs.Aggregate.ToDataPointAggregate(),
                    configObj.DataSampling.Granularity,
                    samplingRange,
                    token).ConfigureAwait(false);
                var inputDps = dps.ToTimeSeriesData(
                    configObj.DataSampling.Granularity,
                    inputTs.Aggregate.ToDataPointAggregate());
                if (inputDps.Count == 0)
                {
                    throw new SimulationException($"Could not find data points in input timeseries {inputTs.SourceExternalId}");
                }

                // This assumes the unit specified in the configuration is the same as the time series unit
                // No unit conversion is made
                var averageValue = inputDps.GetAverage();
                inputData[inputTs.ReferenceId] = new SimulatorValueItem()
                {
                    Value = new SimulatorValue.Double(averageValue),
                    Unit = inputTs.Unit != null ? new SimulatorValueUnit() {
                        Name = inputTs.Unit.Name
                    } : null,
                    Overridden = false,
                    ReferenceId = inputTs.ReferenceId,
                    TimeseriesExternalId = inputTs.SaveTimeseriesExternalId,
                    ValueType = SimulatorValueType.DOUBLE,
                };
                var simInput = new SimulationInput
                {
                    RoutineRevisionInfo = routineRevisionInfo,
                    Name = inputTs.Name,
                    ReferenceId = inputTs.ReferenceId,
                    Unit = inputTs.Unit.Name,
                    SaveTimeseriesExternalId = inputTs.SaveTimeseriesExternalId
                };

                if (simInput.ShouldSaveToTimeSeries) {
                    inputTsToCreate.Add(simInput);
                    dpsToCreate.Add(
                        new Identity(simInput.SaveTimeseriesExternalId),
                        new List<Datapoint> 
                        { 
                            new Datapoint(samplingRange.Midpoint, averageValue) 
                        });
                }
            }
            var results = await SimulatorClient
                .RunSimulation(modelState, routineRevision, inputData)
                .ConfigureAwait(false);

            _logger.LogDebug("Saving simulation results as time series");
            foreach (var output in configObj.Outputs.Where(o => !string.IsNullOrEmpty(o.SaveTimeseriesExternalId)))
            {
                if (results.ContainsKey(output.ReferenceId))
                {
                    var outputTs = new SimulationOutput
                    {
                        RoutineRevisionInfo = routineRevisionInfo,
                        SaveTimeseriesExternalId = output.SaveTimeseriesExternalId,
                        Name = output.Name,
                        ReferenceId = output.ReferenceId,
                        Unit = output.Unit.Name,
                    };

                    outputTsToCreate.Add(outputTs);

                    var valueItem = results[output.ReferenceId];
                    if (valueItem.Value.Type == SimulatorValueType.DOUBLE)
                    {
                        var value = (valueItem.Value as SimulatorValue.Double).Value;
                        dpsToCreate.Add(
                            new Identity(outputTs.SaveTimeseriesExternalId),
                            new List<Datapoint> 
                            { 
                                new Datapoint(samplingRange.Midpoint, value) 
                            });  
                    } else {
                        throw new SimulationException($"Could not save output value for {output.Name} ({output.ReferenceId}). Only double precision values can be saved to time series.");
                    }
                }
            }
            try
            {
                // saving the inputs/outputs back to the simulation run
                await _cdf.CogniteClient.Alpha.Simulators.SimulationRunCallbackAsync(
                    new SimulationRunCallbackItem()
                    {
                        Id = e.Run.Id,
                        Status = SimulationRunStatus.running,
                        Inputs = inputData.Values,
                        Outputs = results.Values,
                    }, token).ConfigureAwait(false);

                // Store input time series
                await timeSeries
                    .GetOrCreateSimulationInputs(inputTsToCreate, modelState.DataSetId, token)
                    .ConfigureAwait(false);

                // Store output time series
                await timeSeries
                    .GetOrCreateSimulationOutputs(outputTsToCreate, modelState.DataSetId, token)
                    .ConfigureAwait(false);

            }
            catch (SimulationModelVersionCreationException ex)
            {
                throw new ConnectorException(ex.Message, ex.CogniteErrors);
            }
            catch (SimulationTimeSeriesCreationException ex)
            {
                throw new ConnectorException(ex.Message, ex.CogniteErrors);
            }
            var dpResult = await _cdf.InsertDataPointsAsync(
                dpsToCreate,
                SanitationMode.None,
                RetryMode.OnError,
                token).ConfigureAwait(false);

            _logger.LogInformation("Simulation results saved successfully");

            if (!dpResult.IsAllGood)
            {
                throw new ConnectorException($"Could not create data points for time series in CDF", dpResult.Errors);
            }
        }
    }

    /// <summary>
    /// Interface to be implemented by simulator integration clients that can
    /// execute simulation configuration of the type <typeparamref name="V"/>
    /// </summary>
    /// <typeparam name="T">Type of the model state object</typeparam>
    /// <typeparam name="V">Type of the simulation configuration object</typeparam>
    public interface ISimulatorClient<T, V> 
        where T : ModelStateBase
        where V : SimulatorRoutineRevision
    {
        /// <summary>
        /// Run a simulation by executing the routine passed as parameter with
        /// the given input data
        /// </summary>
        /// <param name="modelState">Model state object</param>
        /// <param name="simulationConfiguration">Simulation configuration object</param>
        /// <param name="inputData">Input data</param>
        /// <returns></returns>
        Task<Dictionary<string, SimulatorValueItem>> RunSimulation(
            T modelState, 
            V simulationConfiguration, 
            Dictionary<string, SimulatorValueItem> inputData);
    } 
}