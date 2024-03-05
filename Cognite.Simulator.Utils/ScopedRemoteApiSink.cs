using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cognite.Simulator.Extensions;
using Cognite.Extractor.Utils;
using CogniteSdk.Alpha;

namespace Cognite.Simulator.Utils {

    /// <summary>
    /// Represents a sink for emitting log events to a remote API.
    /// </summary>
    /// 
    public class ScopedRemoteApiSink : ILogEventSink
    {
        private readonly CogniteDestination cdfClient;
        // Buffer for storing log data

        private readonly Dictionary<long, List<SimulatorLogDataEntry>> logBuffer = new Dictionary<long, List<SimulatorLogDataEntry>>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopedRemoteApiSink"/> class.
        /// </summary>
        /// <param name="client">CDF Destination</param>
        public ScopedRemoteApiSink(CogniteDestination client)
        {
            cdfClient = client;
        }

        public void Emit(LogEvent logEvent)
        {
            
            logEvent.Properties.TryGetValue("LogId", out var logId);
            if (logId != null){
                long logIdLong = long.Parse(logId.ToString());
                // Customize the log data to send to the remote API
                var logData = new SimulatorLogDataEntry
                {
                    Timestamp = logEvent.Timestamp.ToUnixTimeMilliseconds(),
                    Severity = logEvent.Level.ToString(),
                    Message = logEvent.RenderMessage(),
                };

                if(logBuffer.ContainsKey(logIdLong)){
                    logBuffer[logIdLong].Add(logData);
                } else {
                    logBuffer.Add(logIdLong, new List<SimulatorLogDataEntry>(){logData});
                }
            }
        }

        /// <summary>
        /// Flushes the collected logs to the remote API.
        /// </summary>
        public void Flush()
        {
            // Send the collected logs to the remote API
            SendToRemoteApi(logBuffer).Wait(); // Wait for the request to complete

            // Clear the log buffer
            logBuffer.Clear();
        }

        private async Task SendToRemoteApi(Dictionary<long, List<SimulatorLogDataEntry>> logs)
        {
            try {
                foreach (var log in logs)
                {
                    await cdfClient.CogniteClient.Alpha.Simulators.UpdateLogsBatch(
                        log.Key,
                        log.Value
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