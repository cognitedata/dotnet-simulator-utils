using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cognite.Extensions;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Simulation runner for simulation configurations of type <see cref="SimulationConfigurationWithRoutine"/>
    /// </summary>
    /// <typeparam name="T">Type of model state objects</typeparam>
    /// <typeparam name="U">Type of simulation configuration state objects</typeparam>
    /// <typeparam name="V">Type of simulation configuration objects</typeparam>
    public abstract class RoutineRunnerBase<T, U, V> : SimulationRunnerBase<T, U, V>
        where T : ModelStateBase
        where U : ConfigurationStateBase
        where V : SimulationConfigurationWithRoutine
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
            IConfigurationProvider<U, V> configLibrary,
            ISimulatorClient<T, V> simulatorClient,
            ILogger logger) : 
            base(connectorConfig, simulators, cdf, modelLibrary, configLibrary, logger)
        {
            _logger = logger;
            _cdf = cdf;
            SimulatorClient = simulatorClient;
        }

        /// <summary>
        /// Run the given simulation event by parsing and executing the simulation routine associated with it
        /// </summary>
        /// <param name="e">Simulation event</param>
        /// <param name="startTime">Simulation start time</param>
        /// <param name="modelState">Model state</param>
        /// <param name="configState">Configuration state</param>
        /// <param name="configObj">Configuration object</param>
        /// <param name="samplingRange">Input sampling range</param>
        /// <param name="token">Cancellation token</param>
        /// <exception cref="ArgumentNullException">When one of the arguments is missing</exception>
        /// <exception cref="SimulationException">When it was not possible to sample data points</exception>
        /// <exception cref="ConnectorException">When it was not possible to save the results in CDF</exception>
        protected override async Task RunSimulation(
            SimulationRunEvent e, 
            DateTime startTime, 
            T modelState, 
            U configState, 
            V configObj, 
            SamplingRange samplingRange, 
            CancellationToken token)
        {
            if (modelState == null)
            {
                throw new ArgumentNullException(nameof(modelState));
            }
            if (configObj == null)
            {
                throw new ArgumentNullException(nameof(configObj));
            }
            if (samplingRange == null)
            {
                throw new ArgumentNullException(nameof(samplingRange));
            }
            if (e == null)
            {
                throw new ArgumentNullException(nameof(e));
            }
            _logger.LogInformation("Started running simulation event {ID}", e.HasSimulationRun ? e.Run.Id.ToString() : e.Event.ExternalId);

            var timeSeries = _cdf.CogniteClient.TimeSeries;
            var inputData = new Dictionary<string, double>();

            var outputTsToCreate = new List<SimulationOutput>();
            var inputTsToCreate = new List<SimulationInput>();
            IDictionary<Identity, IEnumerable<Datapoint>> dpsToCreate = new Dictionary<Identity, IEnumerable<Datapoint>>();

            // Collect sampled inputs, to run simulations and to store as time series and data points
            foreach (var inputTs in configObj.InputTimeSeries)
            {
                var dps = await _cdf.CogniteClient.DataPoints.GetSample(
                    inputTs.SensorExternalId,
                    inputTs.AggregateType.ToDataPointAggregate(),
                    configObj.DataSampling.Granularity,
                    samplingRange,
                    token).ConfigureAwait(false);
                var inputDps = dps.ToTimeSeriesData(
                    configObj.DataSampling.Granularity,
                    inputTs.AggregateType.ToDataPointAggregate());
                if (inputDps.Count == 0)
                {
                    throw new SimulationException($"Could not find data points in input timeseries {inputTs.SensorExternalId}");
                }

                // This assumes the unit specified in the configuration is the same as the time series unit
                // No unit conversion is made
                var averageValue = inputDps.GetAverage();
                inputData[inputTs.Type] = averageValue;
                var simInput = new SimulationInput
                {
                    Calculation = configObj.Calculation,
                    Name = inputTs.Name,
                    Type = inputTs.Type,
                    Unit = inputTs.Unit,
                };

                // If the sampled input is to be saved with an external ID different than the
                // auto-generated one
                if (!string.IsNullOrEmpty(inputTs.SampleExternalId))
                {
                    simInput.OverwriteTimeSeriesId(inputTs.SampleExternalId);
                }

                inputTsToCreate.Add(simInput);
                dpsToCreate.Add(
                    new Identity(simInput.TimeSeriesExternalId),
                    new List<Datapoint> 
                    { 
                        new Datapoint(samplingRange.Midpoint, averageValue) 
                    });
            }
            var results = await SimulatorClient
                .RunSimulation(modelState, configObj, inputData)
                .ConfigureAwait(false);

            _logger.LogDebug("Saving simulation results as time series");
            foreach (var output in configObj.OutputTimeSeries)
            {
                if (results.ContainsKey(output.Type))
                {
                    var outputTs = new SimulationOutput
                    {
                        Calculation = configObj.Calculation,
                        Name = output.Name,
                        Type = output.Type,
                        Unit = output.Unit,
                    };
                    if (!string.IsNullOrEmpty(output.ExternalId))
                    {
                        outputTs.OverwriteTimeSeriesId(output.ExternalId);
                    }
                    outputTsToCreate.Add(outputTs);

                    dpsToCreate.Add(
                        new Identity(outputTs.TimeSeriesExternalId),
                        new List<Datapoint> 
                        { 
                            new Datapoint(samplingRange.Midpoint, results[output.Type]) 
                        });
                }
            }
            try
            {
                //Store model version time series
                var mvts = await timeSeries
                    .GetOrCreateSimulationModelVersion(configObj.Calculation, modelState.DataSetId, token)
                    .ConfigureAwait(false);
                dpsToCreate.Add(
                    new Identity(mvts.ExternalId),
                    new List<Datapoint> { new Datapoint(samplingRange.Midpoint, modelState.Version) });
                
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
        where V : SimulationConfigurationWithRoutine
    {
        /// <summary>
        /// Run a simulation by executing the routine passed as parameter with
        /// the given input data
        /// </summary>
        /// <param name="modelState">Model state object</param>
        /// <param name="simulationConfiguration">Simulation configuration object</param>
        /// <param name="inputData">Input data</param>
        /// <returns></returns>
        Task<Dictionary<string, double>> RunSimulation(
            T modelState, 
            V simulationConfiguration, 
            Dictionary<string, double> inputData);
    } 
}