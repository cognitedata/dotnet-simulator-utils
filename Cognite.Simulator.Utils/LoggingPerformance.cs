using System;

using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// High-performance logging extensions that match ILogger's method names.
    /// </summary>
    public static class LoggingPerformance
    {
        private static readonly Action<ILogger, string, Exception> DebugTemplate =
            LoggerMessage.Define<string>(LogLevel.Debug, 0, "{Message}");

        private static readonly Action<ILogger, string, object, Exception> ParamTemplate =
            LoggerMessage.Define<string, object>(LogLevel.Information, 1, "{Message}: {Param}");

        private static readonly Action<ILogger, string, object, object, Exception> ParamsTemplate =
            LoggerMessage.Define<string, object, object>(LogLevel.Information, 2, "{Message}: {Param1}, {Param2}");

        private static readonly Action<ILogger, string, object, object, object, Exception> ErrorThreeParamTemplate =
            LoggerMessage.Define<string, object, object, object>(LogLevel.Error, 3, "{Message}: {Param1}, {Param2}, {Param3}");

        private static readonly Action<ILogger, string, Exception> ErrorTemplate =
            LoggerMessage.Define<string>(LogLevel.Error, 4, "{Message}");

        private static readonly Action<ILogger, string, object, Exception> ErrorParamTemplate =
            LoggerMessage.Define<string, object>(LogLevel.Error, 5, "{Message}: {Param}");

        private static readonly Action<ILogger, string, object, object, Exception> ErrorParamsTemplate =
            LoggerMessage.Define<string, object, object>(LogLevel.Error, 6, "{Message}: {Param1}, {Param2}");

        private static readonly Action<ILogger, string, object, object, object, Exception> ThreeParamsTemplate =
            LoggerMessage.Define<string, object, object, object>(LogLevel.Information, 0, "{Message}: {Param1}, {Param2}, {Param3}");

        private static readonly Action<ILogger, string, object, object, object, object, Exception> FourParamsTemplate =
            LoggerMessage.Define<string, object, object, object, object>(LogLevel.Information, 0, "{Message}: {Param1}, {Param2}, {Param3}, {Param4}");

        /// <summary>Logs a trace message.</summary>
        public static void LogTrace(this ILogger logger, string message) =>
            LoggerMessage.Define<string>(LogLevel.Trace, 0, "{Message}")(logger, message, null);

        /// <summary>Logs a trace message with a parameter.</summary>
        public static void LogTrace(this ILogger logger, string message, object param) =>
            LoggerMessage.Define<string, object>(LogLevel.Trace, 0, "{Message}: {Param}")(logger, message, param, null);

        /// <summary>Logs a trace message with two parameters.</summary>
        public static void LogTrace(this ILogger logger, string message, object param1, object param2) =>
            LoggerMessage.Define<string, object, object>(LogLevel.Trace, 0, "{Message}: {Param1}, {Param2}")(logger, message, param1, param2, null);

        /// <summary>Logs a debug message.</summary>
        public static void LogDebug(this ILogger logger, string message) =>
            DebugTemplate(logger, message, null);

        /// <summary>Logs a debug message with a parameter.</summary>
        public static void LogDebug(this ILogger logger, string message, object param) =>
            ParamTemplate(logger, message, param, null);

        /// <summary>Logs a debug message with two parameters.</summary>
        public static void LogDebug(this ILogger logger, string message, object param1, object param2) =>
            ParamsTemplate(logger, message, param1, param2, null);

        /// <summary>Logs an information message.</summary>
        public static void LogInformation(this ILogger logger, string message) =>
            LoggerMessage.Define<string>(LogLevel.Information, 0, "{Message}")(logger, message, null);

        /// <summary>Logs an information message with a parameter.</summary>
        public static void LogInformation(this ILogger logger, string message, object param) =>
            ParamTemplate(logger, message, param, null);

        /// <summary>Logs an information message with two parameters.</summary>
        public static void LogInformation(this ILogger logger, string message, object param1, object param2) =>
            ParamsTemplate(logger, message, param1, param2, null);

        /// <summary>Logs a warning message.</summary>
        public static void LogWarning(this ILogger logger, string message) =>
            LoggerMessage.Define<string>(LogLevel.Warning, 0, "{Message}")(logger, message, null);

        /// <summary>Logs a warning message with a parameter.</summary>
        public static void LogWarning(this ILogger logger, string message, object param) =>
            ParamTemplate(logger, message, param, null);

        /// <summary>Logs a warning message with two parameters.</summary>
        public static void LogWarning(this ILogger logger, string message, object param1, object param2) =>
            ParamsTemplate(logger, message, param1, param2, null);

        /// <summary>Logs a warning message with three parameters.</summary>
        public static void LogWarning(this ILogger logger, string message, object param1, object param2, object param3) =>
            ThreeParamsTemplate(logger, message, param1, param2, param3, null);

        /// <summary>Logs an error message with optional exception.</summary>
        public static void LogError(this ILogger logger, string message, Exception ex = null) =>
            ErrorTemplate(logger, message, ex);

        /// <summary>Logs an error message with a parameter and optional exception.</summary>
        public static void LogError(this ILogger logger, string message, object param, Exception ex = null) =>
            ErrorParamTemplate(logger, message, param, ex);

        /// <summary>Logs an error message with two parameters and optional exception.</summary>
        public static void LogError(this ILogger logger, string message, object param1, object param2, Exception ex = null) =>
            ErrorParamsTemplate(logger, message, param1, param2, ex);

        /// <summary>Logs an information message with three parameters.</summary>
        public static void LogInformation(this ILogger logger, string message, object param1, object param2, object param3) =>
            ThreeParamsTemplate(logger, message, param1, param2, param3, null);

        /// <summary>Logs an information message with four parameters.</summary>
        public static void LogInformation(this ILogger logger, string message, object param1, object param2, object param3, object param4) =>
            FourParamsTemplate(logger, message, param1, param2, param3, param4, null);

        /// <summary>Logs an error message with three parameters and optional exception.</summary>
        public static void LogError(this ILogger logger, string message, object param1, object param2, object param3, Exception ex = null) =>
            ErrorThreeParamTemplate(logger, message, param1, param2, param3, ex);

        /// <summary>Logs an error message with four parameters and optional exception.</summary>
        public static void LogError(this ILogger logger, string message, object param1, object param2, object param3, object param4, Exception ex = null) =>
            FourParamsTemplate(logger, message, param1, param2, param3, param4, ex);
    }
}



