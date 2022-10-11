using CogniteSdk;
using Cognite.Simulator.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Xunit;
using Cognite.Extractor.Common;

namespace Cognite.Simulator.Tests.ExtensionsTests
{
    public class SequencesTest
    {
        [Fact]
        public async Task TestFindModelBoundaryConditions()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var sequences = cdf.Sequences;

            // Assumes this resource exists in the CDF test project
            var rows = await sequences.FindModelBoundaryConditions(
                "PROSPER",
                "Connector Test Model",
                CancellationToken.None).ConfigureAwait(false);
            Assert.NotEmpty(rows.Columns);
            Assert.Contains(rows.Columns, c => c.ExternalId == BoundaryConditionsSequenceColumns.Id);
            Assert.Contains(rows.Columns, c => c.ExternalId == BoundaryConditionsSequenceColumns.TimeSeries);
            Assert.Contains(rows.Columns, c => c.ExternalId == BoundaryConditionsSequenceColumns.Name);
            Assert.Contains(rows.Columns, c => c.ExternalId == BoundaryConditionsSequenceColumns.Address);
            Assert.NotEmpty(rows.Rows);
            Assert.Equal(4, rows.Rows.First().Values.Count());
        }

        [Fact]
        public async Task TestGetOrCreateSimulatorIntegration()
        {
            const string connectorName = "integration-tests-connector";
            const long dataSetId = 7900866844615420;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var sequences = cdf.Sequences;
            var simulators = new Dictionary<string, long>
                {
                    { "PROSPER", dataSetId }, // Assumes this one exists in CDF
                    { "SomeSimulator", dataSetId } // This one should be created
                };

            string? externalIdToDelete = null;
            try
            {
                var integrations = await sequences.GetOrCreateSimulatorIntegrations(
                    connectorName,
                    simulators,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.NotEmpty(integrations);
                foreach (var sim in simulators)
                {
                    var seq = Assert.Single(integrations, i =>
                        i.DataSetId == sim.Value &&
                        i.Metadata[BaseMetadata.DataTypeKey] == SimulatorIntegrationMetadata.DataType.MetadataValue() &&
                        i.Metadata[BaseMetadata.SimulatorKey] == sim.Key &&
                        i.Metadata[SimulatorIntegrationMetadata.ConnectorNameKey] == connectorName);

                    Assert.Equal(2, seq.Columns.Count());
                    if (sim.Key == "SomeSimulator")
                    {
                        externalIdToDelete = seq.ExternalId;
                    }
                }
            }
            finally
            {
                // Cleanup created resources
                if (externalIdToDelete != null)
                {
                    await sequences.DeleteAsync(new List<string> { externalIdToDelete }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        [Fact]
        public async Task TestUpdateSimulatorIntegrationsHeartbeat()
        {
            const string connectorName = "integration-tests-connector";
            const long dataSetId = 7900866844615420;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var sequences = cdf.Sequences;
            var simulators = new Dictionary<string, long>
                {
                    { "TestHeartbeatSimulator", dataSetId },
                };

            string? externalIdToDelete = null;
            try
            {
                var integrations = await sequences.GetOrCreateSimulatorIntegrations(
                    connectorName,
                    simulators,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.NotEmpty(integrations);
                externalIdToDelete = integrations.First().ExternalId;
                var integrationsMap = integrations.ToDictionary(
                    i =>  i.ExternalId,
                    i => dataSetId);
                
                var now = DateTime.UtcNow.ToUnixTimeMilliseconds();
                await sequences.UpdateSimulatorIntegrationsHeartbeat(
                    true,
                    "1.0.0",
                    integrationsMap,
                    CancellationToken.None).ConfigureAwait(false);

                var result = await sequences.ListRowsAsync(new SequenceRowQuery
                {
                    ExternalId = externalIdToDelete
                }, CancellationToken.None).ConfigureAwait(false);
                Assert.NotEmpty(result.Columns);
                Assert.Contains(result.Columns, c => c.ExternalId == KeyValuePairSequenceColumns.Key);
                Assert.Contains(result.Columns, c => c.ExternalId == KeyValuePairSequenceColumns.Value);

                foreach(var row in result.Rows)
                {
                    var values = row.GetStringValues();
                    bool isHeartbeat = values[0] == SimulatorIntegrationSequenceRows.Heartbeat;
                    bool isDataSetId = values[0] == SimulatorIntegrationSequenceRows.DataSetId;
                    bool isConnectorVersion = values[0] == SimulatorIntegrationSequenceRows.ConnectorVersion;
                    Assert.True(isHeartbeat || isDataSetId || isConnectorVersion);
                    if (isHeartbeat)
                    {
                        Assert.True(long.TryParse(values[1], out long heartbeat) && heartbeat >= now);
                    }
                    if (isConnectorVersion)
                    {
                        Assert.Equal("1.0.0", values[1]);
                    }
                    if (isDataSetId)
                    {
                        Assert.Equal(dataSetId.ToString(), values[1]);
                    }
                }
            }
            finally
            {
                // Cleanup created resources
                if (externalIdToDelete != null)
                {
                    await sequences.DeleteAsync(new List<string> { externalIdToDelete }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }
}
