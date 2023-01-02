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
    public abstract class ConnectorBase
    {
        protected CogniteDestination Cdf { get; }
        protected IList<SimulatorConfig> Simulators { get; }

        private readonly Dictionary<string, string> _simulatorSequenceIds;
        private readonly ILogger<ConnectorBase> _logger;

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

        public abstract Task Init(CancellationToken token);
        public abstract Task Run(CancellationToken token);
        public abstract string GetConnectorName();
        public abstract string GetConnectorVersion();
        public abstract TimeSpan GetHeartbeatInterval();

        /// <summary>
        /// For each simulator specified in the configuration, create a sequence in CDF containing the
        /// simulator name and connector name as meta-data. The sequence will have key-value pairs as
        /// rows. The keys are: heartbeat, data set id and connector version. The rows will be updated
        /// periodically by the connector, and indicate the status of the currently running connector to
        /// applications consuming this simulation integration.
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
        protected async Task UpdateIntegrationRows(bool init, CancellationToken token)
        {
            var sequences = Cdf.CogniteClient.Sequences;
            var simulators = Simulators.ToDictionary(
                s => _simulatorSequenceIds[s.Name],
                s => s.DataSetId);
            try
            {
                await sequences.UpdateSimulatorIntegrationsHeartbeat(
                    init,
                    GetConnectorVersion(),
                    simulators,
                    token).ConfigureAwait(false);
            }
            catch (SimulatorIntegrationSequenceException e)
            {
                throw new ConnectorException(e.Message, e.CogniteErrors);
            }
        }

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

    }
    public class ConnectorException : Exception
    {
        public IEnumerable<Cognite.Extensions.CogniteError> Errors;

        public ConnectorException()
        {
            Errors = new List<Cognite.Extensions.CogniteError> { };
        }

        public ConnectorException(string message) : base(message)
        {
            Errors = new List<Cognite.Extensions.CogniteError> { };
        }

        public ConnectorException(string message, IEnumerable<Cognite.Extensions.CogniteError> errors)
            : base(message)
        {
            Errors = errors;
        }

        public ConnectorException(string message, Exception innerException) : base(message, innerException)
        {
            Errors = new List<Cognite.Extensions.CogniteError> { };
        }
    }
}
