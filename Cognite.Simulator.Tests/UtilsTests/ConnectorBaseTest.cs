using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils;
using CogniteSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public class ConnectorBaseTest
    {
        [Fact]
        public async Task TestConnectorBase()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddTransient<TestConnector>();
            using var provider = services.BuildServiceProvider();
            using var source = new CancellationTokenSource();

            var connector = provider.GetRequiredService<TestConnector>();
            var cdf = provider.GetRequiredService<Client>();

            string? externalIdToDelete = null;
            try
            {
                var timestamp = DateTime.UtcNow.ToUnixTimeMilliseconds();
                await connector
                    .Init(source.Token)
                    .ConfigureAwait(false);

                externalIdToDelete = connector.GetSimulatorIntegartionExternalId("TestSim");
                Assert.NotNull(externalIdToDelete);

                var rowQuery = new SequenceRowQuery
                {
                    ExternalId = externalIdToDelete,
                };

                var rowsResult = await cdf.Sequences.ListRowsAsync(
                    rowQuery,
                    source.Token).ConfigureAwait(false);
                Assert.NotNull(rowsResult);
                Assert.NotEmpty(rowsResult.Rows);

                IDictionary<string, string> resultDict = rowsResult.Rows.ToDictionary(
                    r => r.GetStringValues()[0], r => r.GetStringValues()[1]);
                var heartbeat = Assert.Contains(SimulatorIntegrationSequenceRows.Heartbeat, resultDict);
                var connVersion = Assert.Contains(SimulatorIntegrationSequenceRows.ConnectorVersion, resultDict);
                var simDataset = Assert.Contains(SimulatorIntegrationSequenceRows.DataSetId, resultDict);
                Assert.True(long.Parse(heartbeat) > timestamp);
                Assert.Equal(connector.GetConnectorVersion(), connVersion);
                Assert.Equal(CdfTestClient.TestDataset, long.Parse(simDataset));

                // Start the connector loop and cancel it after 5 seconds. Should be enough time
                // to report a heartbeat back to CDF at least once.
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5));
                await connector.Run(linkedToken).ConfigureAwait(false);

                rowsResult = await cdf.Sequences.ListRowsAsync(
                    rowQuery,
                    source.Token).ConfigureAwait(false);

                resultDict = rowsResult.Rows.ToDictionary(
                    r => r.GetStringValues()[0], r => r.GetStringValues()[1]);
                var lastHeartbeat = Assert.Contains(SimulatorIntegrationSequenceRows.Heartbeat, resultDict);
                Assert.True(long.Parse(lastHeartbeat) > long.Parse(heartbeat));
            }
            finally
            {
                if (externalIdToDelete != null)
                {
                    await cdf.Sequences
                        .DeleteAsync(new List<string> { externalIdToDelete }, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Implements a simple mock connector that only reports
    /// status back to CDF (Heartbeat)
    /// </summary>
    internal class TestConnector : ConnectorBase
    {
        public TestConnector(
            CogniteDestination cdf, 
            ILogger<ConnectorBase> logger) : 
            base(
                cdf,
                new List<SimulatorConfig>
                {
                    new SimulatorConfig
                    {
                        Name = "TestSim",
                        DataSetId = CdfTestClient.TestDataset
                    }
                },
                logger)
        {
        }

        public override string GetConnectorName()
        {
            return "Test Connector";
        }

        public override string GetConnectorVersion()
        {
            return "v0.0.1";
        }

        public override TimeSpan GetHeartbeatInterval()
        {
            return TimeSpan.FromSeconds(2);
        }

        public override async Task Init(CancellationToken token)
        {
            await EnsureSimulatorIntegrationsSequencesExists(token).ConfigureAwait(false);
            await UpdateIntegrationRows(true, token).ConfigureAwait(false);
        }

        public override async Task Run(CancellationToken token)
        {
            try
            {
                await Heartbeat(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Heartbeat will run until the token is canceled. Exit without throwing in this case
                return;
            }
        }
    }
}