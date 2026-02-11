using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk;
using CogniteSdk.Alpha;
using CogniteSdk.Resources.Alpha;

using Microsoft.Extensions.Logging;

using Serilog.Context;

using static Cognite.Simulator.Utils.SimulatorLoggingUtils;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents the connector's simulation runner process. This base class can
    /// fetch simulation runs from CDF that are ready to run, validate them and find
    /// the time range to sample data where the process is in steady state.
    /// </summary>
    /// <typeparam name="A">Type of automation configuration objects</typeparam>
    /// <typeparam name="T">Type of model state objects</typeparam>
    /// <typeparam name="V">Type of simulation configuration objects</typeparam>
    public abstract class SimulationRunnerBase<A, T, V>
        where A : AutomationConfig
        where T : ModelStateBase
        where V : SimulatorRoutineRevision
    {
        private readonly ConnectorConfig _connectorConfig;
        private readonly SimulatorsResource _cdfSimulators;
        private readonly CogniteSdk.Resources.DataPointsResource _cdfDataPoints;
        private readonly ILogger _logger;

        /// <summary>
        /// Library containing the simulator model files
        /// </summary>
        protected IModelProvider<A, T> ModelLibrary { get; }

        /// <summary>
        /// Library containing the simulation configuration files
        /// </summary>
        protected IRoutineProvider<V> RoutineLibrary { get; }

        private long? simulatorIntegrationId;

        private SimulatorCreate _simulatorDefinition;


        /// <summary>
        /// Create a new instance of the runner with the provided parameters
        /// </summary>
        /// <param name="connectorConfig">Connector configuration</param>
        /// <param name="simulatorDefinition">Simulator definition</param>
        /// <param name="cdf">CDF client</param>
        /// <param name="modelLibrary">Model library</param>
        /// <param name="routineLibrary">Configuration library</param>
        /// <param name="logger">Logger</param>
        public SimulationRunnerBase(
            ConnectorConfig connectorConfig,
            SimulatorCreate simulatorDefinition,
            CogniteDestination cdf,
            IModelProvider<A, T> modelLibrary,
            IRoutineProvider<V> routineLibrary,
            ILogger logger)
        {
            if (cdf == null)
            {
                throw new ArgumentNullException(nameof(cdf));
            }
            _connectorConfig = connectorConfig;
            _simulatorDefinition = simulatorDefinition;
            _cdfSimulators = cdf.CogniteClient.Alpha.Simulators;
            _cdfDataPoints = cdf.CogniteClient.DataPoints;
            _logger = logger;
            ModelLibrary = modelLibrary;
            RoutineLibrary = routineLibrary;
        }

        /// <summary>
        /// Updates the status of a simulation run in CDF
        /// </summary>
        /// <param name="runId">Run ID</param>
        /// <param name="status">Run status</param>
        /// <param name="statusMessage">Run status message</param>
        /// <param name="token">Cancellation token</param>
        /// <param name="runConfiguration">Run configuration</param>
        /// <returns>Run ID</returns>
        private async Task<SimulationRun> UpdateSimulationRunStatus(
            long runId,
            SimulationRunStatus status,
            string statusMessage,
            CancellationToken token,
            Dictionary<string, long> runConfiguration = null)
        {

            long? simulationTime = null;
            if (runConfiguration != null && runConfiguration.TryGetValue("simulationTime", out var simTime))
            {
                simulationTime = simTime;
            }

            var message = statusMessage == null || statusMessage.Length < 255 ? statusMessage : statusMessage.Substring(0, 254);

            var res = await _cdfSimulators.SimulationRunCallbackAsync(
                new SimulationRunCallbackItem()
                {
                    Id = runId,
                    Status = status,
                    StatusMessage = message,
                    SimulationTime = simulationTime
                }, token).ConfigureAwait(false);

            return res.Items.First();
        }

        private async Task<IEnumerable<SimulationRun>> FindSimulationRunsWithStatus(
            SimulationRunStatus status,
            CancellationToken token)
        {
            var connectorName = _connectorConfig.GetConnectorName();

            var query = new SimulationRunQuery()
            {
                Filter = new SimulationRunFilter()
                {
                    Status = status,
                    SimulatorExternalIds = [_simulatorDefinition.ExternalId],
                    SimulatorIntegrationExternalIds = [connectorName],
                }
            };
            var runsResult = await _cdfSimulators
                .ListSimulationRunsAsync(query, token)
                .ConfigureAwait(false);

            return runsResult.Items;
        }

        private async Task<IEnumerable<SimulationRunItem>> FindSimulationRuns(
            SimulationRunStatus status,
            CancellationToken token)
        {
            var simulationRuns = await FindSimulationRunsWithStatus(status, token).ConfigureAwait(false);
            return simulationRuns.Select(r => new SimulationRunItem(r)).ToList();
        }

        /// <summary>
        /// Start the loop for fetching and processing simulation events from CDF
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Run(CancellationToken token)
        {
            var interval = TimeSpan.FromSeconds(_connectorConfig.FetchRunsInterval);
            while (!token.IsCancellationRequested)
            {
                // Find runs that are ready to be executed
                var simulationRuns = await FindSimulationRuns(
                    SimulationRunStatus.ready,
                    token).ConfigureAwait(false);
                if (simulationRuns.Any())
                {
                    _logger.LogInformation(
                        "{Number} simulation runs(s) ready to run found in CDF",
                        simulationRuns.Count());
                }

                // Find runs that are in progress. Should not have any, as the connector runs them in sequence.
                // Any running events indicates that the connector went down during the run, and the run status should
                // be updated to "failure".
                var simulationRunningItems = await FindSimulationRuns(
                    SimulationRunStatus.running,
                    token).ConfigureAwait(false);
                if (simulationRunningItems.Any())
                {
                    _logger.LogWarning(
                        "{Number} simulation run(s) that are in progress (but should have finished) found in CDF",
                        simulationRunningItems.Count());
                }
                // Process the "running" events first. Those will be saved as "failed" in CDF
                // and then process the "ready" events in the older-first order.
                var allRunItems = new List<SimulationRunItem>(simulationRunningItems);
                allRunItems.AddRange(simulationRuns);

                foreach (SimulationRunItem runItem in allRunItems)
                {
                    var runId = runItem.Run.Id;
                    var startTime = DateTime.UtcNow;
                    T modelState = null;
                    V routineRev = null;
                    bool skipped = false;

                    var connectorExternalId = _connectorConfig.GetConnectorName();

                    using (LogContext.Push(await GetLogEnrichers(_cdfSimulators, runItem.Run.LogId, true).ConfigureAwait(false)))
                    {
                        try
                        {
                            runItem.ValidateReadinessForExecution(_connectorConfig.SimulationRunTolerance);
                            (modelState, routineRev) = await GetModelAndRoutine(runItem).ConfigureAwait(false);
                            if (routineRev == null || connectorExternalId != routineRev.SimulatorIntegrationExternalId)
                            {
                                _logger.LogError("Skip simulation run that belongs to another connector: {Id} {Connector}",
                                runId,
                                routineRev?.SimulatorIntegrationExternalId);
                                skipped = true;
                                continue;
                            }

                            await PublishConnectorStatusAsync(ConnectorStatus.RUNNING_SIMULATION, token).ConfigureAwait(false);

                            await InitSimulationRun(
                                runItem,
                                startTime,
                                modelState,
                                routineRev,
                                token)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            if (ex is ConnectorException ce && ce.Errors != null)
                            {
                                foreach (var error in ce.Errors)
                                {
                                    _logger.LogError("{Message}", error.Message);
                                }
                            }
                            _logger.LogWarning("Simulation run {id} failed with error: {Message}", runId, ex);
                            runItem.Run = await UpdateSimulationRunStatus(
                                runId,
                                SimulationRunStatus.failure,
                                ex.Message,
                                token,
                                runItem.RunConfiguration
                            ).ConfigureAwait(false);
                        }
                        finally
                        {
                            // the following check was added because the code below was running even for skipped runs
                            if (!skipped)
                            {
                                _logger.LogDebug("Simulation run finished for run {Id}", runId);
                                await PublishConnectorStatusAsync(ConnectorStatus.IDLE, token).ConfigureAwait(false);
                            }
                        }
                    }
                }

                await Task.Delay(interval, token).ConfigureAwait(false);
            }
        }

        private async Task<(T, V)> GetModelAndRoutine(SimulationRunItem simEv)
        {
            string modelRevExternalId = simEv.Run.ModelRevisionExternalId;
            string runId = simEv.Run.Id.ToString();

            var model = await ModelLibrary.GetModelRevision(modelRevExternalId).ConfigureAwait(false);
            if (model == null)
            {
                _logger.LogError("Could not find a local model file to run Simulation run {Id}", runId);
                throw new SimulationException($"Could not find a model file for {modelRevExternalId}");
            }
            V routineRev = await RoutineLibrary.GetRoutineRevision(simEv.Run.RoutineRevisionExternalId).ConfigureAwait(false);

            if (routineRev == null)
            {
                _logger.LogError("Could not find a local configuration to run Simulation run {Id}", runId);
                throw new SimulationException($"Could not find a routine revision for model: {modelRevExternalId} routineRevision: {simEv.Run.RoutineRevisionExternalId}");
            }

            if (_connectorConfig.GetConnectorName() != routineRev.SimulatorIntegrationExternalId)
            {
                return (model, null);
            }

            return (model, routineRev);
        }

        async Task PublishConnectorStatusAsync(ConnectorStatus status, CancellationToken token)
        {
            try
            {
                if (!simulatorIntegrationId.HasValue)
                {
                    var simulatorExternalId = _simulatorDefinition.ExternalId;
                    var integrationRes = await _cdfSimulators.ListSimulatorIntegrationsAsync(
                        new SimulatorIntegrationQuery()
                        {
                            Filter = new SimulatorIntegrationFilter()
                            {
                                SimulatorExternalIds = [simulatorExternalId]
                            }
                        },
                        token).ConfigureAwait(false);
                    var integration = integrationRes.Items.FirstOrDefault(i => i.ExternalId == _connectorConfig.GetConnectorName());
                    if (integration == null)
                    {
                        throw new ConnectorException($"Simulator integration for {simulatorExternalId} not found");
                    }
                    simulatorIntegrationId = integration.Id;
                }
                var now = DateTime.UtcNow.ToUnixTimeMilliseconds();
                var simulatorIntegrationUpdate = new SimulatorIntegrationUpdate
                {
                    ConnectorStatus = new Update<string>(status.ToString()),
                    ConnectorStatusUpdatedTime = new Update<long>(now)
                };
                await _cdfSimulators.UpdateSimulatorIntegrationAsync(
                    new[] {
                        new SimulatorIntegrationUpdateItem(simulatorIntegrationId.Value) {
                            Update = simulatorIntegrationUpdate
                        }
                    },
                    token
                ).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogWarning("Failed to update simulator integration status: {Message}", e.Message);
                throw;
            }
        }

        /// <summary>
        /// Initialize the simulation run execution
        /// </summary>
        /// <param name="runItem">Simulation run item</param>
        /// <param name="startTime">Simulation start time</param>
        /// <param name="modelState">Model state object</param>
        /// <param name="routineRevision">Routine revision object</param>
        /// <param name="token">Cancellation token</param>
        private async Task InitSimulationRun(
            SimulationRunItem runItem,
            DateTime startTime,
            T modelState,
            V routineRevision,
            CancellationToken token)
        {

            if (modelState == null)
            {
                throw new ArgumentNullException(nameof(modelState));
            }
            if (runItem == null)
            {
                throw new ArgumentNullException(nameof(runItem));
            }
            if (routineRevision == null)
            {
                throw new ArgumentNullException(nameof(routineRevision));
            }

            runItem.Run = await UpdateSimulationRunStatus(
                runItem.Run.Id,
                SimulationRunStatus.running,
                null,
                token).ConfigureAwait(false);

            var configObj = routineRevision.Configuration;

            // Determine the validation end time
            // If the run contains a validation end overwrite, use that instead of the current time
            var validationEnd = runItem.Run.RunTime.HasValue ? CogniteTime.FromUnixTimeMilliseconds(runItem.Run.RunTime.Value) : startTime;

            SamplingConfiguration samplingConfiguration = null;
            try
            {
                // check if data sampling is enabled
                if (configObj.DataSampling.Enabled)
                {
                    // Run validation and return a sampling range
                    var samplingRange = await SimulationUtils.RunSteadyStateAndLogicalCheck(
                        _cdfDataPoints,
                        configObj,
                        validationEnd,
                        token).ConfigureAwait(false);

                    // if the sampling range is not null, use it to create the sampling configuration
                    // this should always pass, as the validation check should throw an exception if it fails
                    if (samplingRange.Max.HasValue)
                        samplingConfiguration = new SamplingConfiguration(
                            end: samplingRange.Max.Value,
                            start: samplingRange.Min,
                            samplingPosition: SamplingPosition.Midpoint
                        );
                }
                // if data sampling is not enabled, we do not sample data, but instead use the latest datapoint before
                // validation end and the simulation time becomes also the validation end
                else
                {
                    samplingConfiguration = new SamplingConfiguration(
                        end: validationEnd.ToUnixTimeMilliseconds()
                    );
                }

                _logger.LogInformation("Running routine revision {ExternalId} for model {ModelExternalId}. Simulation time: {Time}",
                    routineRevision.ExternalId,
                    routineRevision.ModelExternalId,
                    CogniteTime.FromUnixTimeMilliseconds(samplingConfiguration?.SimulationTime ?? 0));
            }
            catch (SimulationException ex)
            {
                _logger.LogError("Logical check or steady state detection failed: {Message}", ex.Message);
                throw;
            }
            finally
            {
                if (samplingConfiguration != null)
                    runItem.RunConfiguration.Add("simulationTime", samplingConfiguration.SimulationTime);
            }
            await this.RunRoutine(
                runItem,
                startTime,
                modelState,
                routineRevision,
                samplingConfiguration,
                token).ConfigureAwait(false);

            runItem.Run = await UpdateSimulationRunStatus(
                runItem.Run.Id,
                SimulationRunStatus.success,
                "Simulation ran to completion",
                token,
                runItem.RunConfiguration
            ).ConfigureAwait(false);
        }
        /// <summary>
        /// Run a simulation and saves the results back to CDF. Different simulators
        /// will implement different patterns of interaction when running simulations
        /// </summary>
        /// <param name="runItem">Simulation run item</param>
        /// <param name="startTime">Simulation start time</param>
        /// <param name="modelState">Model state object</param>
        /// <param name="configObj">Configuration object</param>
        /// <param name="samplingConfiguration">Selected simulation sampling samplingConfiguration</param>
        /// <param name="token">Cancellation token</param>
        protected abstract Task RunRoutine(
            SimulationRunItem runItem,
            DateTime startTime,
            T modelState,
            V configObj,
            SamplingConfiguration samplingConfiguration,
            CancellationToken token);

    }

    /// <summary>
    /// Wrapper class for <see cref="SimulationRun"/> entity.
    /// Contains the simulation run configuration as a dictionary of key-value pairs
    /// </summary>
    public class SimulationRunItem
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
        /// Creates a new simulation run item based on simulation run CDF resource
        /// </summary>
        public SimulationRunItem(SimulationRun r)
        {
            Run = r;
        }

        /// <summary>
        /// Validates the simulation run for readiness for execution.
        /// Throws an exception if the simulation run is too old or in an invalid state
        /// </summary>
        /// <param name="maxAgeSeconds">Maximum age of the simulation run in seconds</param>
        public void ValidateReadinessForExecution(int maxAgeSeconds = 3600)
        {

            if (Run.CreatedTime < DateTime.UtcNow.AddSeconds(-1 * maxAgeSeconds).ToUnixTimeMilliseconds())
            {
                throw new ConnectorException($"Simulation has timed out because it is older than {maxAgeSeconds} second(s)");
            }

            if (Run.Status == SimulationRunStatus.running)
            {
                throw new ConnectorException("Simulation entered unrecoverable state and failed");
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