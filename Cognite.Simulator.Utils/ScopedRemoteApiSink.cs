using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Simulator.Extensions;

using CogniteSdk.Alpha;
using CogniteSdk.Resources.Alpha;

using Serilog.Core;
using Serilog.Events;

namespace Cognite.Simulator.Utils
{

    /// <summary>
    /// Represents a sink for emitting log events to a remote API.
    /// </summary>
    /// 
    public class ScopedRemoteApiSink : ILogEventSink
    {
        private bool enabled;
        private LogEventLevel minSeverityLevel;

        // Buffer for storing log data
        private readonly ConcurrentDictionary<long, List<SimulatorLogDataEntry>> logBuffer = new ConcurrentDictionary<long, List<SimulatorLogDataEntry>>();
        private long? defaultLogId;
        private static readonly LogEventLevel DefaultMinimumLevel = LogEventLevel.Warning;
        private static readonly LogEventLevel[] AllowedLogLevels = new[] { LogEventLevel.Debug, LogEventLevel.Information, LogEventLevel.Warning, LogEventLevel.Error };

        /// <summary>
        /// Create a scoped api sink
        /// </summary>
        /// <param name="loggerConfig">The logger configuration</param>
        public ScopedRemoteApiSink(LoggerConfig loggerConfig) : base()
        {
            if (loggerConfig != null)
            {
                enabled = loggerConfig.Remote == null || loggerConfig.Remote.Enabled;
                if (enabled)
                {
                    minSeverityLevel = ParseLogLevel(loggerConfig.Remote?.Level);
                }
            }
        }

        private static LogEventLevel ParseLogLevel(string level)
        {
            if (string.IsNullOrEmpty(level))
            {
                return DefaultMinimumLevel;
            }
            if (!Enum.TryParse(level, true, out LogEventLevel logLevel))
            {
                throw new ArgumentException($"Unknown minimum log level for remote API: {level}");
            }
            return logLevel;
        }

        /// <summary>
        /// Sets the default log ID.
        /// </summary>
        /// <param name="logId">The default log ID to set.</param>
        public void SetDefaultLogId(long logId)
        {
            defaultLogId = logId;
        }

        /// <summary>
        /// Store the log in the buffer to be sent to the remote API.
        /// </summary>
        /// <param name="logEvent">The log event to emit.</param>
        public void Emit(LogEvent logEvent)
        {
            if (!enabled)
            {
                return;
            }

            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            var minLevel = minSeverityLevel;

            if (logEvent.Properties.TryGetValue("Severity", out var value) && value is ScalarValue sv && sv.Value is string overrideSeverity)
            {
                minLevel = ParseLogLevel(overrideSeverity);
            }

            if (logEvent.Level < minLevel)
            {
                return;
            }

            if (!AllowedLogLevels.Contains(logEvent.Level))
            {
                // Fatal and Verbose are not supported by the remote API.
                // In case of Verbose, we can just ignore it.
                // In case of Fatal, we will most likely not be able to send the logs to the remote API anyway.
                return;
            }

            logEvent.Properties.TryGetValue("LogId", out var logId);

            if (logId == null && defaultLogId == null)
            {
                return;
            }
            long logIdLong = logId == null ? defaultLogId.Value : long.Parse(logId.ToString());
            if (logIdLong == 0)
            {
                return;
            }
            // Customize the log data to send to the remote API
            var logData = new SimulatorLogDataEntry
            {
                Timestamp = logEvent.Timestamp.ToUnixTimeMilliseconds(),
                Severity = logEvent.Level.ToString(),
                Message = logEvent.RenderMessage(),
            };

            logBuffer.AddOrUpdate(logIdLong, new List<SimulatorLogDataEntry>() { logData }, (key, oldValue) =>
            {
                oldValue.Add(logData);
                return oldValue;
            });

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
                try
                {
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