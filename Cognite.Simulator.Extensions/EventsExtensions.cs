using Cognite.Extensions;
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
        /// <summary>
        /// Update the simulation event with the given external ID (<paramref name="externalId"/>).
        /// Update the event start time (<paramref name="startTime"/>) and its
        /// metadata
        /// </summary>
        /// <param name="cdfEvents">CDF Events resource</param>
        /// <param name="id">Simulation event ID</param>
        /// <param name="startTime">Event start time</param>
        /// <param name="metadata">Event metadata</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Updated simulation event</returns>
        public static async Task<Event> UpdateSimulationEvent(
            this EventsResource cdfEvents,
            long id,
            DateTime startTime,
            Dictionary<string, string> metadata,
            CancellationToken token)
        {
            var eventUpdate = new EventUpdateItem(id)
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
