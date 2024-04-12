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
            const long dataSetId = CdfTestClient.TestDataset;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var timeSeries = cdf.TimeSeries;

            var model = new SimulatorModelInfo
            {
                Name = "Connector Test Model",
                ExternalId = "Connector_Test_Model",
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
                var result = await timeSeries.GetOrCreateBoundaryConditions(
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
                Assert.Equal(bc1.Key, bc1Ts.Metadata["referenceId"]);
                Assert.True(bc1Ts.IsStep);
                Assert.False(bc1Ts.IsString);
                var bc2Ts = result.Where(ts => ts.ExternalId == bc2Id).First();
                Assert.Equal(bc2.DataSetId, bc2Ts.DataSetId);
                Assert.Equal(bc2.Unit, bc2Ts.Unit);
                Assert.Equal(bc2.Name, bc2Ts.Metadata["variableName"]);
                Assert.Equal(bc2.Key, bc2Ts.Metadata["referenceId"]);
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

        [Fact]
        public async Task TestCreateSimulationTimeSeries()
        {
            const long dataSetId = CdfTestClient.TestDataset;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var timeSeries = cdf.TimeSeries;

            var model = new SimulatorModelInfo
            {
                Name = "Connector Test Model",
                ExternalId = "Connector_Test_Model",
                Simulator = "TestSimulator"
            };
            var routineRevisionInfo = new SimulatorRoutineRevisionInfo
            {
                Model = model,
                ExternalId = "TestCalc - 1",
                RoutineExternalId = "TestCalc",
            };

            var inA = new SimulationInput
            {
                RoutineRevisionInfo = routineRevisionInfo,
                Name = "Input A",
                ReferenceId = "THP",
                Unit = "BARg",
                Metadata = new Dictionary<string, string>
                    {
                        { "sourceAddress", "TEST.IN.A" }
                    },
                SaveTimeseriesExternalId = "TestSimulator-Input_A-TestCalc-THP"
            };
            var inB = new SimulationInput
            {
                RoutineRevisionInfo = routineRevisionInfo,
                Name = "Input B",
                ReferenceId = "THT",
                Unit = "DegC",
                Metadata = new Dictionary<string, string>
                    {
                        { "sourceAddress", "TEST.IN.B" }
                    },
                SaveTimeseriesExternalId = "TestSimulator-Input_B-TestCalc-CTM" 
            };

            var inputs = new List<SimulationInput>
            {
                inA,
                inB
            };

            var outA = new SimulationOutput
            {
                RoutineRevisionInfo = routineRevisionInfo,
                Name = "Output A",
                ReferenceId = "GasRate",
                Unit = "MMscf/day",
                Metadata = new Dictionary<string, string>
                    {
                        { "sourceAddress", "TEST.OUT.A" }
                    },
                SaveTimeseriesExternalId = "TestSimulator-Output_A-TestCalc-GasRate"
            };
            var outputs = new List<SimulationOutput> { 
                outA
            };

            var tsToDelete = new List<Identity>
            {
                new Identity(inA.SaveTimeseriesExternalId),
                new Identity(inB.SaveTimeseriesExternalId),
                new Identity(outA.SaveTimeseriesExternalId)
            };
            try
            {
                // Test input time series
                var inputTs = await timeSeries.GetOrCreateSimulationInputs(
                    inputs,
                    dataSetId,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.True(inputTs.Any());
                Assert.Equal(2, inputTs.Count());
                var inTsA = inputTs.First(ts => ts.ExternalId == inA.SaveTimeseriesExternalId);
                Assert.Equal(inA.Unit, inTsA.Unit);
                Assert.Equal(inA.Name, inTsA.Metadata["variableName"]);
                Assert.Equal(inA.ReferenceId, inTsA.Metadata["referenceId"]);
                Assert.Equal(inA.Metadata["sourceAddress"], inTsA.Metadata["sourceAddress"]);
                Assert.False(inTsA.IsStep);
                Assert.False(inTsA.IsString);
                var inTsB = inputTs.First(ts => ts.ExternalId == inB.SaveTimeseriesExternalId);
                Assert.Equal(inB.Unit, inTsB.Unit);
                Assert.Equal(inB.Name, inTsB.Metadata["variableName"]);
                Assert.Equal(inB.ReferenceId, inTsB.Metadata["referenceId"]);
                Assert.Equal(inB.Metadata["sourceAddress"], inTsB.Metadata["sourceAddress"]);
                Assert.False(inTsB.IsStep);
                Assert.False(inTsB.IsString);

                // Test output time series
                var outputTs = await timeSeries.GetOrCreateSimulationOutputs(
                    outputs,
                    dataSetId,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.Single(outputTs);
                var outTsA = outputTs.First(ts => ts.ExternalId == outA.SaveTimeseriesExternalId);
                Assert.Equal(outA.Unit, outTsA.Unit);
                Assert.Equal(outA.Name, outTsA.Metadata["variableName"]);
                Assert.Equal(outA.ReferenceId, outTsA.Metadata["referenceId"]);
                Assert.Equal(outA.Metadata["sourceAddress"], outTsA.Metadata["sourceAddress"]);
                Assert.False(outTsA.IsStep);
                Assert.False(outTsA.IsString);

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
