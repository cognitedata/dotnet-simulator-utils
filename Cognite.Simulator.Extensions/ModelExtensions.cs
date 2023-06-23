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
        /// <param name="update">Data to be updated</param>
        /// <param name="token">Cancellation token</param>
        public static async Task UpdateSimulatorIntegrationsHeartbeat(
            this DataModelsResource models,
            SimulatorIntegrationUpdate update,
            CancellationToken token)
        {
            await models.UpsertInstances(new InstanceWriteRequest() {
                    Items = new List<BaseInstanceWrite>() {
                        new NodeWrite() {
                            ExternalId = update.ConnectorName,
                            Space = "SimulatorSpace", // TODO: this will be in CDF space
                            Sources = new List<InstanceData>() {
                                new InstanceData<StandardInstanceWriteData>
                                {
                                    Source = new ContainerIdentifier() {
                                        ExternalId = "SimulatorIntegration",
                                        Space = "SimulatorSpace"
                                    },
                                    Properties = new StandardInstanceWriteData
                                    {
                                        { "simulator", new DirectRelationIdentifier() {
                                            ExternalId = update.Simulator,
                                            Space = "SimulatorSpace"
                                        }},
                                        { "dataSetId", new RawPropertyValue<long?>() {
                                            Value = update.DataSetId,
                                        }},
                                        { "connectorVersion", new RawPropertyValue<string>() {
                                            Value = update.ConnectorVersion
                                        }},
                                        { "heartbeat", new RawPropertyValue<long>() {
                                            Value = DateTime.UtcNow.ToUnixTimeMilliseconds()
                                        }},
                                        { "simulatorVersion", new RawPropertyValue<string>() {
                                            Value = update.SimulatorVersion,
                                        }},
                                        // { "simulatorApiEnabled", new RawPropertyValue<bool>() {
                                        //     Value = update.SimulatorApiEnabled
                                        // }},
                                    },
                                }
                            },
                        }
                }}, token).ConfigureAwait(false);
        }
    }
}
