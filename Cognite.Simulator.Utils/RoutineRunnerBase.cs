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
    /// <typeparam name="U">Type of simulation configuration state objects</typeparam>
    /// <typeparam name="V">Type of simulation configuration objects</typeparam>
    public abstract class RoutineRunnerBase<T, U, V> : SimulationRunnerBase<T, U, V>
        where T : ModelStateBase
        where U : ConfigurationStateBase
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
            U configState, 
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
            var inputData = new Dictionary<string, double>();

            var outputTsToCreate = new List<SimulationOutput>();
            var inputTsToCreate = new List<SimulationInput>();
            IDictionary<Identity, IEnumerable<Datapoint>> dpsToCreate = new Dictionary<Identity, IEnumerable<Datapoint>>();

            var configObj = routineRevision.Configuration;

            // Collect manual inputs, to run simulations and to store as time series and data points
            if (configObj.Inputs != null) {
                foreach (var inputValue in configObj.Inputs.Where(i => i.IsConstant))
                {
                    var simInput = new SimulationInput
                    {
                        RoutineRevisionInfo = new SimulatorRoutineRevisionInfo()
                        {
                            ExternalId = routineRevision.ExternalId,
                            Model = new SimulatorModelInfo
                            {
                                ExternalId = modelState.ModelExternalId,
                                Name = modelState.ModelName,
                                Simulator = routineRevision.SimulatorExternalId,
                            },
                            RoutineExternalId = routineRevision.RoutineExternalId,
                        },
                        ReferenceId = inputValue.ReferenceId,
                        Name = inputValue.Name,
                        Unit = inputValue.Unit.Name,
                        SaveTimeseriesExternalId = inputValue.SaveTimeseriesExternalId
                    };

                    // // If the manual input is to be saved with an external ID different than the
                    // // auto-generated one
                    // // TODO: this should be optional now
                    // if (!string.IsNullOrEmpty(inputValue.SaveTimeseriesExternalId))
                    // {
                    //     simInput.OverwriteTimeSeriesId(inputValue.SaveTimeseriesExternalId);
                    // }
                    
                    if (inputValue.Value.Type != SimulatorValueType.DOUBLE)
                    {
                        throw new SimulationException($"Could not parse input constant {inputValue.Name} with value {inputValue.Value}. Only double precision values are supported.");
                    }
                    var inputConstValue = (inputValue.Value as SimulatorValue.Double).Value;

                    inputData[inputValue.ReferenceId] = inputConstValue;

                    if (!String.IsNullOrEmpty(simInput.SaveTimeseriesExternalId)) {
                        inputTsToCreate.Add(simInput);
                        dpsToCreate.Add(
                            new Identity(simInput.SaveTimeseriesExternalId),
                            new List<Datapoint> 
                            { 
                                new Datapoint(samplingRange.Midpoint, inputConstValue) 
                            });

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
                inputData[inputTs.ReferenceId] = averageValue;
                var simInput = new SimulationInput
                {
                    RoutineRevisionInfo = new SimulatorRoutineRevisionInfo
                    {
                        ExternalId = routineRevision.ExternalId,
                        Model = new SimulatorModelInfo
                        {
                            ExternalId = modelState.ModelExternalId,
                            Name = modelState.ModelName,
                            Simulator = routineRevision.SimulatorExternalId,
                        },
                        RoutineExternalId = routineRevision.RoutineExternalId
                    },
                    Name = inputTs.Name,
                    ReferenceId = inputTs.ReferenceId,
                    Unit = inputTs.Unit.Name,
                };

                // // If the sampled input is to be saved with an external ID different than the
                // // auto-generated one
                // if (!string.IsNullOrEmpty(inputTs.SaveTimeseriesExternalId))
                // {
                //     simInput.OverwriteTimeSeriesId(inputTs.SaveTimeseriesExternalId);
                // }

                if (!String.IsNullOrEmpty(simInput.SaveTimeseriesExternalId)) {
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
            foreach (var output in configObj.Outputs.Where(o => !String.IsNullOrEmpty(o.SaveTimeseriesExternalId)))
            {
                if (results.ContainsKey(output.ReferenceId))
                {
                    var outputTs = new SimulationOutput
                    {
                        RoutineRevisionInfo = new SimulatorRoutineRevisionInfo
                        {
                            ExternalId = routineRevision.ExternalId,
                            Model = new SimulatorModelInfo
                            {
                                ExternalId = modelState.ModelExternalId,
                                Name = modelState.ModelName,
                                Simulator = routineRevision.SimulatorExternalId,
                            },
                            RoutineExternalId = routineRevision.RoutineExternalId
                        },
                        SaveTimeseriesExternalId = output.SaveTimeseriesExternalId,
                        Name = output.Name,
                        ReferenceId = output.ReferenceId,
                        Unit = output.Unit.Name,
                    };
                    // if (!string.IsNullOrEmpty(output.SaveTimeseriesExternalId))
                    // {
                    //     outputTs.Sa(output.SaveTimeseriesExternalId); // TODO this should be optional
                    // }
                    outputTsToCreate.Add(outputTs);

                    dpsToCreate.Add(
                        new Identity(outputTs.SaveTimeseriesExternalId),
                        new List<Datapoint> 
                        { 
                            new Datapoint(samplingRange.Midpoint, results[output.ReferenceId]) 
                        });

                }
            }
            try
            {
                //Store model version time series TODO remove this
                // var mvts = await timeSeries
                //     .GetOrCreateSimulationModelVersion(configObj.RoutineExternalId, modelState.DataSetId, token)
                //     .ConfigureAwait(false);
                // dpsToCreate.Add(
                //     new Identity(mvts.ExternalId),
                //     new List<Datapoint> { new Datapoint(samplingRange.Midpoint, modelState.Version) });
                
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
        Task<Dictionary<string, double>> RunSimulation(
            T modelState, 
            V simulationConfiguration, 
            Dictionary<string, double> inputData);
    } 
}