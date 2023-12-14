using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cognite.Extractor.Configuration;
using CogniteSdk.Alpha;
using CogniteSdk;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Base class for simulator connectors. Implements heartbeat reporting.
    /// The connector information is saved as a CDF sequence, where the rows
    /// are key/value pairs (see <seealso cref="SimulatorIntegrationSequenceRows"/>)
    /// </summary>
    public abstract class ConnectorBase<T> where T : BaseConfig
    {
        /// <summary>
        /// CDF client wrapper
        /// </summary>
        protected CogniteDestination Cdf { get; }
        
        /// <summary>
        /// List of simulator configurations handled by this connector
        /// </summary>
        protected IList<SimulatorConfig> Simulators { get; }
        private ConnectorConfig Config { get; }

        private readonly Dictionary<string, long> _simulatorIntegrationIds;
        private readonly ILogger<ConnectorBase<T>> _logger;
        private readonly ConnectorConfig _config;

        private long LastLicenseCheckTimestamp { get; set; }
        private string LastLicenseCheckResult { get; set; }
        private const int FIFTEEN_MIN = 9000;

        private readonly RemoteConfigManager<T> _remoteConfigManager;

        /// <summary>
        /// Initialize the connector with the given parameters
        /// </summary>
        /// <param name="cdf">CDF client wrapper</param>
        /// <param name="config">Connector configuration</param>
        /// <param name="simulators">List of simulator configurations</param>
        /// <param name="logger">Logger</param>
        /// <param name="remoteConfigManager"></param>
        public ConnectorBase(
            CogniteDestination cdf,
            ConnectorConfig config,
            IList<SimulatorConfig> simulators,
            ILogger<ConnectorBase<T>> logger,
            RemoteConfigManager<T> remoteConfigManager)
        {
            Cdf = cdf;
            Simulators = simulators;
            Config = config;
            _simulatorIntegrationIds = new Dictionary<string, long>();
            _logger = logger;
            _config = config;
            _remoteConfigManager = remoteConfigManager;
        }

        /// <summary>
        /// Returns the ID of the simulator integration resource in CDF, if any
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <returns>Simulator integration ID, or null if not found</returns>
        public long? GetSimulatorIntegrationId(string simulator)
        {
            if (!_simulatorIntegrationIds.ContainsKey(simulator))
            {
                return null;
            }
            return _simulatorIntegrationIds[simulator];
        }

        /// <summary>
        /// Initialize the connector. Should include any initialization tasks to be performed before the connector loop.
        /// This should include a call to
        /// <see cref="EnsureSimulatorIntegrationsSequencesExists(CancellationToken)"/>
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public abstract Task Init(CancellationToken token);
        
        /// <summary>
        /// Implements the connector loop. Should call the <see cref="Heartbeat(CancellationToken)"/> method and any
        /// other thats that are done periodically by the connector
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
        /// Returns any extra information about the simulator integration. This information
        /// is reported back to CDF
        /// </summary>
        /// <param name="simulator"></param>
        /// <returns></returns>
        public virtual Dictionary<string, string> GetExtraInformation(string simulator)
        {
            return new Dictionary<string, string>();
        }

        /// <summary>
        /// Returns the connector name.This is reported periodically to CDF
        /// </summary>
        /// <returns>Connector name</returns>
        public virtual string GetConnectorName()
        {
            return _config.GetConnectorName();
        }

        /// <summary>
        /// How often to report the connector information back to CDF (Heartbeat)
        /// </summary>
        /// <returns>Time interval</returns>
        public virtual TimeSpan GetHeartbeatInterval()
        {
            return TimeSpan.FromSeconds(_config.StatusInterval);
        }

        /// <summary>
        /// How often to report the simulator license status information back to CDF (Heartbeat)
        /// </summary>
        /// <returns>Time interval</returns>
        public virtual TimeSpan GetLicenseCheckInterval()
        {
            int min3600 = _config.LicenseCheck.Interval < 3600 ? 3600 : _config.LicenseCheck.Interval;
            return TimeSpan.FromSeconds(min3600);
        }
        
        /// <summary>
        /// If the connector should check and report the license status back to CDF
        /// </summary>
        public virtual bool ShouldLicenseCheck()
        {
            return _config.LicenseCheck.Enabled;
        }

        /// <summary>
        /// Indicates if this connectors should use Cognite's Simulator Integration API
        /// </summary>
        /// <returns></returns>
        public virtual bool ApiEnabled()
        {
            return _config.UseSimulatorsApi;
        }

        /// <summary>
        /// For each simulator specified in the configuration, create a simulator integration in CDF containing the
        /// simulator name, connector name, data set id, connector version, etc. These parameters will be updated
        /// periodically by the connector, and indicate the status of the currently running connector to
        /// applications consuming this simulation integration data.
        /// </summary>
        protected async Task EnsureSimulatorIntegrationsSequencesExists(CancellationToken token)
        {
            var simulatorsApi = Cdf.CogniteClient.Alpha.Simulators;
            try
            {
                var integrationRes = await simulatorsApi.ListSimulatorIntegrationsAsync(
                    new SimulatorIntegrationQuery(),
                    token).ConfigureAwait(false);
                var integrations = integrationRes.Items;
                var connectorName = GetConnectorName();
                foreach (var simulator in Simulators)
                {
                    var simulatorName = simulator.Name;
                    var existing = integrations.FirstOrDefault(i => i.ExternalId == connectorName && i.SimulatorExternalId == simulator.Name);
                    if (existing == null)
                    {
                        _logger.LogInformation("Creating new simulator integration for {Simulator}", simulatorName);
                        var integrationToCreate = new SimulatorIntegrationCreate
                        {
                            ExternalId = connectorName,
                            SimulatorExternalId = simulatorName,
                            DataSetId = simulator.DataSetId,
                            ConnectorVersion = GetConnectorVersion(),
                            SimulatorVersion = GetSimulatorVersion(simulatorName),
                            RunApiEnabled = ApiEnabled(),
                        };
                        var res = await simulatorsApi.CreateSimulatorIntegrationAsync(new List<SimulatorIntegrationCreate> {
                            integrationToCreate
                        }, token).ConfigureAwait(false);
                        _simulatorIntegrationIds[simulatorName] = res.First().Id;
                    }
                    else
                    {
                        _logger.LogInformation("Found existing simulator integration for {Simulator}", simulatorName);
                        _simulatorIntegrationIds[simulatorName] = existing.Id;
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
        protected async Task UpdateIntegrationRows(
            bool init,
            CancellationToken token)
        {   
            if (init)
            {
                LastLicenseCheckResult = ShouldLicenseCheck() ? "Not checked yet" : "License check disabled";
            }
            var simulatorsApi = Cdf.CogniteClient.Alpha.Simulators;
            try
            {
                foreach (var simulator in Simulators)
                {
                    var integrationUpdate = init ? new SimulatorIntegrationUpdate
                    {
                        DataSetId = new Update<long> { Set = simulator.DataSetId },
                        ConnectorVersion = new Update<string> { Set = GetConnectorVersion() },
                        SimulatorVersion = new Update<string> { Set = GetSimulatorVersion(simulator.Name) },
                        RunApiEnabled = new Update<bool> { Set = ApiEnabled() },
                        ConnectorStatus = new Update<string> { Set = "IDLE" },
                        ConnectorStatusUpdatedTime = new Update<long> { Set = DateTime.UtcNow.ToUnixTimeMilliseconds() },
                        Heartbeat = new Update<long> { Set = DateTime.UtcNow.ToUnixTimeMilliseconds() },
                        LicenseLastCheckedTime = new Update<long> { Set = LastLicenseCheckTimestamp },
                        LicenseStatus = new Update<string> { Set = LastLicenseCheckResult }, 
                    } : new SimulatorIntegrationUpdate {
                        Heartbeat = new Update<long> { Set = DateTime.UtcNow.ToUnixTimeMilliseconds() },
                        LicenseLastCheckedTime = new Update<long> { Set = LastLicenseCheckTimestamp },
                        LicenseStatus = new Update<string> { Set = LastLicenseCheckResult }, 
                    };
                    var simIntegrationId = GetSimulatorIntegrationId(simulator.Name);
                    if (simIntegrationId == null)
                    {
                        _logger.LogWarning("Simulator integration for {Simulator} not found", simulator.Name);
                        throw new ConnectorException($"Simulator integration for {simulator.Name} not found");
                    }
                    var integrationUpdateItem = new UpdateItem<SimulatorIntegrationUpdate>(GetSimulatorIntegrationId(simulator.Name).Value)
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
        public async Task Heartbeat(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task
                    .Delay(GetHeartbeatInterval(), token)
                    .ConfigureAwait(false);
                _logger.LogDebug("Updating connector heartbeat");
                await UpdateIntegrationRows(false, token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// This method should be overridden in each specific Connector class.
        /// </summary>
        /// <returns>True if the simulator has a valid license, false otherwise.</returns>
        public virtual bool CheckLicenseStatus()
        {
            return true;
        }
        /// <summary>
        /// Task that runs in a loop, reporting the connector license status to CDF periodically
        /// (with the interval defined in <see cref="GetLicenseCheckInterval"/>)
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task LicenseCheck(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task
                    .Delay(GetLicenseCheckInterval(), token)
                    .ConfigureAwait(false);
                _logger.LogDebug("Updating connector license timestamp");
                LastLicenseCheckTimestamp = DateTime.UtcNow.ToUnixTimeMilliseconds();
                LastLicenseCheckResult = CheckLicenseStatus() ? "Available" : "Not available";
                await UpdateIntegrationRows(false, token)
                    .ConfigureAwait(false);
            }
        }
        
        /// <summary>
        /// Task that runs in a loop, checking for new config in extraction pipelines
        /// </summary>
        public async Task CheckRemoteConfig(CancellationToken token)
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
