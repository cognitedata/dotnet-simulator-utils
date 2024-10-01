using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

using System.Threading;
using System.Threading.Tasks;

using CogniteSdk.Alpha;
using CogniteSdk;
using Cognite.Simulator.Utils.Automation;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Connector status
    /// </summary>
    public enum ConnectorStatus
    {
        /// <summary>
        /// Connector is currently idle.
        /// </summary>
        IDLE,

        /// <summary>
        /// Connector is currently running a simulation.
        /// </summary>
        RUNNING_SIMULATION,
    }

    /// <summary>
    /// License status
    /// </summary>
    public enum LicenseStatus 
    {
        /// <summary>
        /// License is available.
        /// </summary>
        AVAILABLE,
        /// <summary>
        /// License is not available.
        /// </summary>
        NOT_AVAILABLE,
        /// <summary>
        /// License status has not been checked yet.
        /// </summary>
        UNKNOWN,
        /// <summary>
        /// License check is disabled.
        /// </summary>
        DISABLED
    }
    /// <summary>
    /// Base class for simulator connectors. Implements heartbeat reporting.
    /// The connector information is stored in the simulator integration resource in CDF.
    /// </summary>
    public abstract class ConnectorBase<T,TAutomationConfig> where T : BaseConfig
            where TAutomationConfig : AutomationConfig, new()

    {
        /// <summary>
        /// CDF client wrapper
        /// </summary>
        protected CogniteDestination Cdf { get; }
        
        /// <summary>
        /// List of simulator configurations handled by this connector
        /// </summary>
        protected IList<SimulatorConfig> Simulators { get; }
        private readonly Dictionary<string, SimulatorIntegration> _simulatorIntegrationsList;
        private readonly ILogger<ConnectorBase<T,TAutomationConfig>> _logger;
        private readonly ConnectorConfig _config;

        private long LastLicenseCheckTimestamp { get; set; }
        private LicenseStatus LastLicenseCheckResult { get; set; }
        private const int FIFTEEN_MIN = 9000;
        private const int ONE_HOUR = 3600;

        private readonly RemoteConfigManager<T> _remoteConfigManager;
        private readonly ScopedRemoteApiSink<TAutomationConfig> _remoteApiSink;

        /// <summary>
        /// Initialize the connector with the given parameters
        /// </summary>
        /// <param name="cdf">CDF client wrapper</param>
        /// <param name="config">Connector configuration</param>
        /// <param name="simulators">List of simulator configurations</param>
        /// <param name="logger">Logger</param>
        /// <param name="remoteConfigManager"></param>
        /// <param name="remoteSink">Remote API sink for the logger</param>
        public ConnectorBase(
            CogniteDestination cdf,
            ConnectorConfig config,
            IList<SimulatorConfig> simulators,
            ILogger<ConnectorBase<T,TAutomationConfig>> logger,
            RemoteConfigManager<T> remoteConfigManager,
            ScopedRemoteApiSink<TAutomationConfig> remoteSink)
        {
            Cdf = cdf;
            Simulators = simulators;
            _simulatorIntegrationsList = new Dictionary<string, SimulatorIntegration>();
            _logger = logger;
            _remoteApiSink = remoteSink;
            _config = config;
            _remoteConfigManager = remoteConfigManager;
        }

        /// <summary>
        /// Returns the ID of the simulator integration resource in CDF, if any
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <returns>Simulator integration ID, or null if not found</returns>
        public SimulatorIntegration GetSimulatorIntegration(string simulator)
        {
            if (!_simulatorIntegrationsList.ContainsKey(simulator))
            {
                return null;
            }
            return _simulatorIntegrationsList[simulator];
        }

        /// <summary>
        /// Returns the first simulator integration.
        /// </summary>
        /// <returns>A list of simulator integrations.</returns>
        public IEnumerable<CogniteSdk.Alpha.SimulatorIntegration> GetSimulatorIntegrations()
        {
            return _simulatorIntegrationsList.Values;
        }


        /// <summary>
        /// Initialize the connector. Should include any initialization tasks to be performed before the connector loop.
        /// This should include a call to
        /// <see cref="InitRemoteSimulatorIntegrations(CancellationToken)"/>
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public abstract Task Init(CancellationToken token);
        
        /// <summary>
        /// Implements the connector loop. Should call the <see cref="HeartbeatLoop(CancellationToken)"/> method and any
        /// other thats that are done periodically by the connector
        /// 
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public abstract Task Run(CancellationToken token);

        /// <summary>
        /// Returns the connector version. This is reported periodically to CDF
        /// </summary>
        /// <returns>Connector version</returns>
        public abstract string GetConnectorVersion();

        /// <summary>
        /// Returns the version of the given simulator. The connector reads the version and
        /// report it back to CDF
        /// </summary>
        /// <param name="simulator">Name of the simulator</param>
        /// <returns>Version</returns>
        public abstract string GetSimulatorVersion(string simulator);

        /// <summary>
        /// Returns the connector name.This is reported periodically to CDF
        /// </summary>
        /// <returns>Connector name</returns>
        public string GetConnectorName()
        {
            return _config.GetConnectorName();
        }

        /// <summary>
        /// How often to report the connector information back to CDF (Heartbeat)
        /// </summary>
        /// <returns>Time interval</returns>
        public TimeSpan GetHeartbeatInterval()
        {
            return TimeSpan.FromSeconds(_config.StatusInterval);
        }

        /// <summary>
        /// How often to report the simulator license status information back to CDF (Heartbeat)
        /// </summary>
        /// <returns>Time interval</returns>
        public TimeSpan GetLicenseCheckInterval()
        {
            int interval = _config.LicenseCheck.Interval < ONE_HOUR ? ONE_HOUR : _config.LicenseCheck.Interval;
            return TimeSpan.FromSeconds(interval);
        }
        
        /// <summary>
        /// If the connector should check and report the license status back to CDF
        /// </summary>
        public bool ShouldLicenseCheck()
        {
            return _config.LicenseCheck.Enabled;
        }

        /// <summary>
        /// For each simulator specified in the configuration, create a simulator integration in CDF containing the
        /// simulator name, connector name, data set id, connector version, etc. These parameters will be updated
        /// periodically by the connector, and indicate the status of the currently running connector to
        /// applications consuming this simulation integration data.
        /// </summary>
        protected async Task InitRemoteSimulatorIntegrations(CancellationToken token)
        {
            var simulatorsApi = Cdf.CogniteClient.Alpha.Simulators;
            try
            {
                var integrationRes = await simulatorsApi.ListSimulatorIntegrationsAsync(
                    new SimulatorIntegrationQuery(),
                    token).ConfigureAwait(false);
                var integrations = integrationRes.Items;
                var connectorName = GetConnectorName();
                for (var index = 0; index < Simulators.Count; index++)
                {
                    var simulator = Simulators[index];

                    // If there are more than one, we add the Simulator name as a prefix
                    if (index > 0)
                    {
                        connectorName = $"{simulator.Name}-{GetConnectorName()}";
                    }
                    
                    var existing = integrations.FirstOrDefault(i => i.ExternalId == connectorName && i.SimulatorExternalId == simulator.Name);
                    if (existing == null)
                    {
                        _logger.LogInformation("Creating new simulator integration for {Simulator}", simulator.Name);
                        var existingSimulators = await Cdf.CogniteClient.Alpha.Simulators.ListAsync(
                            new SimulatorQuery (),
                            token).ConfigureAwait(false);
                        var existingSimulator = existingSimulators.Items.FirstOrDefault(s => s.ExternalId == simulator.Name);

                        if (existingSimulator == null)
                        {
                            _logger.LogWarning("Simulator {Simulator} not found in CDF", simulator.Name);
                            throw new ConnectorException($"Simulator {simulator.Name} not found in CDF");
                        }

                        var integrationToCreate = new SimulatorIntegrationCreate
                        {
                            ExternalId = connectorName,
                            SimulatorExternalId = simulator.Name,
                            DataSetId = simulator.DataSetId,
                            ConnectorVersion = GetConnectorVersion() ?? "N/A",
                            SimulatorVersion = GetSimulatorVersion(simulator.Name) ?? "N/A",
                        };

                        var res = await simulatorsApi.CreateSimulatorIntegrationAsync(new List<SimulatorIntegrationCreate> {
                            integrationToCreate
                        }, token).ConfigureAwait(false);
                        _simulatorIntegrationsList[simulator.Name] = res.First();
                    }
                    else
                    {
                        _logger.LogInformation("Found existing simulator integration for {Simulator}", simulator.Name);
                        _simulatorIntegrationsList[simulator.Name] = existing;
                    }
                }
            }
            catch (CogniteException e)
            {
                throw new ConnectorException(e.Message, e.CogniteErrors);
            }
        }

        /// <summary>
        /// Update the heartbeat, data set id and connector version in CDF. Data set id and connector
        /// version are not expected to change while the connector is running, and are only set during
        /// initialization.
        /// </summary>
        protected async Task UpdateRemoteSimulatorIntegrations(
            bool init,
            CancellationToken token)
        {   
            if (init)
            {
                LastLicenseCheckResult = ShouldLicenseCheck() ? LicenseStatus.UNKNOWN : LicenseStatus.DISABLED;
            }
            var simulatorsApi = Cdf.CogniteClient.Alpha.Simulators;
            try
            {
                foreach (var simulator in Simulators)
                {
                    var integrationUpdate = init ? new SimulatorIntegrationUpdate
                    {
                        DataSetId = new Update<long> { Set = simulator.DataSetId },
                        ConnectorVersion = new Update<string> { Set = GetConnectorVersion() ?? "N/A" },
                        SimulatorVersion = new Update<string> { Set = GetSimulatorVersion(simulator.Name) ?? "N/A" },
                        ConnectorStatus = new Update<string> { Set = ConnectorStatus.IDLE.ToString() },
                        ConnectorStatusUpdatedTime = new Update<long> { Set = DateTime.UtcNow.ToUnixTimeMilliseconds() },
                        Heartbeat = new Update<long> { Set = DateTime.UtcNow.ToUnixTimeMilliseconds() },
                        LicenseLastCheckedTime = new Update<long> { Set = LastLicenseCheckTimestamp },
                        LicenseStatus = new Update<string> { Set = LastLicenseCheckResult.ToString() }, 
                    } : new SimulatorIntegrationUpdate {
                        Heartbeat = new Update<long> { Set = DateTime.UtcNow.ToUnixTimeMilliseconds() },
                        LicenseLastCheckedTime = new Update<long> { Set = LastLicenseCheckTimestamp },
                        LicenseStatus = new Update<string> { Set = LastLicenseCheckResult.ToString() },
                    };
                    var simIntegration = GetSimulatorIntegration(simulator.Name);
                    if (simIntegration == null)
                    {
                        _logger.LogWarning("Simulator integration for {Simulator} not found", simulator.Name);
                        throw new ConnectorException($"Simulator integration for {simulator.Name} not found");
                    }
                    var integrationUpdateItem = new UpdateItem<SimulatorIntegrationUpdate>(simIntegration.Id)
                        {
                            Update = integrationUpdate,
                        };
                    await simulatorsApi.UpdateSimulatorIntegrationAsync(
                        new [] { integrationUpdateItem },
                        token).ConfigureAwait(false);
                }
            }
            catch (CogniteException e)
            {
                throw new ConnectorException(e.Message, e.CogniteErrors);
            }
        }

        /// <summary>
        /// Task that runs in a loop, reporting the connector information to CDF periodically
        /// (with the interval defined in <see cref="GetHeartbeatInterval"/>)
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task HeartbeatLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task
                    .Delay(GetHeartbeatInterval(), token)
                    .ConfigureAwait(false);
                _logger.LogDebug("Updating connector heartbeat");
                await UpdateRemoteSimulatorIntegrations(false, token)
                    .ConfigureAwait(false);
                await _remoteApiSink.Flush(Cdf.CogniteClient.Alpha.Simulators, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// This method should be overridden in each specific Connector class.
        /// </summary>
        /// <returns>True if the simulator has a valid license, false otherwise.</returns>
        public LicenseStatus GetLicenseStatus()
        {
            return  LicenseStatus.AVAILABLE;
        }
        /// <summary>
        /// Task that runs in a loop, reporting the connector license status to CDF periodically
        /// (with the interval defined in <see cref="GetLicenseCheckInterval"/>)
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task LicenseCheckLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task
                    .Delay(GetLicenseCheckInterval(), token)
                    .ConfigureAwait(false);
                _logger.LogDebug("Updating connector license timestamp");
                LastLicenseCheckTimestamp = DateTime.UtcNow.ToUnixTimeMilliseconds();
                LastLicenseCheckResult = GetLicenseStatus() ;
                await UpdateRemoteSimulatorIntegrations(false, token)
                    .ConfigureAwait(false);
            }
        }
        
        /// <summary>
        /// Task that runs in a loop, checking for new config in extraction pipelines
        /// </summary>
        public async Task RestartOnNewRemoteConfigLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task
                    .Delay(FIFTEEN_MIN, token) // Run every 15 minutes
                    .ConfigureAwait(false);
                _logger.LogDebug("Checking remote config updates");
                if (_remoteConfigManager == null) return;
                var newConfig = await _remoteConfigManager.FetchLatest(token).ConfigureAwait(false);
                if (newConfig != null)
                {
                    throw new NewConfigDetected();
                }
            }
        }
    }

    /// <summary>
    /// Exception used to restart connector
    /// </summary>
    public class NewConfigDetected : Exception
    {
    }
    
    /// <summary>
    /// Represents errors related to the connector operation
    /// </summary>
    public class ConnectorException : Exception
    {
        /// <summary>
        /// CDF errors that may have caused this exception
        /// </summary>
        public IEnumerable<Cognite.Extensions.CogniteError> Errors { get; }

        /// <summary>
        /// Creates a new connector exception
        /// </summary>
        public ConnectorException()
        {
            Errors = new List<Cognite.Extensions.CogniteError> { };
        }

        /// <summary>
        /// Creates a new connector exception with the given message
        /// </summary>
        /// <param name="message">Error message</param>
        public ConnectorException(string message) : base(message)
        {
            Errors = new List<Cognite.Extensions.CogniteError> { };
        }

        /// <summary>
        /// Creates a new connector exception with the given message and CDF errors
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="errors">CDF errors</param>
        public ConnectorException(string message, IEnumerable<Cognite.Extensions.CogniteError> errors)
            : base(message)
        {
            Errors = errors;
        }

        /// <summary>
        /// Creates a new connector exception with the given message and inner exception
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="innerException">Inner exception</param>
        public ConnectorException(string message, Exception innerException) : base(message, innerException)
        {
            Errors = new List<Cognite.Extensions.CogniteError> { };
        }
    }
}
