using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Simulator.Extensions;

using CogniteSdk;
using CogniteSdk.Alpha;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cognite.Simulator.Tests.ExtensionsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class SimulatorsTest
    {
        [Fact]
        public async Task TestUpsertSimulators()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var simulator = SeedData.SimulatorCreate;

            simulator.ModelDependencies = [
                new SimulatorModelDependency()
                {
                    FileExtensionTypes = ["xml"],
                    Fields = [new SimulatorModelDependencyField()
                    {
                        Name = "input1",
                        Info = "input1 information",
                        Label = "Input1"
                    }]
                }
            ];

            try
            {
                var res = await cdf.Alpha.Simulators.UpsertAsync(simulator, CancellationToken.None);
                var res2 = await cdf.Alpha.Simulators.UpsertAsync(simulator, CancellationToken.None); // Upsert again to check idempotency

                // Assert created resource
                Assert.Equal(simulator.ExternalId, res.ExternalId);
                Assert.Equal(simulator.Name, res.Name);

                Assert.Equal(simulator.FileExtensionTypes, res.FileExtensionTypes);
                Assert.Equal(simulator.ModelTypes.Select(mt => mt.ToString()), res.ModelTypes.Select(mt => mt.ToString()));
                Assert.Equal(simulator.ModelDependencies.Select(md => md.ToString()), res.ModelDependencies.Select(md => md.ToString()));
                Assert.Equal(simulator.StepFields.Select(sf => sf.ToString()), res.StepFields.Select(sf => sf.ToString()));
                Assert.Equal(simulator.UnitQuantities.Select(uq => uq.ToString()), res.UnitQuantities.Select(uq => uq.ToString()));

                // Assert idempotency
                Assert.True(res2.LastUpdatedTime > res.LastUpdatedTime); // Last updated time should be updated
                res.LastUpdatedTime = res2.LastUpdatedTime = 0; // Ignore created time in comparison
                Assert.Equal(res.ToString(), res2.ToString());
            }
            finally
            {
                // Cleanup created resources
                await SeedData.DeleteSimulator(cdf, simulator.ExternalId);
            }
        }
    }
}
