using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extensions;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk;
using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Simulation runner for simulation routine revision of type <see cref="SimulatorRoutineRevision"/>
    /// </summary>
    /// <typeparam name="A">Type of automation configuration objects</typeparam>
    /// <typeparam name="T">Type of model state objects</typeparam>
    /// <typeparam name="V">Type of simulation configuration objects</typeparam>
    public abstract class RoutineRunnerBase<A, T, V> : SimulationRunnerBase<A, T, V>
        where A : AutomationConfig
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
        /// <param name="simulatorDefinition">Simulator definition</param>
        /// <param name="cdf">CDF client</param>
        /// <param name="modelLibrary">Model library</param>
        /// <param name="configLibrary">Configuration library</param>
        /// <param name="simulatorClient">Simulator client</param>
        /// <param name="logger">Logger</param>
        protected RoutineRunnerBase(
            ConnectorConfig connectorConfig,
            SimulatorCreate simulatorDefinition,
            CogniteDestination cdf,
            IModelProvider<A, T> modelLibrary,
            IRoutineProvider<V> configLibrary,
            ISimulatorClient<T, V> simulatorClient,
            ILogger logger) :
            base(connectorConfig, simulatorDefinition, cdf, modelLibrary, configLibrary, logger)
        {
            _logger = logger;
            _cdf = cdf;
            SimulatorClient = simulatorClient;
        }

        private async Task<Dictionary<string, SimulatorValueItem>> LoadSimulationInputOverrides(long runId, CancellationToken token)
        {
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
        /// Run the given simulation run by parsing and executing the simulation routine associated with it
        /// </summary>
        /// <param name="runItem">Simulation run item</param>
        /// <param name="startTime">Simulation start time</param>
        /// <param name="modelState">Model state</param>
        /// <param name="routineRevision">Routine revision object</param>
        /// <param name="samplingConfiguration">Input sampling samplingConfiguration</param>
        /// <param name="token">Cancellation token</param>
        /// <exception cref="ArgumentNullException">When one of the arguments is missing</exception>
        /// <exception cref="SimulationException">When it was not possible to sample data points</exception>
        /// <exception cref="ConnectorException">When it was not possible to save the results in CDF</exception>
        protected override async Task RunRoutine(
            SimulationRunItem runItem,
            DateTime startTime,
            T modelState,
            V routineRevision,
            SamplingConfiguration samplingConfiguration,
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
            if (samplingConfiguration == null)
            {
                throw new ArgumentNullException(nameof(samplingConfiguration));
            }
            if (runItem == null)
            {
                throw new ArgumentNullException(nameof(runItem));
            }
            _logger.LogInformation("Started executing simulation run {ID}", runItem.Run.Id.ToString());

            var timeSeries = _cdf.CogniteClient.TimeSeries;
            var inputData = new Dictionary<string, SimulatorValueItem>();
            var inputDataOverrides = await LoadSimulationInputOverrides(runItem.Run.Id, token).ConfigureAwait(false);

            var outputTsToCreate = new List<SimulationOutput>();
            var inputTsToCreate = new List<SimulationInput>();
            IDictionary<Identity, IEnumerable<Datapoint>> dpsToCreate = new Dictionary<Identity, IEnumerable<Datapoint>>();
            var routineRevisionInfo = new SimulatorRoutineRevisionInfo()
            {
                ExternalId = routineRevision.ExternalId,
                Model = new SimulatorModelInfo
                {
                    ExternalId = modelState.ModelExternalId,
                    Simulator = routineRevision.SimulatorExternalId,
                },
                RoutineExternalId = routineRevision.RoutineExternalId,
            };

            var configObj = routineRevision.Configuration;

            // Collect constant inputs, to run simulations and to store as time series and data points
            if (configObj.Inputs != null)
            {
                foreach (var originalInput in configObj.Inputs.Where(i => i.IsConstant))
                {
                    // constant values should be read directly from the run data as they may be overridden per run
                    if (!inputDataOverrides.TryGetValue(originalInput.ReferenceId, out var inputValue))
                    {
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

                    if (simInput.ShouldSaveToTimeSeries)
                    {
                        if (inputValue.Value.Type == SimulatorValueType.DOUBLE)
                        {
                            var inputConstValue = (inputValue.Value as SimulatorValue.Double).Value;

                            inputTsToCreate.Add(simInput);
                            dpsToCreate.Add(
                                new Identity(simInput.SaveTimeseriesExternalId),
                                new List<Datapoint>
                                {
                                    new Datapoint(samplingConfiguration.SimulationTime, inputConstValue)
                                });
                        }
                        else
                        {
                            throw new SimulationException($"Could not save input value for {originalInput.Name} ({originalInput.ReferenceId}). Only double precision values can be saved to time series.");
                        }
                    }
                }
            }

            // Collect sampled inputs, to run simulations and to store as time series and data points
            foreach (var inputTs in configObj.Inputs.Where(i => i.IsTimeSeries))
            {
                // time series inputs could be overridden per run, in these cases the constant value should be read from the run data
                if (!inputDataOverrides.TryGetValue(inputTs.ReferenceId, out var inputValue))
                {
                    inputValue = await _cdf.CogniteClient.LoadTimeseriesSimulationInput(inputTs, configObj, samplingConfiguration, token).ConfigureAwait(false);
                }

                inputData[inputTs.ReferenceId] = inputValue;

                var simInput = new SimulationInput
                {
                    RoutineRevisionInfo = routineRevisionInfo,
                    Name = inputTs.Name,
                    ReferenceId = inputTs.ReferenceId,
                    Unit = inputValue?.Unit?.Name,
                    SaveTimeseriesExternalId = inputTs.SaveTimeseriesExternalId
                };

                if (simInput.ShouldSaveToTimeSeries)
                {
                    var inputRawValue = (inputValue.Value as SimulatorValue.Double).Value;

                    inputTsToCreate.Add(simInput);
                    dpsToCreate.Add(
                        new Identity(simInput.SaveTimeseriesExternalId),
                        new List<Datapoint>
                        {
                            new Datapoint(samplingConfiguration.SimulationTime, inputRawValue)
                        });
                }
            }
            var results = await SimulatorClient
                .RunSimulation(modelState, routineRevision, inputData, token)
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
                        Unit = output.Unit?.Name,
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
                                new Datapoint(samplingConfiguration.SimulationTime, value)
                            });
                    }
                    else
                    {
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
                        Id = runItem.Run.Id,
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
        /// <param name="token">Cancellation token</param>
        /// <returns></returns>
        Task<Dictionary<string, SimulatorValueItem>> RunSimulation(
            T modelState,
            V simulationConfiguration,
            Dictionary<string, SimulatorValueItem> inputData, CancellationToken token);

        /// <summary>
        /// This method should open the model versions in the simulator, extract the required information and
        /// ingest it to CDF. 
        /// </summary>
        /// <param name="state">Model file states</param>
        /// <param name="_token">Cancellation token</param>
        Task ExtractModelInformation(T state, CancellationToken _token);

        /// <summary>
        /// Returns the version of the given simulator. The connector reads the version and
        /// report it back to CDF
        /// </summary>
        /// <returns>Version</returns>
        string GetSimulatorVersion(CancellationToken _token);

        /// <summary>
        /// Returns the connector version. This is reported periodically to CDF
        /// </summary>
        /// <returns>Connector version</returns>
        string GetConnectorVersion(CancellationToken _token);

        /// <summary>
        /// Tests the connection to the simulator.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>A task representing the asynchronous operation</returns>
        Task TestConnection(CancellationToken token);
    }
}