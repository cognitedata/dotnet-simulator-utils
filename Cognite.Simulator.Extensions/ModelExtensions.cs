using Cognite.Extractor.Common;
using CogniteSdk.Beta.DataModels;
using CogniteSdk.Resources.Beta;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Class containing extensions to the CDF DataModel instances with utility methods
    /// for simulator integrations
    /// </summary>
    public static class ModelInstancesExtensions
    {
        /// <summary>
        /// Update the simulator integration data model with the connector heartbeat (last time seen)
        /// </summary>
        /// <param name="models">CDF data models</param>
        /// <param name="init">If init, upsert all properties, otherwise only heartbeat</param>
        /// <param name="update">Data to be updated</param>
        /// <param name="token">Cancellation token</param>
        public static async Task UpdateSimulatorIntegrationsHeartbeat(
            this DataModelsResource models,
            bool init,
            SimulatorIntegrationUpdate update,
            CancellationToken token)
        {
            var heartbeat = new RawPropertyValue<long>() {
                Value = DateTime.UtcNow.ToUnixTimeMilliseconds()
            };
            var initData = new StandardInstanceWriteData() {
                { "simulator", new DirectRelationIdentifier() {
                    ExternalId = update.Simulator,
                    Space = SimulatorIntegrationDms.Space
                }},
                { SimulatorIntegrationSequenceRows.DataSetId, new RawPropertyValue<long?>() {
                    Value = update.DataSetId,
                }},
                { SimulatorIntegrationSequenceRows.ConnectorVersion, new RawPropertyValue<string>() {
                    Value = update.ConnectorVersion
                }},
                { SimulatorIntegrationSequenceRows.Heartbeat, heartbeat },
                { SimulatorIntegrationSequenceRows.SimulatorVersion, new RawPropertyValue<string>() {
                    Value = update.SimulatorVersion,
                }},
                { SimulatorIntegrationSequenceRows.SimulatorsApiEnabled, new RawPropertyValue<bool>() {
                    Value = update.SimulatorApiEnabled
                }},
            };
            var heartbeatOnly = new StandardInstanceWriteData() {
                { SimulatorIntegrationSequenceRows.Heartbeat, heartbeat }
            };
            var simIntegrationProps = init ? initData : heartbeatOnly;
            var instances = new List<BaseInstanceWrite>() {
                new NodeWrite() {
                    ExternalId = update.ConnectorName,
                    Space = SimulatorIntegrationDms.Space,
                    Sources = new List<InstanceData>() {
                        new InstanceData<StandardInstanceWriteData>
                        {
                            Source = new ContainerIdentifier() {
                                ExternalId = SimulatorIntegrationDms.SimulatorIntegrationContainer,
                                Space = SimulatorIntegrationDms.Space
                            },
                            Properties = simIntegrationProps
                        }
                    },
                }
            };
            if (init) {
                var simulatorProps = new StandardInstanceWriteData() {
                    { "name", new RawPropertyValue<string>() {
                        Value = update.Simulator
                    }}
                };
                instances.Add(new NodeWrite() {
                    ExternalId = update.Simulator,
                    Space = SimulatorIntegrationDms.Space,
                    Sources = new List<InstanceData>() {
                        new InstanceData<StandardInstanceWriteData>
                        {
                            Source = new ContainerIdentifier() {
                                ExternalId = SimulatorIntegrationDms.SimulatorContainer,
                                Space = SimulatorIntegrationDms.Space
                            },
                            Properties = simulatorProps
                        }
                    }
                });
            }
            await models.UpsertInstances(
                new InstanceWriteRequest() {
                    Items = instances
                }, token
            ).ConfigureAwait(false);
        }
    }
}
