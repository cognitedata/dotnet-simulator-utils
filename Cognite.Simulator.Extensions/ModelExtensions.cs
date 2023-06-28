using Cognite.Extensions;
using Cognite.Extractor.Common;
using CogniteSdk;
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
            var dmSpace = "SimulatorIntegrationSpace"; // TODO: this will be in CDF space
            var dmSimulatorIntegrationExternalId = "SimulatorIntegration";
            var dmSimulatorExternalId = "Simulator";
            var heartbeat = new RawPropertyValue<long>() {
                Value = DateTime.UtcNow.ToUnixTimeMilliseconds()
            };
            var initData = new StandardInstanceWriteData() {
                { "simulator", new DirectRelationIdentifier() {
                    ExternalId = update.Simulator,
                    Space = dmSpace
                }},
                { "dataSetId", new RawPropertyValue<long?>() {
                    Value = update.DataSetId,
                }},
                { "connectorVersion", new RawPropertyValue<string>() {
                    Value = update.ConnectorVersion
                }},
                { "heartbeat", heartbeat },
                { "simulatorVersion", new RawPropertyValue<string>() {
                    Value = update.SimulatorVersion,
                }},
                // { "simulatorApiEnabled", new RawPropertyValue<bool>() {
                //     Value = update.SimulatorApiEnabled
                // }},
            };
            var heartbeatOnly = new StandardInstanceWriteData() {
                { "heartbeat", heartbeat }
            };
            var simIntegrationProps = init ? initData : heartbeatOnly;
            var instances = new List<BaseInstanceWrite>() {
                new NodeWrite() {
                    ExternalId = update.ConnectorName,
                    Space = dmSpace,
                    Sources = new List<InstanceData>() {
                        new InstanceData<StandardInstanceWriteData>
                        {
                            Source = new ContainerIdentifier() {
                                ExternalId = dmSimulatorIntegrationExternalId,
                                Space = dmSpace
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
                    Space = dmSpace,
                    Sources = new List<InstanceData>() {
                        new InstanceData<StandardInstanceWriteData>
                        {
                            Source = new ContainerIdentifier() {
                                ExternalId = dmSimulatorExternalId,
                                Space = dmSpace
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
