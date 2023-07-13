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

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Base class for simulator connectors. Implements heartbeat reporting.
    /// The connector information is saved as a CDF sequence, where the rows
    /// are key/value pairs (see <seealso cref="SimulatorIntegrationSequenceRows"/>)
    /// </summary>
    public abstract class ConnectorBase
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

        private readonly Dictionary<string, string> _simulatorSequenceIds;
        private readonly ILogger<ConnectorBase> _logger;
        private readonly ConnectorConfig _config;

        private string LastLicenseCheckTimestamp { get; set; } = "";

        /// <summary>
        /// Initialize the connector with the given parameters
        /// </summary>
        /// <param name="cdf">CDF client wrapper</param>
        /// <param name="config">Connector configuration</param>
        /// <param name="simulators">List of simulator configurations</param>
        /// <param name="logger">Logger</param>
        public ConnectorBase(
            CogniteDestination cdf,
            ConnectorConfig config,
            IList<SimulatorConfig> simulators,
            ILogger<ConnectorBase> logger)
        {
            Cdf = cdf;
            Simulators = simulators;
            Config = config;
            _simulatorSequenceIds = new Dictionary<string, string>();
            _logger = logger;
            _config = config;
        }

        /// <summary>
        /// Returns the external ID of the sequence in CDF that contains information 
        /// about the simulator integration, if any
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <returns>External ID, or null if not found</returns>
        public string GetSimulatorIntegartionExternalId(string simulator)
        {
            if (!_simulatorSequenceIds.ContainsKey(simulator))
            {
                return null;
            }
            return _simulatorSequenceIds[simulator];
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
            int min3600 = _config.LicenseCheck.Frequency < 3600 ? 3600 : _config.LicenseCheck.Frequency;
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
        /// For each simulator specified in the configuration, create a sequence in CDF containing the
        /// simulator name and connector name as meta-data. The sequence will have key-value pairs as
        /// rows. The keys are: heartbeat, data set id and sconnector version. The rows will be updated
        /// periodically by the connector, and indicate the status of the currently running connector to
        /// applications consuming this simulation integration data.
        /// </summary>
        protected async Task EnsureSimulatorIntegrationsSequencesExists(CancellationToken token)
        {
            var sequences = Cdf.CogniteClient.Sequences;
            var simulatorsDict = Simulators.Select(
                s => new SimulatorIntegration
                {
                    Simulator = s.Name,
                    DataSetId = s.DataSetId,
                    ConnectorName = GetConnectorName()
                });
            try
            {
                var integrations = await sequences.GetOrCreateSimulatorIntegrations(
                    simulatorsDict,
                    token).ConfigureAwait(false);
                foreach (var integration in integrations)
                {
                    _simulatorSequenceIds.Add(
                        integration.Metadata[BaseMetadata.SimulatorKey],
                        integration.ExternalId);
                }
            }
            catch (SimulatorIntegrationSequenceException e)
            {
                throw new ConnectorException(e.Message, e.CogniteErrors);
            }
        }

        /// <summary>
        /// Update the heartbeat, data set id and connector version in CDF. Data set id and connector
        /// version are not expected to change while the connector is running, and are only set during
        /// initialization
        /// </summary>
        protected async Task UpdateIntegrationRows(
            bool init,
            CancellationToken token,
            bool licenseCheck = false)
        {
            if (licenseCheck)
            {
                LastLicenseCheckTimestamp = $"{DateTime.UtcNow.ToUnixTimeMilliseconds()}";
            }
            var sequences = Cdf.CogniteClient.Sequences;
            try
            {
                foreach (var simulator in Simulators)
                {
                    var update = init ?
                    new SimulatorIntegrationUpdate
                    {
                        Simulator = simulator.Name,
                        DataSetId = simulator.DataSetId,
                        ConnectorName = GetConnectorName(),
                        ConnectorVersion = GetConnectorVersion(),
                        SimulatorVersion = GetSimulatorVersion(simulator.Name),
                        ExtraInformation = GetExtraInformation(simulator.Name),
                        SimulatorApiEnabled = ApiEnabled(),
                        // Maybe add LicenseEnabled here, so that it will display if the sequence should have license timestamp
                    }
                    : null;
                    await sequences.UpdateSimulatorIntegrationsData(
                        _simulatorSequenceIds[simulator.Name],
                        init,
                        update,
                        token,
                        updateLicense: licenseCheck,
                        LastLicenseCheckTimestamp: LastLicenseCheckTimestamp).ConfigureAwait(false);
                }
            }
            catch (SimulatorIntegrationSequenceException e)
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
        public virtual bool CheckLicenseStatus() // make this abstract?
        {
            return true;
        }
        /// <summary>
        /// 
        /// </summary>
        public async Task LicenseCheck(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task
                    .Delay(GetLicenseCheckInterval(), token)
                    .ConfigureAwait(false);
                _logger.LogDebug("Updating connector license timestamp");
                if (CheckLicenseStatus() is true)
                {
                    await UpdateIntegrationRows(false, token, true)
                        .ConfigureAwait(false);
                }
            }
        }
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
