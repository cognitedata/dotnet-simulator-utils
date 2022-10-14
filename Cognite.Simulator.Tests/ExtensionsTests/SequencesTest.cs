﻿using CogniteSdk;
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
                // Create a test simulator integration sequence
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
                
                // Update the sequence with connector heartbeat
                await sequences.UpdateSimulatorIntegrationsHeartbeat(
                    true,
                    "1.0.0",
                    integrationsMap,
                    CancellationToken.None).ConfigureAwait(false);

                // Verify that the sequence was updated correctly
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

        [Fact]
        public async Task TestStoreSimulationResults()
        {
            const long dataSetId = 7900866844615420;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var sequences = cdf.Sequences;

            var doubleValues = new List<double>() { 1.0, 2.0, 3.0, 4.0 };
            var stringValues = new List<string>() { "A", "B", "C", "D" };

            var results = new SimulationTabularResults
            {
                CalculationName = "Test Simulation Calculation",
                CalculationType = "UserDefined",
                CalculationTypeUserDefined = "Test/Calc",
                ModelName = "Connector Test Model",
                ResultName = "System Results",
                ResultType = "SystemResults",
                Simulator = "TestSimulator",
                Columns =  new List<SimulationResultColumn>()
                {
                     new SimulationNumericResultColumn
                     {
                         Header = "NumericColumn",
                         Metadata = new Dictionary<string, string>()
                         {
                             { "key", "value" }
                         },
                         Rows = doubleValues
                     },
                     new SimulationStringResultColumn
                     {
                         Header = "StringColumn",
                         Metadata = new Dictionary<string, string>()
                         {
                             { "key", "value" }
                         },
                         Rows = stringValues
                     },
                }
            };

            string? externalIdToDelete = null;
            try
            {
                var createdSeq = await sequences.StoreSimulationResults(
                    null,
                    0,
                    dataSetId,
                    results,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.NotNull(createdSeq);
                externalIdToDelete = createdSeq.ExternalId;
                Assert.Contains(BaseMetadata.SimulatorKey, createdSeq.Metadata.Keys);
                Assert.Contains(BaseMetadata.DataModelVersionKey, createdSeq.Metadata.Keys);
                Assert.Contains(BaseMetadata.DataTypeKey, createdSeq.Metadata.Keys);
                Assert.Contains(ModelMetadata.NameKey, createdSeq.Metadata.Keys);
                Assert.Contains(CalculationMetadata.TypeKey, createdSeq.Metadata.Keys);
                Assert.Contains(CalculationMetadata.NameKey, createdSeq.Metadata.Keys);
                Assert.Contains(CalculationMetadata.UserDefinedTypeKey, createdSeq.Metadata.Keys);
                Assert.Contains(CalculationMetadata.ResultNameKey, createdSeq.Metadata.Keys);
                Assert.Contains(CalculationMetadata.ResultTypeKey, createdSeq.Metadata.Keys);

                Assert.Equal(results.CalculationType, createdSeq.Metadata[CalculationMetadata.TypeKey]);
                Assert.Equal(results.CalculationName, createdSeq.Metadata[CalculationMetadata.NameKey]);
                Assert.Equal(results.CalculationTypeUserDefined, createdSeq.Metadata[CalculationMetadata.UserDefinedTypeKey]);
                Assert.Equal(results.ResultName, createdSeq.Metadata[CalculationMetadata.ResultNameKey]);
                Assert.Equal(results.ResultType, createdSeq.Metadata[CalculationMetadata.ResultTypeKey]);

                Assert.Equal(2, createdSeq.Columns.Count());
                Assert.Contains(createdSeq.Columns, c => c.ExternalId == results.Columns[0].Header);
                Assert.Contains(createdSeq.Columns, c => c.ExternalId == results.Columns[1].Header);
                // Verify that the sequence was updated correctly
                var result = await sequences.ListRowsAsync(new SequenceRowQuery
                {
                    ExternalId = createdSeq.ExternalId
                }, CancellationToken.None).ConfigureAwait(false);
                Assert.NotEmpty(result.Columns);
                Assert.Contains(result.Columns, c => c.ExternalId == results.Columns[0].Header);
                Assert.Contains(result.Columns, c => c.ExternalId == results.Columns[1].Header);
                var rows = result.Rows.ToArray();
                for (int i = 0; i < result.Rows.Count(); ++i)
                {
                    var values = rows[i].Values.ToArray();
                    Assert.True(values[0] is MultiValue.Double);
                    Assert.Equal(doubleValues[i], ((MultiValue.Double)values[0]).Value);
                    Assert.True(values[1] is MultiValue.String);
                    Assert.Equal(stringValues[i], ((MultiValue.String)values[1]).Value);
                }
            }
            finally
            {
                if (externalIdToDelete != null)
                {
                    await sequences.DeleteAsync(new List<string> { externalIdToDelete }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }
}
