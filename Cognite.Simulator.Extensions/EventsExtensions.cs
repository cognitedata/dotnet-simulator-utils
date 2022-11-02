﻿using Cognite.Extensions;
using Cognite.Extractor.Common;
using CogniteSdk;
using CogniteSdk.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Class containing extensions to the CDF Events resource with utility methods
    /// for simulator integrations
    /// </summary>
    public static class EventsExtensions
    {
        public static async Task<IEnumerable<Event>> FindSimulationEventsReadyToRun(
            this EventsResource cdfEvents,
            Dictionary<string, long> simulators,
            string connectorName,
            CancellationToken token)
        {
            return await cdfEvents.FindSimulationEvents(
                simulators,
                new Dictionary<string, string>()
                {
                    { SimulationEventMetadata.StatusKey, SimulationEventStatusValues.Ready },
                    { SimulatorIntegrationMetadata.ConnectorNameKey, connectorName }
                },
                token).ConfigureAwait(false);
        }

        public static async Task<IEnumerable<Event>> FindSimulationEventsRunning(
            this EventsResource cdfEvents,
            Dictionary<string, long> simulators,
            string connectorName,
            CancellationToken token)
        {
            return await cdfEvents.FindSimulationEvents(
                simulators,
                new Dictionary<string, string>()
                {
                    { SimulationEventMetadata.StatusKey, SimulationEventStatusValues.Running },
                    { SimulatorIntegrationMetadata.ConnectorNameKey, connectorName }
                },
                token).ConfigureAwait(false);
        }

        /// <summary>
        /// Search for simulation run events in CDF for each simulator in <paramref name="simulators"/>
        /// </summary>
        /// <param name="cdfEvents">CDF Events resource</param>
        /// <param name="simulators">Dictionary of (simulator name, data set id) pairs</param>
        /// <param name="metadata">Dictionary with metadata (key, value) pairs</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>All simualtor events for the given simulators, containing the given metadata</returns>
        public static async Task<IEnumerable<Event>> FindSimulationEvents(
            this EventsResource cdfEvents,
            Dictionary<string, long> simulators,
            Dictionary<string, string> metadata,
            CancellationToken token)
        {
            var result = new List<Event>();
            if (simulators == null || !simulators.Any())
            {
                return result;
            }
            
            foreach (var source in simulators)
            {
                var eventMetadata = new Dictionary<string, string>()
                {
                    { BaseMetadata.SimulatorKey, source.Key },
                    { BaseMetadata.DataTypeKey, SimulatorDataType.SimulationEvent.MetadataValue() },
                };
                eventMetadata.AddRange(metadata);

                string cursor = null;
                var filter = new EventFilter
                {
                    Source = source.Key,
                    Type = SimulatorDataType.SimulationEvent.MetadataValue(),
                    Metadata = eventMetadata,
                    DataSetIds = new List<Identity> { new Identity(source.Value) }
                };
                var query = new EventQuery
                {
                    Filter = filter
                };
                do
                {
                    query.Cursor = cursor;
                    var events = await cdfEvents
                        .ListAsync(query, token)
                        .ConfigureAwait(false);
                    if (events.Items.Any())
                    {
                        result.AddRange(events.Items);
                    }
                    cursor = events.NextCursor;
                }
                while (cursor != null);
            }
            return result;
        }
        public static async Task<Event> UpdateSimulationEventToRunning(
            this EventsResource cdfEvents,
            string externalId,
            DateTime startTime,
            Dictionary<string, string> metadata,
            int modelVersion,
            CancellationToken token)
        {
            // Update the event with model version and status
            Dictionary<string, string> eventMetadata = new Dictionary<string, string>()
            {
                { SimulationEventMetadata.StatusKey, SimulationEventStatusValues.Running },
                { SimulationEventMetadata.ModelVersionKey, modelVersion.ToString() }
            };

            eventMetadata.AddRange(metadata);

            var updatedEvent = await cdfEvents
                .UpdateSimulationEvent(externalId, startTime, eventMetadata, token)
                .ConfigureAwait(false);
            return await CheckEventStatus(cdfEvents, updatedEvent, SimulationEventStatusValues.Running, token)
                .ConfigureAwait(false);
        }

        public static async Task<Event> UpdateSimulationEventToSuccess(
            this EventsResource cdfEvents,
            string externalId,
            DateTime startTime,
            Dictionary<string, string> metadata,
            string statusMessage,
            CancellationToken token)
        {
            // Update the event with model version and status
            Dictionary<string, string> eventMetadata = new Dictionary<string, string>()
            {
                { SimulationEventMetadata.StatusKey, SimulationEventStatusValues.Success },
                { SimulationEventMetadata.StatusMessageKey, statusMessage }
            };

            eventMetadata.AddRange(metadata);

            var updatedEvent = await cdfEvents
                .UpdateSimulationEvent(externalId, startTime, eventMetadata, token)
                .ConfigureAwait(false);
            return await CheckEventStatus(cdfEvents, updatedEvent, SimulationEventStatusValues.Success, token)
                .ConfigureAwait(false);
        }

        public static async Task<Event> UpdateSimulationEventToFailure(
            this EventsResource cdfEvents,
            string externalId,
            DateTime startTime,
            Dictionary<string, string> metadata,
            string statusMessage,
            CancellationToken token)
        {
            // Update the event with model version and status
            Dictionary<string, string> eventMetadata = new Dictionary<string, string>()
            {
                { SimulationEventMetadata.StatusKey, SimulationEventStatusValues.Failure },
                { SimulationEventMetadata.StatusMessageKey, statusMessage }
            };

            eventMetadata.AddRange(metadata);

            var updatedEvent = await cdfEvents
                .UpdateSimulationEvent(externalId, startTime, eventMetadata, token)
                .ConfigureAwait(false);
            return await CheckEventStatus(cdfEvents, updatedEvent, SimulationEventStatusValues.Failure, token)
                .ConfigureAwait(false);
        }

        public static async Task<Event> UpdateSimulationEvent(
            this EventsResource cdfEvents,
            string externalId,
            DateTime startTime,
            Dictionary<string, string> metadata,
            CancellationToken token)
        {
            var eventUpdate = new EventUpdateItem(externalId)
            {
                Update = new EventUpdate
                {
                    StartTime = new Update<long?>(startTime.ToUnixTimeMilliseconds()),
                    Metadata = new UpdateDictionary<string>(metadata, null)
                }
            };
            if (metadata != null && 
                metadata.TryGetValue(SimulationEventMetadata.StatusKey, out var status) && 
                (status == SimulationEventStatusValues.Success || status == SimulationEventStatusValues.Failure))
            {
                eventUpdate.Update.EndTime = new Update<long?>(DateTime.UtcNow.ToUnixTimeMilliseconds());
            }
            var events = await cdfEvents.UpdateAsync(
                new List<EventUpdateItem>() { eventUpdate },
                token).ConfigureAwait(false);
            
            return events.First();
        }

        /// <summary>
        /// Tries to verify that a given event created in CDF (<paramref name="cdfEvent"/>) can be
        /// found through a filter query. Due to eventual consistency issues with the Events API that may
        /// not be always the case.
        /// </summary>
        private static async Task<Event> CheckEventStatus(
            EventsResource cdfEvents,
            Event cdfEvent,
            string status,
            CancellationToken token)
        {
            int retryCount = 0;
            while (retryCount < 20)
            {
                var retrievedEvents = await cdfEvents
                    .ListAsync(new EventQuery
                    {
                        Filter = new EventFilter
                        {
                            ExternalIdPrefix = cdfEvent.ExternalId,
                            Metadata = cdfEvent.Metadata,
                        }
                    },
                    token)
                    .ConfigureAwait(false);
                if (retrievedEvents.Items.Any())
                {
                    return cdfEvent;
                }
                retryCount++;
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            return cdfEvent;
        }

        public static async Task<IEnumerable<Event>> CreateSimulationEventReadyToRun(
            this EventsResource cdfEvents,
            IEnumerable<SimulationEvent> simulationEvents,
            CancellationToken token)
        {
            if (simulationEvents == null || !simulationEvents.Any())
            {
                return Enumerable.Empty<Event>();
            }
            var eventsToCreate = simulationEvents.Select(e => BuildSimulatorRunEvent(e)).ToList();
            var createdEvents = await cdfEvents.EnsureExistsAsync(
                eventsToCreate,
                100,
                1,
                RetryMode.None,
                SanitationMode.None,
                token).ConfigureAwait(false);
            if (!createdEvents.IsAllGood)
            {
                throw new SimulationEventException("Could not find or create simulator integration sequence", createdEvents.Errors);
            }
            var events = createdEvents.Results;
            foreach (Event e in events)
            {
                await CheckEventStatus(cdfEvents, e, SimulationEventStatusValues.Ready, token)
                    .ConfigureAwait(false);
            }
            return events;
        }

        private static EventCreate BuildSimulatorRunEvent(
            SimulationEvent simulationEvent)
        {
            var calculation = simulationEvent.Calculation;
            var calcTypeForId = calculation.GetCalcTypeForIds();
            var modelNameForId = calculation.Model.Name.ReplaceSpecialCharacters("_");
            var externalId = $"{calculation.Model.Simulator}-SR-{calcTypeForId}-{modelNameForId}-{DateTime.UtcNow.ToUnixTimeMilliseconds()}";
            var metadata = new Dictionary<string, string>()
            {
                { BaseMetadata.SimulatorKey, calculation.Model.Simulator },
                { BaseMetadata.DataTypeKey, SimulatorDataType.SimulationEvent.MetadataValue() },
                { BaseMetadata.DataModelVersionKey, BaseMetadata.DataModelVersionValue },
                { ModelMetadata.NameKey, calculation.Model.Name },
                { SimulatorIntegrationMetadata.ConnectorNameKey, simulationEvent.Connector },
                { CalculationMetadata.TypeKey, calculation.Type },
                { CalculationMetadata.NameKey, calculation.Name },
                { SimulationEventMetadata.RunTypeKey, simulationEvent.RunType },
                { SimulationEventMetadata.StatusKey, SimulationEventStatusValues.Ready },
                { SimulationEventMetadata.StatusMessageKey, "Calculation ready to run" },
                { SimulationEventMetadata.CalculationIdKey, simulationEvent.CalculationId },
                { SimulationEventMetadata.UserEmailKey, simulationEvent.UserEmail }
            };
            if (!string.IsNullOrEmpty(calculation.UserDefinedType))
            {
                metadata.Add(CalculationMetadata.UserDefinedTypeKey, calculation.UserDefinedType);
            }
            var eventCreate = new EventCreate
            {
                ExternalId = externalId,
                Type = SimulatorDataType.SimulationEvent.MetadataValue(),
                Source = calculation.Model.Simulator,
                Subtype = calculation.Name,
                Metadata = metadata,
                Description = $"{calculation.Model.Simulator} simulation event"
            };
            if (simulationEvent.DataSetId.HasValue)
            {
                eventCreate.DataSetId = simulationEvent.DataSetId.Value;
            }
            return eventCreate;
        }
    }

    /// <summary>
    /// Represent errors related to read/write simulation events in CDF
    /// </summary>
    public class SimulationEventException : CogniteException
    {
        /// <summary>
        /// Create a new exception containing the provided <paramref name="errors"/> and <paramref name="message"/>
        /// </summary>
        public SimulationEventException(string message, IEnumerable<CogniteError> errors)
            : base(message, errors)
        {
        }
    }

}
