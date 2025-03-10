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
    public abstract class ConnectorBase<T> where T : BaseConfig
    {
        /// <summary>
        /// CDF client wrapper
        /// </summary>
        protected CogniteDestination Cdf { get; }

        /// <summary>
        /// Simulator definition, containing the simulator name, supported units, etc.
        /// </summary>
        protected SimulatorCreate SimulatorDefinition { get; }

        /// <summary>
        /// Simulator integration resource in CDF
        /// </summary>
        public SimulatorIntegration RemoteSimulatorIntegration { get; private set; }
        private readonly ILogger<ConnectorBase<T>> _logger;
        private readonly ConnectorConfig _config;

        private long LastLicenseCheckTimestamp { get; set; }
        private LicenseStatus LastLicenseCheckResult { get; set; }
        private const int FIFTEEN_MIN = 9000;
        private const int ONE_HOUR = 3600;

        private readonly RemoteConfigManager<T> _remoteConfigManager;
        private readonly ScopedRemoteApiSink _remoteApiSink;

        /// <summary>
        /// Initialize the connector with the given parameters
        /// </summary>
        /// <param name="cdf">CDF client wrapper</param>
        /// <param name="config">Connector configuration</param>
        /// <param name="simulatorDefinition">Simulator definition</param>
        /// <param name="logger">Logger</param>
        /// <param name="remoteConfigManager"></param>
        /// <param name="remoteSink">Remote API sink for the logger</param>
        public ConnectorBase(
            CogniteDestination cdf,
            ConnectorConfig config,
            SimulatorCreate simulatorDefinition,
            ILogger<ConnectorBase<T>> logger,
            RemoteConfigManager<T> remoteConfigManager,
            ScopedRemoteApiSink remoteSink)
        {
            Cdf = cdf;
            SimulatorDefinition = simulatorDefinition;
            _logger = logger;
            _remoteApiSink = remoteSink;
            _config = config;
            _remoteConfigManager = remoteConfigManager;
        }

        /// <summary>
        /// Initialize the connector. Should include any initialization tasks to be performed before the connector loop.
        /// This should include a call to
        /// <see cref="InitRemoteSimulatorIntegration(CancellationToken)"/>
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
        public abstract string GetConnectorVersion(CancellationToken token);

        /// <summary>
        /// Returns the version of the given simulator. The connector reads the version and
        /// report it back to CDF
        /// </summary>
        /// <param name="simulator">Name of the simulator</param>
        /// <returns>Version</returns>
        public abstract string GetSimulatorVersion(string simulator, CancellationToken token);

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
        /// Create a simulator integration in CDF containing the
        /// simulator name, connector name, data set id, connector version, etc. These parameters will be updated
        /// periodically by the connector, and indicate the status of the currently running connector to
        /// applications consuming this simulation integration data.
        /// </summary>
        protected async Task InitRemoteSimulatorIntegration(CancellationToken token)
        {
            var simulatorsApi = Cdf.CogniteClient.Alpha.Simulators;
            try
            {
                var integrationRes = await simulatorsApi.ListSimulatorIntegrationsAsync(
                    new SimulatorIntegrationQuery(),
                    token).ConfigureAwait(false);
                var integrations = integrationRes.Items;
                var connectorName = GetConnectorName();
                var simulatorExternalId = SimulatorDefinition.ExternalId;

                var existing = integrations.FirstOrDefault(i => i.ExternalId == connectorName && i.SimulatorExternalId == simulatorExternalId);
                if (existing == null)
                {
                    _logger.LogInformation("Creating new simulator integration for {Simulator}", SimulatorDefinition.Name);

                    var integrationToCreate = new SimulatorIntegrationCreate
                    {
                        ExternalId = connectorName,
                        SimulatorExternalId = simulatorExternalId,
                        DataSetId = _config.DataSetId,
                        ConnectorVersion = GetConnectorVersion(token) ?? "N/A",
                        SimulatorVersion = GetSimulatorVersion(simulatorExternalId, token) ?? "N/A",
                    };

                    var res = await simulatorsApi.CreateSimulatorIntegrationAsync(new List<SimulatorIntegrationCreate> {
                        integrationToCreate
                    }, token).ConfigureAwait(false);
                    RemoteSimulatorIntegration = res.First();
                }
                else
                {
                    _logger.LogInformation("Found existing simulator integration for {Simulator}", simulatorExternalId);
                    RemoteSimulatorIntegration = existing;
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
        protected async Task UpdateRemoteSimulatorIntegration(
            bool init,
            CancellationToken token)
        {
            if (init)
            {
                LastLicenseCheckResult = ShouldLicenseCheck() ? LicenseStatus.UNKNOWN : LicenseStatus.DISABLED;
            }
            var simulatorsApi = Cdf.CogniteClient.Alpha.Simulators;
            var simulatorExternalId = SimulatorDefinition.ExternalId;
            try
            {
                var integrationUpdate = init ? new SimulatorIntegrationUpdate
                {
                    DataSetId = new Update<long> { Set = _config.DataSetId },
                    ConnectorVersion = new Update<string> { Set = GetConnectorVersion(token) ?? "N/A" },
                    SimulatorVersion = new Update<string> { Set = GetSimulatorVersion(simulatorExternalId, token) ?? "N/A" },
                    ConnectorStatus = new Update<string> { Set = ConnectorStatus.IDLE.ToString() },
                    ConnectorStatusUpdatedTime = new Update<long> { Set = DateTime.UtcNow.ToUnixTimeMilliseconds() },
                    Heartbeat = new Update<long> { Set = DateTime.UtcNow.ToUnixTimeMilliseconds() },
                    LicenseLastCheckedTime = new Update<long> { Set = LastLicenseCheckTimestamp },
                    LicenseStatus = new Update<string> { Set = LastLicenseCheckResult.ToString() },
                } : new SimulatorIntegrationUpdate
                {
                    Heartbeat = new Update<long> { Set = DateTime.UtcNow.ToUnixTimeMilliseconds() },
                    LicenseLastCheckedTime = new Update<long> { Set = LastLicenseCheckTimestamp },
                    LicenseStatus = new Update<string> { Set = LastLicenseCheckResult.ToString() },
                };
                if (RemoteSimulatorIntegration == null)
                {
                    _logger.LogWarning("Simulator integration for {Simulator} not found", simulatorExternalId);
                    throw new ConnectorException($"Simulator integration for {simulatorExternalId} not found");
                }
                var integrationUpdateItem = new UpdateItem<SimulatorIntegrationUpdate>(RemoteSimulatorIntegration.Id)
                {
                    Update = integrationUpdate,
                };
                await simulatorsApi.UpdateSimulatorIntegrationAsync(
                    new[] { integrationUpdateItem },
                    token).ConfigureAwait(false);
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
                await UpdateRemoteSimulatorIntegration(false, token)
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
            return LicenseStatus.AVAILABLE;
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
                LastLicenseCheckResult = GetLicenseStatus();
                await UpdateRemoteSimulatorIntegration(false, token)
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
