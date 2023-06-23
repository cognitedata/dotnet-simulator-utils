using Cognite.Simulator.Extensions;
using CogniteSdk;
using CogniteSdk.Beta.DataModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Cognite.Simulator.Tests.ExtensionsTests
{
    public class DataModelsTest
    {
        [Fact]
        public async Task TestGetOrCreateSimulatorIntegration()
        {
            const string connectorName = "integration-tests-connector";
            const long dataSetId = CdfTestClient.TestDataset;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var dataModels = cdf.Beta.DataModels;
            var simulators = new List<SimulatorIntegration>
                {
                    new SimulatorIntegration
                    {
                        Simulator = "PROSPER", // Assumes this one exists in CDF
                        DataSetId = dataSetId,
                        ConnectorName = connectorName,
                    },
                    new SimulatorIntegration
                    {
                        Simulator = "SomeSimulator", // This one should be created
                        DataSetId = dataSetId,
                        ConnectorName = connectorName,
                    }
                };

            var simintId = "SimInt";
            try
            {
                await dataModels.GetOrCreateSimulatorIntegrations(
                    simulators,
                    CancellationToken.None).ConfigureAwait(false);

                var conts = await dataModels.ListContainers(
                    new ContainersQuery
                    {
                        Space = simintId
                    }, CancellationToken.None).ConfigureAwait(false);
                var vs = await dataModels.ListViews(
                    new ViewQuery
                    {
                        AllVersions = true,
                        Space = simintId
                    }, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                // Delete everything
                //var insts = await dataModels.DeleteInstances(
                //    new List<InstanceIdentifier>
                //    {
                //        new InstanceIdentifier(InstanceType.node, simintId, "PROSPER"),
                //        new InstanceIdentifier(InstanceType.node, simintId, "SomeSimulator"),
                //        new InstanceIdentifier(InstanceType.node, simintId, "PROSPER_integration-tests-connector"),
                //        new InstanceIdentifier(InstanceType.node, simintId, "SomeSimulator_integration-tests-connector")
                //    }, CancellationToken.None).ConfigureAwait(false);
                //var result2 = await dataModels.DeleteViews(
                //    new[]
                //    {
                //        new FDMExternalId($"Simulator", simintId, BaseMetadata.DataModelVersionValue.Replace('.', '_')),
                //        new FDMExternalId($"{SimulatorIntegrationMetadata.DataType}", simintId, BaseMetadata.DataModelVersionValue.Replace('.', '_')),
                //    }, CancellationToken.None).ConfigureAwait(false);
                //var result = await dataModels.DeleteContainers(
                //    new[]
                //    {
                //        new ContainerId($"Simulator", simintId),
                //        new ContainerId($"{SimulatorIntegrationMetadata.DataType}", simintId)
                //    }, CancellationToken.None).ConfigureAwait(false);

            }
        }
    }
}
