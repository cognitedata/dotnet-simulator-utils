using CogniteSdk;
using Cognite.Simulator.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Xunit;
using Cognite.Extractor.Common;
using CogniteSdk.Beta.DataModels;
using System.Collections.Generic;

namespace Cognite.Simulator.Tests.ExtensionsTests
{
    public class DataModelsTest
    {
        [Fact]
        [Trait("Category", "DataModels")]
        public async Task TestUpdateSimulatorIntegrationsHeartbeat()
        {
            const string connectorName = "integration-tests-connector";
            const string simulatorName = "TestHeartbeatSimulator";
            const long dataSetId = CdfTestClient.TestDataset;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var dataModels = cdf.Beta.DataModels;

            string? externalIdToDelete = null;
            try
            {
                var now = DateTime.UtcNow.ToUnixTimeMilliseconds();

                // Update the model instance with connector heartbeat
                await dataModels.UpdateSimulatorIntegrationsHeartbeat(
                    true,
                    new SimulatorIntegrationUpdate
                    {
                        Simulator = simulatorName,
                        DataSetId = dataSetId,
                        ConnectorName = connectorName,
                        ConnectorVersion = "1.0.0",
                        SimulatorVersion = "1.2.3",
                    },
                    CancellationToken.None).ConfigureAwait(false);

                // Verify that the model instance was updated correctly
                var ids = new[] {
                    new InstanceIdentifier(InstanceType.node, SimulatorIntegrationDms.Space, connectorName)
                };
                var result = await dataModels.RetrieveInstances<StandardInstanceData>(new InstancesRetrieve
                {
                    Sources = new[]
                    {
                        new InstanceSource
                        {
                            Source = new ViewIdentifier(
                                SimulatorIntegrationDms.Space,
                                SimulatorIntegrationDms.SimulatorIntegrationContainer,
                                SimulatorIntegrationDms.ViewVersion
                            ),
                        }
                    },
                    Items = ids,
                    IncludeTyping = true
                }, CancellationToken.None).ConfigureAwait(false);

                Assert.Equal(result.Items.Count(), 1);
                var instance = result.Items.First();
                var instanceViewAndVersion = SimulatorIntegrationDms.SimulatorIntegrationContainer + "/" + SimulatorIntegrationDms.ViewVersion;
                var instanceData = result.Items.First().Properties[SimulatorIntegrationDms.Space][instanceViewAndVersion];
                externalIdToDelete = instance.ExternalId;

                var datasetIdRes = instanceData["dataSetId"] as RawPropertyValue<double>;
                var heartbeatRes = instanceData["heartbeat"] as RawPropertyValue<double>;
                var apiEnabledRes = instanceData["apiEnabled"] as RawPropertyValue<bool>;
                var connectorVersionRes = instanceData["connectorVersion"] as RawPropertyValue<string>;
                var simulatorVersionRes = instanceData["simulatorVersion"] as RawPropertyValue<string>;

                Assert.Equal(connectorName, instance.ExternalId);
                Assert.Equal(dataSetId, datasetIdRes?.Value);
                Assert.False(apiEnabledRes?.Value);
                Assert.True(heartbeatRes?.Value >= now);
                Assert.Equal("1.0.0", connectorVersionRes?.Value);
                Assert.Equal("1.2.3", simulatorVersionRes?.Value);
            }
            finally
            {
                // Cleanup created data model instances
                if (externalIdToDelete != null)
                {
                    await dataModels.DeleteInstances(new List<InstanceIdentifier> {
                        new InstanceIdentifier(InstanceType.node, SimulatorIntegrationDms.Space, externalIdToDelete),
                        new InstanceIdentifier(InstanceType.node, SimulatorIntegrationDms.Space, simulatorName)
                    }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }
}
