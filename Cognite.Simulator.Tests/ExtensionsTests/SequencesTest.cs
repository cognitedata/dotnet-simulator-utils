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
                new SimulatorModel
                {
                    Simulator = "PROSPER",
                    Name = "Connector Test Model",
                },
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
            const long dataSetId = CdfTestClient.TestDataset;
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
            const long dataSetId = CdfTestClient.TestDataset;
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
            const long dataSetId = CdfTestClient.TestDataset;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var sequences = cdf.Sequences;

            var doubleValues = new List<double>() { 1.0, 2.0, 3.0, 4.0 };
            var stringValues = new List<string>() { "A", "B", "C", "D" };

            var model = new SimulatorModel
            {
                Simulator = "TestSimulator",
                Name = "Connector Test Model"
            };
            var calculation = new SimulatorCalculation
            {
                Model = model,
                Name = "Test Simulation Calculation",
                Type = "UserDefined",
                UserDefinedType = "Test/Calc"
            };

            var results = new SimulationTabularResults
            {
                Calculation = calculation,
                Name = "System Results",
                Type = "SystemResults",
                Columns =  new Dictionary<string, SimulationResultColumn>()
                {
                    { 
                        "NumericColumn",
                        new SimulationNumericResultColumn
                        {
                            Metadata = new Dictionary<string, string>()
                            {
                                { "key", "value" }
                            },
                            Rows = doubleValues
                        }
                    },
                    {
                        "StringColumn",
                         new SimulationStringResultColumn
                         {
                             Metadata = new Dictionary<string, string>()
                             {
                                 { "key", "value" }
                             },
                             Rows = stringValues
                         }
                    }
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

                Assert.Equal(SimulatorDataType.SimulationOutput.MetadataValue(), createdSeq.Metadata[BaseMetadata.DataTypeKey]);
                Assert.Equal(calculation.Type, createdSeq.Metadata[CalculationMetadata.TypeKey]);
                Assert.Equal(calculation.Name, createdSeq.Metadata[CalculationMetadata.NameKey]);
                Assert.Equal(calculation.UserDefinedType, createdSeq.Metadata[CalculationMetadata.UserDefinedTypeKey]);
                Assert.Equal(results.Name, createdSeq.Metadata[CalculationMetadata.ResultNameKey]);
                Assert.Equal(results.Type, createdSeq.Metadata[CalculationMetadata.ResultTypeKey]);

                Assert.Equal(2, createdSeq.Columns.Count());
                Assert.Contains(createdSeq.Columns, c => c.ExternalId == results.Columns.ToArray()[0].Key);
                Assert.Contains(createdSeq.Columns, c => c.ExternalId == results.Columns.ToArray()[1].Key);
                // Verify that the sequence was updated correctly
                var result = await sequences.ListRowsAsync(new SequenceRowQuery
                {
                    ExternalId = createdSeq.ExternalId
                }, CancellationToken.None).ConfigureAwait(false);
                Assert.NotEmpty(result.Columns);
                Assert.Contains(result.Columns, c => c.ExternalId == results.Columns.ToArray()[0].Key);
                Assert.Contains(result.Columns, c => c.ExternalId == results.Columns.ToArray()[1].Key);
                var rows = result.Rows.ToArray();
                for (int i = 0; i < rows.Length; ++i)
                {
                    var values = rows[i].Values.ToArray();
                    Assert.Equal(i, rows[i].RowNumber);
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

        [Fact]
        public async Task TestStoreRunConfiguration()
        {
            const long dataSetId = CdfTestClient.TestDataset;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var sequences = cdf.Sequences;

            var runConfiguration = new Dictionary<string, string>() {
                { "runEventId", "SomeExternalId" },
                { "calTime", "1224257770000" },
                { "modelVersion", "3" },
                { "logicalCheckEnabled", "False" }
            };

            var model = new SimulatorModel
            {
                Simulator = "TestSimulator",
                Name = "Connector Test Model"
            };
            var calculation = new SimulatorCalculation
            {
                Model = model,
                Name = "Test Simulation Calculation",
                Type = "UserDefined",
                UserDefinedType = "Test/Calc"
            };

            string? externalIdToDelete = null;
            try
            {
                var createdSeq = await sequences.StoreRunConfiguration(
                    null,
                    0,
                    dataSetId,
                    calculation,
                    runConfiguration,
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
                
                Assert.Equal(SimulatorDataType.SimulationRunConfiguration.MetadataValue(), createdSeq.Metadata[BaseMetadata.DataTypeKey]);
                Assert.Equal(calculation.Type, createdSeq.Metadata[CalculationMetadata.TypeKey]);
                Assert.Equal(calculation.Name, createdSeq.Metadata[CalculationMetadata.NameKey]);
                Assert.Equal(calculation.UserDefinedType, createdSeq.Metadata[CalculationMetadata.UserDefinedTypeKey]);
                
                Assert.Equal(2, createdSeq.Columns.Count());
                
                // Verify that the sequence was updated correctly
                var result = await sequences.ListRowsAsync(new SequenceRowQuery
                {
                    ExternalId = createdSeq.ExternalId
                }, CancellationToken.None).ConfigureAwait(false);
                Assert.NotEmpty(result.Columns);
                var rows = result.Rows.ToArray();
                for (int i = 0; i < rows.Length; ++i)
                {
                    var values = rows[i].Values.ToArray();
                    Assert.Equal(i, rows[i].RowNumber);
                    Assert.True(values[0] is MultiValue.String);
                    Assert.Equal(runConfiguration.Keys.ToArray()[i], ((MultiValue.String)values[0]).Value);
                    Assert.True(values[1] is MultiValue.String);
                    Assert.Equal(runConfiguration.Values.ToArray()[i], ((MultiValue.String)values[1]).Value);
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
