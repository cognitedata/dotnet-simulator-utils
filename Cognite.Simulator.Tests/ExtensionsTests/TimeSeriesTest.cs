using CogniteSdk;
using Cognite.Simulator.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using System.Threading;

namespace Cognite.Simulator.Tests.ExtensionsTests
{
    public class TimeSeriesTest
    {
        [Fact]
        public async Task TestCreateBoundaryConditions()
        {
            const long dataSetId = 7900866844615420;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var timeSeries = cdf.TimeSeries;

            var model = new SimulatorModel
            {
                Name = "Connector Test Model",
                Simulator = "TestSimulator"
            };

            var bc1Id = "TestSimulator-BC-BT1-Connector_Test_Model";
            var bc1 = new BoundaryCondition
            {
                DataSetId = dataSetId,
                Key = "BT1",
                Name = "Boundary Condition Test 1",
                Unit = "BARg",
                Model = model
            };

            var bc2Id = "TestSimulator-BC-BT2-Connector_Test_Model";
            var bc2 = new BoundaryCondition
            {
                DataSetId = dataSetId,
                Key = "BT2",
                Name = "Boundary Condition Test 2",
                Unit = "BARg",
                Model = model
            };
            var tsToDelete = new List<Identity>
            {
                new Identity(bc1Id),
                new Identity(bc2Id)
            };
            try
            {
                var result = await timeSeries.CreateBoundaryConditions(
                    new Dictionary<string, BoundaryCondition>
                    {
                        { bc1Id, bc1 },
                        { bc2Id, bc2 }
                    },
                    CancellationToken.None).ConfigureAwait(false);
                Assert.True(result.Any());
                Assert.Equal(2, result.Count());
                var bc1Ts = result.Where(ts => ts.ExternalId == bc1Id).First();
                Assert.Equal(bc1.DataSetId, bc1Ts.DataSetId);
                Assert.Equal(bc1.Unit, bc1Ts.Unit);
                Assert.Equal(bc1.Name, bc1Ts.Metadata["variableName"]);
                Assert.Equal(bc1.Key, bc1Ts.Metadata["variableType"]);
                Assert.True(bc1Ts.IsStep);
                Assert.False(bc1Ts.IsString);
                var bc2Ts = result.Where(ts => ts.ExternalId == bc2Id).First();
                Assert.Equal(bc2.DataSetId, bc2Ts.DataSetId);
                Assert.Equal(bc2.Unit, bc2Ts.Unit);
                Assert.Equal(bc2.Name, bc2Ts.Metadata["variableName"]);
                Assert.Equal(bc2.Key, bc2Ts.Metadata["variableType"]);
                Assert.True(bc2Ts.IsStep);
                Assert.False(bc2Ts.IsString);
            }
            finally
            {
                // Cleanup created resources
                if (tsToDelete.Any())
                {
                    await timeSeries.DeleteAsync(new TimeSeriesDelete
                    {
                        IgnoreUnknownIds = true,
                        Items = tsToDelete,
                    }, CancellationToken.None).ConfigureAwait(false);
                }

            }
        }
    }
}
