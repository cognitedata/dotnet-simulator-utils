using Serilog.Core;
using Serilog.Events;
using Serilog.Context;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using Cognite.Extensions;
using Cognite.Simulator.Extensions;
using CogniteSdk.Types.Common;
using Cognite.Extractor.Utils;
using CogniteSdk;
using Cognite.Extensions.DataModels.QueryBuilder;
using CogniteSdk.Alpha;
using System.Linq;
using System.Threading;

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
            Console.WriteLine($"Emitting log: {logEvent.RenderMessage()}");
            
            logEvent.Properties.TryGetValue("LogId", out var logId);
            if (logId != null){
                long logIdLong = long.Parse(logId.ToString());
                Console.WriteLine("LogID is: " + logId);
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

                // Store log data in the buffer
                Console.WriteLine(logBuffer[logIdLong].Count);
            }
        }

        /// <summary>
        /// Flushes the collected logs to the remote API.
        /// </summary>
        public void Flush()
        {
            Console.WriteLine($"Flushing {logBuffer.Count} logs");
            // Send the collected logs to the remote API
            Console.WriteLine(logBuffer.First());
            SendToRemoteApi(logBuffer).Wait(); // Wait for the request to complete

            // Clear the log buffer
            logBuffer.Clear();
        }

        private async Task SendToRemoteApi(Dictionary<long, List<SimulatorLogDataEntry>> logs)
        {
            Console.WriteLine(logs.First().Key);
            Console.WriteLine(logs.First().Value);
            Console.WriteLine($"Sending ALL LOGS ({logs.Values.Count}) to CDF");
            try
            {
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