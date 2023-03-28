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

        private readonly Dictionary<string, string> _simulatorSequenceIds;
        private readonly ILogger<ConnectorBase> _logger;

        /// <summary>
        /// Initialize the connector with the given parameters
        /// </summary>
        /// <param name="cdf">CDF client wrapper</param>
        /// <param name="simulators">List of simulator configurations</param>
        /// <param name="logger">Logger</param>
        public ConnectorBase(
            CogniteDestination cdf,
            IList<SimulatorConfig> simulators,
            ILogger<ConnectorBase> logger)
        {
            Cdf = cdf;
            Simulators = simulators;
            _simulatorSequenceIds = new Dictionary<string, string>();
            _logger = logger;
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
        /// Returns the connector name.This is reported periodically to CDF
        /// </summary>
        /// <returns>Connector name</returns>
        public abstract string GetConnectorName();
        
        /// <summary>
        /// Returns the connector version. This is reported periodically to CDF
        /// </summary>
        /// <returns>Connector version</returns>
        public abstract string GetConnectorVersion();
        
        /// <summary>
        /// How often to report the connector information back to CDF (Heartbeat)
        /// </summary>
        /// <returns>Time interval</returns>
        public abstract TimeSpan GetHeartbeatInterval();

        /// <summary>
        /// For each simulator specified in the configuration, create a sequence in CDF containing the
        /// simulator name and connector name as meta-data. The sequence will have key-value pairs as
        /// rows. The keys are: heartbeat, data set id and connector version. The rows will be updated
        /// periodically by the connector, and indicate the status of the currently running connector to
        /// applications consuming this simulation integration data.
        /// </summary>
        protected async Task EnsureSimulatorIntegrationsSequencesExists(CancellationToken token)
        {
            var sequences = Cdf.CogniteClient.Sequences;
            var simulatorsDict = Simulators.ToDictionary(
                s => s.Name,
                s => s.DataSetId);
            try
            {
                var integrations = await sequences.GetOrCreateSimulatorIntegrations(
                    GetConnectorName(),
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
            Dictionary<string, string> extraInformation,
            CancellationToken token)
        {
            var sequences = Cdf.CogniteClient.Sequences;
            try
            {
                foreach (var simulator in Simulators)
                {
                    await sequences.UpdateSimulatorIntegrationsHeartbeat(
                        init,
                        GetConnectorVersion(),
                        _simulatorSequenceIds[simulator.Name],
                        simulator.DataSetId,
                        extraInformation,
                        token).ConfigureAwait(false);
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
                await UpdateIntegrationRows(false, null, token)
                    .ConfigureAwait(false);
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
