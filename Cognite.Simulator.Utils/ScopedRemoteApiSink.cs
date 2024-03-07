using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cognite.Simulator.Extensions;
using CogniteSdk.Alpha;
using CogniteSdk.Resources.Alpha;

namespace Cognite.Simulator.Utils {

    /// <summary>
    /// Represents a sink for emitting log events to a remote API.
    /// </summary>
    /// 
    public class ScopedRemoteApiSink : ILogEventSink
    {
        // private readonly CogniteDestination cdfClient;
        // Buffer for storing log data

        private readonly Dictionary<long, List<SimulatorLogDataEntry>> logBuffer = new Dictionary<long, List<SimulatorLogDataEntry>>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopedRemoteApiSink"/> class.
        /// </summary>
        /// <param name="client">CDF Destination</param>
        public ScopedRemoteApiSink()
        {
            // cdfClient = client;
        }

        /// <summary>
        /// Store the log in the buffer to be sent to the remote API.
        /// </summary>
        /// <param name="logEvent">The log event to emit.</param>
        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            if (logEvent.Level < LogEventLevel.Warning)
            {
                return;
            }

            logEvent.Properties.TryGetValue("LogId", out var logId);
            if (logId != null)
            {
                long logIdLong = long.Parse(logId.ToString());
                // Customize the log data to send to the remote API
                var logData = new SimulatorLogDataEntry
                {
                    Timestamp = logEvent.Timestamp.ToUnixTimeMilliseconds(),
                    Severity = logEvent.Level.ToString(),
                    Message = logEvent.RenderMessage(),
                };

                logBuffer.AddOrUpdate(logIdLong, new List<SimulatorLogDataEntry>(){ logData }, (key, oldValue) => {
                    oldValue.Add(logData);
                    return oldValue;
                });
            }
        }

        /// <summary>
        /// Flushes the logs to the remote API and clears the buffer.
        /// </summary>
        public void Flush(SimulatorsResource client)
        {
            // Send the collected logs to the remote API
            SendToRemoteApi(client, logBuffer).Wait(); // Wait for the request to complete

            // Clear the log buffer
            logBuffer.Clear();
        }

        private async Task SendToRemoteApi(SimulatorsResource client, Dictionary<long, List<SimulatorLogDataEntry>> logs)
        {
            try {
                foreach (var log in logs)
                {
                    await client.UpdateLogsBatch(
                        log.Key,
                        log.Value
                    ).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                try {
                    // to make sure we remove only the logs that were sent to the remote API
                    if (logBuffer.TryRemove(log.Key, out var logData))
                    {
                        await client.UpdateLogsBatch(
                            log.Key,
                            logData,
                            token
                        ).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send logs to CDF: {ex}");
                }
            }
        }
    }
}