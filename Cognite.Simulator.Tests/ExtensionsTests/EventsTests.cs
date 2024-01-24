using Cognite.Extractor.Common;
using Cognite.Simulator.Extensions;
using CogniteSdk;
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
    public class EventsTests
    {
        [Fact]
        public async Task TestSimulationEvent()
        {
            const long dataSetId = CdfTestClient.TestDataset;
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var events = cdf.Events;
            var fakeCalcId = "TestSimulator-Connector_Test_model-UserDefined-Test_Calc";
            var fakeConnectorName = "test-connector";

            var model = new SimulatorModelInfo
            {
                Simulator = "TestSimulator",
                Name = "Connector Test Model"
            };
            var calculation = new SimulatorCalculation
            {
                Model = model,
                Name = "Test Simulation Calculation",
                Type = "UserDefined",
                UserDefinedType = "Test/Calc"
            };
            var simEvent = new SimulationEvent
            {
                Calculation = calculation,
                CalculationId = fakeCalcId,
                Connector = fakeConnectorName,
                DataSetId = dataSetId,
                RunType = SimulationEventRunTypeValues.Manual,
                UserEmail = "some-email-address"
            };

            string eventToDelete = "";
            try
            {
                var readyEvents = await events.CreateSimulationEventReadyToRun(
                    new List<SimulationEvent> { simEvent },
                    CancellationToken.None).ConfigureAwait(false);
                Assert.NotEmpty(readyEvents);
                var readyEvent = readyEvents.First();
                eventToDelete = readyEvent.ExternalId;
                Assert.Equal(calculation.Model.Simulator, readyEvent.Source);
                Assert.Contains(BaseMetadata.DataTypeKey, readyEvent.Metadata.Keys);
                Assert.Contains(BaseMetadata.SimulatorKey, readyEvent.Metadata.Keys);
                Assert.Contains(BaseMetadata.DataModelVersionKey, readyEvent.Metadata.Keys);
                Assert.Contains(ModelMetadata.NameKey, readyEvent.Metadata.Keys);
                Assert.Contains(SimulatorIntegrationMetadata.ConnectorNameKey, readyEvent.Metadata.Keys);
                Assert.Contains(CalculationMetadata.TypeKey, readyEvent.Metadata.Keys);
                Assert.Contains(CalculationMetadata.NameKey, readyEvent.Metadata.Keys);
                Assert.Contains(CalculationMetadata.UserDefinedTypeKey, readyEvent.Metadata.Keys);
                Assert.Contains(SimulationEventMetadata.RunTypeKey, readyEvent.Metadata.Keys);
                Assert.Contains(SimulationEventMetadata.StatusKey, readyEvent.Metadata.Keys);
                Assert.Contains(SimulationEventMetadata.StatusMessageKey, readyEvent.Metadata.Keys);
                Assert.Contains(SimulationEventMetadata.CalculationIdKey, readyEvent.Metadata.Keys);
                Assert.Contains(SimulationEventMetadata.UserEmailKey, readyEvent.Metadata.Keys);

                Assert.Equal(SimulatorDataType.SimulationEvent.MetadataValue(), readyEvent.Metadata[BaseMetadata.DataTypeKey]);
                Assert.Equal(model.Simulator, readyEvent.Metadata[BaseMetadata.SimulatorKey]);
                Assert.Equal(model.Name, readyEvent.Metadata[ModelMetadata.NameKey]);
                Assert.Equal(fakeConnectorName, readyEvent.Metadata[SimulatorIntegrationMetadata.ConnectorNameKey]);
                Assert.Equal(calculation.Type, readyEvent.Metadata[CalculationMetadata.TypeKey]);
                Assert.Equal(calculation.Name, readyEvent.Metadata[CalculationMetadata.NameKey]);
                Assert.Equal(calculation.UserDefinedType, readyEvent.Metadata[CalculationMetadata.UserDefinedTypeKey]);
                Assert.Equal(simEvent.RunType, readyEvent.Metadata[SimulationEventMetadata.RunTypeKey]);
                Assert.Equal(SimulationEventStatusValues.Ready, readyEvent.Metadata[SimulationEventMetadata.StatusKey]);
                Assert.Equal(simEvent.CalculationId, readyEvent.Metadata[SimulationEventMetadata.CalculationIdKey]);
                Assert.Equal(simEvent.UserEmail, readyEvent.Metadata[SimulationEventMetadata.UserEmailKey]);

                var simulators = new Dictionary<string, long>
                {
                    { simEvent.Calculation.Model.Simulator, simEvent.DataSetId.Value }
                };

                int retryCount = 0;
                while (retryCount < 20)
                {
                    var foundReadyEvents = await events.FindSimulationEventsReadyToRun(
                        simulators,
                        fakeConnectorName,
                        CancellationToken.None).ConfigureAwait(false);
                    if (!foundReadyEvents.Any())
                    {
                        retryCount++;
                        await Task.Delay(100).ConfigureAwait(false);
                        continue;
                    }
                    Assert.NotEmpty(foundReadyEvents);
                    var foundReadyEvent = foundReadyEvents
                        .Where(e => e.ExternalId == readyEvent.ExternalId)
                        .First();
                    Assert.Equal(readyEvent.ExternalId, foundReadyEvent.ExternalId);
                    Assert.Equal(SimulationEventStatusValues.Ready, foundReadyEvent.Metadata[SimulationEventMetadata.StatusKey]);
                    break;
                }

                var simDate = DateTime.UtcNow;
                var runningEvent = await events.UpdateSimulationEventToRunning(
                    readyEvent.ExternalId,
                    simDate,
                    new Dictionary<string, string>
                    {
                        { "some", "metadata 1" }
                    },
                    1,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.Equal(readyEvent.ExternalId, runningEvent.ExternalId);
                Assert.Equal(SimulationEventStatusValues.Running, runningEvent.Metadata[SimulationEventMetadata.StatusKey]);

                // Needs to retry due to eventual consistency when updating CDF events.
                retryCount = 0;
                while (retryCount < 20)
                {
                    var foundRunningEvents = await events.FindSimulationEventsRunning(
                        simulators,
                        fakeConnectorName,
                        CancellationToken.None).ConfigureAwait(false);
                    if (!foundRunningEvents.Any())
                    {
                        retryCount++;
                        await Task.Delay(100).ConfigureAwait(false);
                        continue;
                    }
                    Assert.NotEmpty(foundRunningEvents);
                    var foundRunningEvent = foundRunningEvents
                        .Where(e => e.ExternalId == readyEvent.ExternalId)
                        .First();
                    Assert.Equal(SimulationEventStatusValues.Running, foundRunningEvent.Metadata[SimulationEventMetadata.StatusKey]);
                    Assert.Equal(simDate.ToUnixTimeMilliseconds(), foundRunningEvent.StartTime);
                    Assert.Equal("1", foundRunningEvent.Metadata[SimulationEventMetadata.ModelVersionKey]);
                    Assert.Equal("metadata 1", foundRunningEvent.Metadata["some"]);
                    break;
                }


                var successEvent = await events.UpdateSimulationEventToSuccess(
                    readyEvent.ExternalId,
                    simDate,
                    new Dictionary<string, string>
                    {
                        { "another", "metadata 2" }
                    },
                    "Calculation finished",
                    CancellationToken.None).ConfigureAwait(false);
                Assert.Equal(readyEvent.ExternalId, successEvent.ExternalId);
                Assert.Equal(SimulationEventStatusValues.Success, successEvent.Metadata[SimulationEventMetadata.StatusKey]);

                // Needs to retry due to eventual consistency when updating CDF events.
                retryCount = 0;
                while (retryCount < 20)
                {
                    var foundSuccessEvents = await events.FindSimulationEvents(
                        simulators,
                        new Dictionary<string, string>
                        {
                        { SimulationEventMetadata.StatusKey, SimulationEventStatusValues.Success },
                        { SimulatorIntegrationMetadata.ConnectorNameKey, fakeConnectorName }
                        },
                        CancellationToken.None).ConfigureAwait(false);
                    if (!foundSuccessEvents.Any())
                    {
                        retryCount++;
                        await Task.Delay(100).ConfigureAwait(false);
                        continue;
                    }
                    Assert.NotEmpty(foundSuccessEvents);
                    var foundSuccessEvent = foundSuccessEvents
                        .Where(e => e.ExternalId == readyEvent.ExternalId)
                        .First();
                    Assert.Equal(readyEvent.ExternalId, foundSuccessEvent.ExternalId);
                    Assert.Equal(SimulationEventStatusValues.Success, foundSuccessEvent.Metadata[SimulationEventMetadata.StatusKey]);
                    Assert.Equal(simDate.ToUnixTimeMilliseconds(), foundSuccessEvent.StartTime);
                    Assert.Equal("Calculation finished", foundSuccessEvent.Metadata[SimulationEventMetadata.StatusMessageKey]);
                    Assert.Equal("metadata 1", foundSuccessEvent.Metadata["some"]);
                    Assert.Equal("metadata 2", foundSuccessEvent.Metadata["another"]);
                    Assert.True(foundSuccessEvent.EndTime > simDate.ToUnixTimeMilliseconds());
                    break;
                }

                var failureEvent = await events.UpdateSimulationEventToFailure(
                    readyEvent.ExternalId,
                    simDate,
                    new Dictionary<string, string>
                    {
                        { "yetAnother", "metadata 3" }
                    },
                    "Calculation failed",
                    CancellationToken.None).ConfigureAwait(false);
                Assert.Equal(readyEvent.ExternalId, failureEvent.ExternalId);
                Assert.Equal(SimulationEventStatusValues.Failure, failureEvent.Metadata[SimulationEventMetadata.StatusKey]);

                // Needs to retry due to eventual consistency when updating CDF events.
                retryCount = 0;
                Event? foundFailureEvent = null;
                while (retryCount < 20)
                {
                    var foundFailureEvents = await events.FindSimulationEvents(
                        simulators,
                        new Dictionary<string, string>
                        {
                        { SimulationEventMetadata.StatusKey, SimulationEventStatusValues.Failure },
                        { SimulatorIntegrationMetadata.ConnectorNameKey, fakeConnectorName }
                        },
                        CancellationToken.None).ConfigureAwait(false);
                    var filteredFailureEvents = foundFailureEvents
                        .Where(e => e.ExternalId == readyEvent.ExternalId);
                    if (!filteredFailureEvents.Any())
                    {
                        retryCount++;
                        await Task.Delay(100).ConfigureAwait(false);
                        continue;
                    }
                    foundFailureEvent = filteredFailureEvents.First();
                    Assert.Equal(SimulationEventStatusValues.Failure, foundFailureEvent.Metadata[SimulationEventMetadata.StatusKey]);
                    Assert.Equal(simDate.ToUnixTimeMilliseconds(), foundFailureEvent.StartTime);
                    Assert.Equal("Calculation failed", foundFailureEvent.Metadata[SimulationEventMetadata.StatusMessageKey]);
                    Assert.Equal("metadata 1", foundFailureEvent.Metadata["some"]);
                    Assert.Equal("metadata 2", foundFailureEvent.Metadata["another"]);
                    Assert.Equal("metadata 3", foundFailureEvent.Metadata["yetAnother"]);
                    Assert.True(foundFailureEvent.EndTime > simDate.ToUnixTimeMilliseconds());
                    break;
                }
                Assert.NotNull(foundFailureEvent);
            }
            finally
            {
                // Cleanup created resources
                if (!string.IsNullOrEmpty(eventToDelete))
                {
                    await events.DeleteAsync(new List<string> { eventToDelete }, CancellationToken.None).ConfigureAwait(false);
                }

            }

        }
    }
}
