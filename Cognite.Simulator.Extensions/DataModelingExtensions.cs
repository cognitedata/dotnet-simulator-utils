using Cognite.Extractor.Common;
using CogniteSdk.Beta.DataModels;
using CogniteSdk.Resources.Beta;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Extensions
{
    public static class DataModelsExtensions
    {
        public static async Task GetOrCreateSimulatorIntegrations(
            this DataModelsResource dataModels,
            IEnumerable<SimulatorIntegration> integrations,
            CancellationToken token)
        {
            var simintId = "SimInt";
            await dataModels.UpsertSpaces(
                new List<SpaceCreate>
                {
                    new SpaceCreate
                    {
                        Space = simintId,
                        Name = "Simulator Integration Data Models"
                    }
                }, token).ConfigureAwait(false);

            // Create containers
            var simContainer = new ContainerCreate
            {
                ExternalId = "Simulator",
                Space = simintId,
                Name = "Simulator Container",
                UsedFor = UsedFor.all,
                Properties = new Dictionary<string, ContainerPropertyDefinition>
                {
                    { "name", new ContainerPropertyDefinition
                    {
                        Type = BasePropertyType.Text(),
                        Nullable = true,
                    } },
                    { "description", new ContainerPropertyDefinition
                    {
                        Type = BasePropertyType.Text(),
                        Nullable = true,
                    } }
                }
            };
            var intContainer = new ContainerCreate
            {
                ExternalId = SimulatorIntegrationMetadata.DataType.ToString(),
                Space = simintId,
                Name = $"{SimulatorIntegrationMetadata.DataType.MetadataValue()} Container",
                UsedFor = UsedFor.all,
                Properties = new Dictionary<string, ContainerPropertyDefinition>
                {
                    { SimulatorIntegrationSequenceRows.Heartbeat, new ContainerPropertyDefinition
                    {
                        Type = BasePropertyType.Create(PropertyTypeVariant.int64),
                        Nullable = true,
                    } },
                    { BaseMetadata.SimulatorKey, new ContainerPropertyDefinition
                    {
                        Type = BasePropertyType.Create(PropertyTypeVariant.direct),
                        Nullable = true,
                    } }
                }
            };

            var simContainerId = new ContainerIdentifier(simintId, simContainer.ExternalId);
            var intContainerId = new ContainerIdentifier(simintId, intContainer.ExternalId);

            var containers = await dataModels.UpsertContainers(
                new List<ContainerCreate>
                {
                    simContainer, 
                    intContainer
                }, token).ConfigureAwait(false);

            // Create views

            var simView = new ViewCreate
            {
                ExternalId = $"Simulator",
                Name = "Simulator",
                Filter = new MatchAllFilter(),
                Space = simintId,
                Version = BaseMetadata.DataModelVersionValue.Replace('.', '_'),
                Properties = new Dictionary<string, ICreateViewProperty>
                {
                    { "name", new ViewPropertyCreate
                    {
                        Container = simContainerId,
                        ContainerPropertyIdentifier = "name",
                        Name = "Simulator Name",
                        
                    } },
                    { "description", new ViewPropertyCreate
                    {
                        Container = simContainerId,
                        ContainerPropertyIdentifier = "description",
                        Name = "Simulator Description",
                    } }
                }
            };

            var intView = new ViewCreate
            {
                ExternalId = SimulatorIntegrationMetadata.DataType.ToString(),
                Name = SimulatorIntegrationMetadata.DataType.MetadataValue(),
                Filter = new MatchAllFilter(),
                Space = simintId,
                Version = BaseMetadata.DataModelVersionValue.Replace('.', '_'),
                Properties = new Dictionary<string, ICreateViewProperty>
                {
                    { SimulatorIntegrationSequenceRows.Heartbeat, new ViewPropertyCreate
                    {
                        Container = intContainerId,
                        ContainerPropertyIdentifier = SimulatorIntegrationSequenceRows.Heartbeat,
                        Name = "Connector Heartbeat",
                    } },
                    { BaseMetadata.SimulatorKey, new ViewPropertyCreate
                    {
                        Container = intContainerId,
                        ContainerPropertyIdentifier = BaseMetadata.SimulatorKey,
                        Name = "Simulator",
                        Source = new ViewIdentifier(simintId, simView.ExternalId, simView.Version)
                    } }
                }
            };

            var views = await dataModels.UpsertViews(
                new List<ViewCreate> {
                    simView,
                    intView
                }, token).ConfigureAwait(false);

            // Create data models
            var dms = await dataModels.UpsertDataModels(
                new List<DataModelCreate>
                {
                    new DataModelCreate
                    {
                        Space = simintId,
                        ExternalId = $"{simintId}_{SimulatorIntegrationMetadata.DataType}_Model",
                        Name = SimulatorIntegrationMetadata.DataType.MetadataValue(),
                        Version = BaseMetadata.DataModelVersionValue.Replace('.', '_'),
                        Views = new List<IViewCreateOrReference>
                        {
                            new ViewIdentifier(simintId, simView.ExternalId, simView.Version),
                            new ViewIdentifier(simintId, intView.ExternalId, intView.Version)
                        }
                    }
                }, token).ConfigureAwait(false);

            var items = new List<NodeWrite>();
            var nodes = new InstanceWriteRequest
            {
                Items = items
            };

            foreach (var integration in integrations)
            {
                items.Add(
                    new NodeWrite
                    {
                        ExternalId = integration.Simulator,
                        Space = simintId,
                        Sources = new[]
                        {
                            new InstanceData<StandardInstanceWriteData>
                            {
                                Properties = new StandardInstanceWriteData
                                {
                                    { "name", new RawPropertyValue<string>(integration.Simulator) },
                                    { "description", new RawPropertyValue<string>($"{integration.Simulator} Simulator") }
                                },
                                Source = simContainerId
                            }
                        }
                    });
            }

            var created = await dataModels
                .UpsertInstances(nodes, token)
                .ConfigureAwait(false);

            items.Clear();
            foreach (var integration in integrations)
            {
                items.Add(
                    new NodeWrite
                    {
                        ExternalId = $"{integration.Simulator}_{integration.ConnectorName}",
                        Space = simintId,
                        Sources = new[]
                        {
                            new InstanceData<StandardInstanceWriteData>
                            {
                                
                                Properties = new StandardInstanceWriteData
                                {
                                    { SimulatorIntegrationSequenceRows.Heartbeat, new RawPropertyValue<long>(DateTime.UtcNow.ToUnixTimeMilliseconds()) },
                                    { BaseMetadata.SimulatorKey, new DirectRelationIdentifier(simintId, integration.Simulator) }
                                },
                                Source = intContainerId
                            }
                        }
                    });
            }
            created = await dataModels
                .UpsertInstances(nodes, token)
                .ConfigureAwait(false);

        }
    }
}
