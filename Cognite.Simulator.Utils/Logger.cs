using System;
// using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

// using Serilog;
// using Serilog.Core;
// using Serilog.Events;
using Serilog.Extensions.Logging;
using Cognite.Extractor.Logging;
using Serilog.Core;
using Serilog.Events;

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
                .WriteTo.Console(LogEventLevel.Information, LoggingUtils.LogTemplate)
                .WriteTo.Sink(new ScopedRemoteApiSink("https://azure-dev.cognitedata.com"))
                // TODO add sink here
                .CreateLogger();
        }

        /// <summary>
        /// Creates a <see cref="Serilog.ILogger"/> logger according to the configuration in <paramref name="config"/>
        /// </summary>
        /// <param name="config">Configuration object of <see cref="LoggerConfig"/> type</param>
        /// <returns>A configured logger</returns>
        public static Serilog.ILogger GetConfiguredLogger(LoggerConfig config)
        {
            var logConfig = LoggingUtils.GetConfiguration(config);
            return logConfig.CreateLogger();
        }

    }
   
    /// <summary>
    /// Extension utilities for logging
    /// </summary>
    public static class LoggingExtensions {

        

        /// <summary>
        /// Adds a configured Serilog logger as singletons of the <see cref="Microsoft.Extensions.Logging.ILogger"/> and
        /// <see cref="Serilog.ILogger"/> types to the <paramref name="services"/> collection.
        /// A configuration object of type <see cref="LoggerConfig"/> is required, and should have been added to the
        /// collection as well.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="buildLogger">Method to build the logger.
        /// <param name="alternativeLogger">True to allow alternative loggers, i.e. allow config.Console and config.File to be null</param>
        /// This defaults to <see cref="SimulatorLoggingUtils.GetConfiguredLogger(LoggerConfig)"/>,
        /// which creates logging configuration for file and console using
        /// <see cref="LoggingUtils.GetConfiguration(LoggerConfig)"/></param>
        public static void AddLogger(this IServiceCollection services, Func<LoggerConfig, Serilog.ILogger> buildLogger = null, bool alternativeLogger = false) {
            // PRINT 1
            Console.WriteLine("here --------------------------- --- ------------------------------------------");
            
            buildLogger = buildLogger ?? SimulatorLoggingUtils.GetConfiguredLogger;
            services.AddSingleton<LoggerTraceListener>();
            services.AddSingleton<Serilog.ILogger>(p => {
                var config = p.GetService<LoggerConfig>();
                if (config == null || !alternativeLogger && (config.Console == null && config.File == null)) {
                    // No logging configuration
                    var defLog = SimulatorLoggingUtils.GetSerilogDefault();
                    defLog.Warning("No Logging configuration found. Using default logger");
                    return defLog;
                }
                return buildLogger(config);
            });
            services.AddLogging(loggingBuilder => {
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