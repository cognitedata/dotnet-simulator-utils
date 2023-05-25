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
    public abstract class RoutineRunnerBase<T, U, V> : SimulationRunnerBase<T, U, V>
        where T : ModelStateBase
        where U : ConfigurationStateBase
        where V : SimulationConfigurationWithRoutine
    {
        private readonly CogniteDestination _cdf;
        private readonly ILogger _logger;

        protected ISimulatorClient<T, V> SimulatorClient { get; }

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

        protected override async Task RunSimulation(
            Event e, 
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
            _logger.LogInformation("Started running simulation event {ID}", e.ExternalId);

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
                    .GetOrCreateSimulationModelVersion(configObj.Calculation, e.DataSetId, token)
                    .ConfigureAwait(false);
                dpsToCreate.Add(
                    new Identity(mvts.ExternalId),
                    new List<Datapoint> { new Datapoint(samplingRange.Midpoint, modelState.Version) });
                
                // Store input time series
                await timeSeries
                    .GetOrCreateSimulationInputs(inputTsToCreate, e.DataSetId, token)
                    .ConfigureAwait(false);

                // Store output time series
                await timeSeries
                    .GetOrCreateSimulationOutputs(outputTsToCreate, e.DataSetId, token)
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

    public interface ISimulatorClient<T, V> 
        where T : ModelStateBase
        where V : SimulationConfigurationWithRoutine
    {
        Task<Dictionary<string, double>> RunSimulation(
            T modelState, 
            V simulationConfiguration, 
            Dictionary<string, double> inputData);
    } 
}