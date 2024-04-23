using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cognite.Simulator.Extensions;
using CogniteSdk.Alpha;
using CogniteSdk.Resources.Alpha;
using System.Collections.Concurrent;
using System.Threading;
using Oryx.Cognite;

namespace Cognite.Simulator.Utils {

    /// <summary>
    /// Represents a sink for emitting log events to a remote API.
    /// </summary>
    /// 
    public class ScopedRemoteApiSink : ILogEventSink
    {
        private SimulatorLoggingConfig apiLoggerConfig;
        // Buffer for storing log data
        private readonly ConcurrentDictionary<long, List<SimulatorLogDataEntry>> logBuffer = new ConcurrentDictionary<long, List<SimulatorLogDataEntry>>();

        /// <summary>
        /// Store the log in the buffer to be sent to the remote API.
        /// </summary>
        /// <param name="logEvent">The log event to emit.</param>
        public void Emit(LogEvent logEvent)
        {
            if(apiLoggerConfig == null || !apiLoggerConfig.Enabled)
            {
                return;
            }
            
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            if (logEvent.Level < apiLoggerConfig.Level)
            {
                return;
            }

            logEvent.Properties.TryGetValue("LogId", out var logIdProp);
            var logIdStr = logIdProp?.ToString();
            var isLogIdNull = string.IsNullOrEmpty(logIdStr);
            
            if (isLogIdNull)
            {
                return;
            }

            if(long.TryParse(logIdStr, out var logId)) {    
                // Customize the log data to send to the remote API
                var logData = new SimulatorLogDataEntry
                {
                    Timestamp = logEvent.Timestamp.ToUnixTimeMilliseconds(),
                    Severity = logEvent.Level.ToString(),
                    Message = logEvent.RenderMessage(),
                };

                logBuffer.AddOrUpdate(logId, new List<SimulatorLogDataEntry>(){ logData }, (key, oldValue) => {
                    oldValue.Add(logData);
                    return oldValue;
                });
            }
            
        }

        /// <summary>
        /// Sets the configuration for the remote API.
        /// </summary>
        /// <param name="config">This configuration sets the minimum log level to report to the APi and wether the remote logging is enabled or not.</param>
        public void SetConfig(SimulatorLoggingConfig config)
        {
            if(config == null){
                apiLoggerConfig = new SimulatorLoggingConfig{
                    Level = LogEventLevel.Information,
                    Enabled = true
                };
            } else {
                apiLoggerConfig = config;
            }
        }

        /// <summary>
        /// Flushes the logs to the remote API and clears the buffer.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <param name="client">Simulator resource client</param>
        /// <returns></returns>
        public async Task Flush(SimulatorsResource client, CancellationToken token)
        {
            await SendToRemoteApi(client, token).ConfigureAwait(false);
        }

        private async Task SendToRemoteApi(SimulatorsResource client, CancellationToken token)
        {
            foreach (var log in logBuffer)
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