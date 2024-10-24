using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Cognite.Extractor.Logging;
using Serilog.Core;
using Serilog.Events;

using CogniteSdk;
using Serilog.Core.Enrichers;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using CogniteSdk.Resources.Alpha;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Utility class for configuring simulator loggers.
    /// The logging framework used is <see href="https://serilog.net/">Serilog</see>.
    /// Loggers are created according to a <see cref="LoggerConfig"/> configuration object.
    /// Log messages contain UTC timestamps.
    /// </summary>
    public static class SimulatorLoggingUtils
    {
        /// <summary>
        /// Push log id into the log context.
        /// You can wrap the code that needs to be logged to the remote (API) sink with this method.
        /// </summary>
        /// <param name="cdf">Simulators resource</param>
        /// <param name="logId">Log id to push</param>
        /// <param name="checkForSeverityOverride">True to check for severity override</param>
        public static async Task<PropertyEnricher[]> GetLogEnrichers(SimulatorsResource cdf, long? logId, bool checkForSeverityOverride = false)
        {
            var enrichers = new List<PropertyEnricher>() {
                new PropertyEnricher("LogId", logId)
            };

            if (checkForSeverityOverride && logId.HasValue)
            {
                try {
                    var logRes = await cdf.RetrieveSimulatorLogsAsync([new Identity(logId.Value)]).ConfigureAwait(false);
                    var logItem = logRes.First();
                    if (logItem.Severity != null)
                    {
                        enrichers.Add(new PropertyEnricher("Severity", logItem.Severity));
                    }
                } catch (Exception) {
                    // Ignore, we don't want to fail everything if we can't get the log item
                }
            }

            return enrichers.ToArray();
        }

        // Enricher that creates a property with UTC timestamp.
        // See: https://github.com/serilog/serilog/issues/1024#issuecomment-338518695
        class UtcTimestampEnricher : ILogEventEnricher {
            public void Enrich(LogEvent logEvent, ILogEventPropertyFactory lepf) {
                logEvent.AddPropertyIfAbsent(
                    lepf.CreateProperty("UtcTimestamp", logEvent.Timestamp.UtcDateTime));
            }
        }

        /// <summary>
        /// Create a default Serilog console logger and returns it.
        /// </summary>
        /// <returns>A <see cref="Serilog.ILogger"/> logger with default properties</returns>
        public static Serilog.ILogger GetSerilogDefault() {
            return new LoggerConfiguration()
                .Enrich.With<UtcTimestampEnricher>()
                .Enrich.FromLogContext()
                .WriteTo.Console(LogEventLevel.Information, LoggingUtils.LogTemplate)
                .CreateLogger();
        }

        /// <summary>
        /// Creates a <see cref="Serilog.ILogger"/> logger according to the configuration in <paramref name="config"/>
        /// </summary>
        /// <param name="config">Configuration object of <see cref="LoggerConfig"/> type</param>
        /// <param name="logEventSink">A custom log event sink to write logs to</param>
        /// <returns>A configured logger</returns>
        public static Serilog.ILogger GetConfiguredLogger(LoggerConfig config, ILogEventSink logEventSink)
        {
            var logConfig = LoggingUtils.GetConfiguration(config);
            logConfig.WriteTo.Sink(logEventSink);
            logConfig.Enrich.With<UtcTimestampEnricher>();
            logConfig.Enrich.FromLogContext();
            return logConfig.CreateLogger();
        }

        /// <summary>
        /// Create a default console logger and returns it.
        /// </summary>
        /// <returns>A <see cref="Microsoft.Extensions.Logging.ILogger"/> logger with default properties</returns>
        public static Microsoft.Extensions.Logging.ILogger GetDefault() {
            using (var loggerFactory = new LoggerFactory())
            {
                loggerFactory.AddSerilog(GetSerilogDefault(), true);
                return loggerFactory.CreateLogger("default");
            }
        }

    }
   
    /// <summary>
    /// Extension utilities for logging
    /// </summary>
    public static class LoggingExtensions {

        /// <summary>
        /// Adds a configured Serilog logger as singleton of the <see cref="Microsoft.Extensions.Logging.ILogger"/> and
        /// <see cref="Serilog.ILogger"/> types to the <paramref name="services"/> collection.
        /// This is a Simulator specific logger as it writes logs to the remote sink (Simulators Logs resource in CDF).
        /// A configuration object of type <see cref="LoggerConfig"/> is required, and should have been added to the
        /// collection as well.
        /// 
        /// This defaults to <see cref="SimulatorLoggingUtils.GetConfiguredLogger(LoggerConfig, ILogEventSink)"/>
        /// which creates logging configuration for file and console using
        /// <see cref="LoggingUtils.GetConfiguration(LoggerConfig)"/>
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="alternativeLogger">True to allow alternative loggers, i.e. allow config.Console and config.File to be null</param>
        public static void AddLogger(this IServiceCollection services,  bool alternativeLogger = false) 
        {
            services.AddSingleton<ScopedRemoteApiSink>();
            services.AddSingleton<LoggerTraceListener>();
            services.AddSingleton(p =>
            {
                var remoteApiSink = p.GetService<ScopedRemoteApiSink>();
                var config = p.GetService<LoggerConfig>();
                if (config == null || !alternativeLogger && (config.Console == null && config.File == null))
                {
                    // No logging configuration
                    var defLog = SimulatorLoggingUtils.GetSerilogDefault();
                    defLog.Warning("No Logging configuration found. Using default logger");
                    return defLog;
                }
                return SimulatorLoggingUtils.GetConfiguredLogger(config, remoteApiSink);
            });
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.Services.AddSingleton<ILoggerProvider, SerilogLoggerProvider>(s =>
                {
                    var logger = s.GetRequiredService<Serilog.ILogger>();
                    return new SerilogLoggerProvider(logger, true);
                });
                loggingBuilder.AddFilter<SerilogLoggerProvider>(null, LogLevel.Trace);
            });
        }
    }
}