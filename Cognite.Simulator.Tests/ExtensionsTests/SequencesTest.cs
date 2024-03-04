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

            var model = new SimulatorModelInfo
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
    }
}
