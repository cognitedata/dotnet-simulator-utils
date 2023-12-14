using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognite.Extractor.Common;
using CogniteSdk;
using CogniteSdk.Alpha;

namespace Cognite.Simulator.Tests {
    public class TestHelpers {
        
        public static async Task SimulateProsperRunningAsync(Client cdf, string connectorName = "scheduler-test-connector" ) {
            var integrations = await cdf.Alpha.Simulators.ListSimulatorIntegrationsAsync(
                new SimulatorIntegrationQuery
                {
                }
            ).ConfigureAwait(false);
            var existing = integrations.Items.FirstOrDefault(i => i.ExternalId == connectorName && i.SimulatorExternalId == "PROSPER");
            if (existing == null) {
                await cdf.Alpha.Simulators.CreateSimulatorIntegrationAsync(
                    new List<SimulatorIntegrationCreate>
                    {
                        new SimulatorIntegrationCreate
                        {
                            ExternalId = connectorName,
                            SimulatorExternalId = "PROSPER",
                            DataSetId = CdfTestClient.TestDataset,
                            Heartbeat = DateTime.UtcNow.ToUnixTimeMilliseconds(),
                            ConnectorVersion = "N/A",
                            SimulatorVersion = "N/A",
                            RunApiEnabled = true,
                        }
                    }
                ).ConfigureAwait(false);
            } else {
                await cdf.Alpha.Simulators.UpdateSimulatorIntegrationAsync(
                    new List<SimulatorIntegrationUpdateItem>
                    {
                        new SimulatorIntegrationUpdateItem(existing.Id)
                        {
                            Update = new SimulatorIntegrationUpdate
                            {
                                Heartbeat = new Update<long>(DateTime.UtcNow.ToUnixTimeMilliseconds())
                            }
                        }
                    }
                ).ConfigureAwait(false);
            }
        }
    }
}
