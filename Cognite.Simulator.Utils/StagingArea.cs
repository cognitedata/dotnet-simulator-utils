using System.Text.Json.Serialization;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Base class for staging data
    /// </summary>
    public class StagingData
    {
        /// <summary>
        /// Timestamp of when this entry was updated last
        /// </summary>
        public long LastUpdatedTime { get; set; }
    }

    /// <summary>
    /// Staging data representing the information produced during model parsing.
    /// That is, when a model is downloaded, the connector will try to open the model
    /// with the simulator, extract information from the model and update that information in CDF
    /// (if necessary). The parsing info should capture information about this process (logs, status, etc) 
    /// </summary>
    public class ModelParsingInfo : StagingData
    {
        /// <summary>
        /// Model name
        /// </summary>
        public string ModelName { get; set; }
        
        /// <summary>
        /// Simulator name
        /// </summary>
        public string Simulator { get; set; }
        
        /// <summary>
        /// Model version
        /// </summary>
        public int ModelVersion { get; set; }

        /// <summary>
        /// Model parsing status
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ParsingStatus Status { get; set; }
        
        /// <summary>
        /// Whether or not the model was parsed
        /// </summary>
        public bool Parsed { get; set; }
        
        /// <summary>
        /// If there were any errors during parsing
        /// </summary>
        public bool Error { get; set; }
    }
    
    /// <summary>
    /// Model parsing status
    /// </summary>
    public enum ParsingStatus
    {
        /// <summary>
        /// Model is ready to be parsed
        /// </summary>
        ready,
        /// <summary>
        /// Model parsing is in progress
        /// </summary>
        running,
        /// <summary>
        /// Parsing failed, but it is possible to retry
        /// </summary>
        retrying,
        /// <summary>
        /// Model successfully parsed
        /// </summary>
        success,
        /// <summary>
        /// Failed to parse the model
        /// </summary>
        failure
    }

    /// <summary>
    /// Parsing log entry
    /// </summary>
    public class ParsingLog
    {
        /// <summary>
        /// Creates a new parsing log entry
        /// </summary>
        /// <param name="level">Log level</param>
        /// <param name="message">Message</param>
        public ParsingLog(ParsingLogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        /// <summary>
        /// Log level
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ParsingLogLevel Level { get; set; }
        
        /// <summary>
        /// Log message
        /// </summary>
        public string Message { get; set; }

    }

    /// <summary>
    /// Parsing log level
    /// </summary>
    public enum ParsingLogLevel
    {
        /// <summary>
        /// Information level
        /// </summary>
        INFORMATION,
        /// <summary>
        /// Warning level
        /// </summary>
        WARNING,
        /// <summary>
        /// Error level
        /// </summary>
        ERROR
    }

    /// <summary>
    /// Extension utilities for the model parsing info
    /// </summary>
    public static class ModelParsingExtensions
    {

        /// <summary>
        /// Update the model info status to success
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        public static void SetSuccess(this ModelParsingInfo mpi)
        {
            mpi.SetStatus(ParsingStatus.success, true, false);
        }

        /// <summary>
        /// Update the model info status to retrying
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        public static void SetRetrying(this ModelParsingInfo mpi)
        {
            mpi.SetStatus(ParsingStatus.retrying, false, true);
        }

        /// <summary>
        /// Update the model info status to failure
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        public static void SetFailure(this ModelParsingInfo mpi)
        {
            mpi.SetStatus(ParsingStatus.failure, true, true);
        }

        private static void SetStatus(this ModelParsingInfo mpi, ParsingStatus status, bool isParsed, bool isError)
        {
            mpi.Status = status;
            mpi.Parsed = isParsed;
            mpi.Error = isError;
        }
    }
}
