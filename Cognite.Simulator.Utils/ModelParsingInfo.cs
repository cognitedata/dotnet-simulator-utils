using System.Text.Json.Serialization;
using CogniteSdk.Alpha;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents the information produced during model parsing.
    /// That is, when a model is downloaded, the connector will try to open the model
    /// with the simulator, extract information from the model and update that information in CDF
    /// (if necessary). The parsing info should capture information about this process (error, status, etc) 
    /// </summary>
    public class ModelParsingInfo
    {
        /// <summary>
        /// Model parsing status
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SimulatorModelRevisionStatus Status { get; set; }

        /// <summary>
        /// Status message
        /// </summary>
        public string StatusMessage { get; set; }
        
        /// <summary>
        /// Whether or not the model was parsed
        /// </summary>
        public bool Parsed { get; set; }
        
        /// <summary>
        /// If there were any errors during parsing
        /// </summary>
        public bool Error { get; set; }
        /// <summary>
        /// Timestamp of when this entry was updated last
        /// </summary>
        public long LastUpdatedTime { get; set; }
    }

    /// <summary>
    /// Extension utilities for the model parsing info
    /// TODO: move the methods to model parsing info 
    /// </summary>
    public static class ModelParsingExtensions
    {

        /// <summary>
        /// Update the model info status to success
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        public static void SetSuccess(this ModelParsingInfo mpi)
        {
            mpi.SetStatus(SimulatorModelRevisionStatus.success, true, false, "Model parsed successfully");
        }

        /// <summary>
        /// Update the model info status to failure
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        /// <param name="statusMessage">Status message</param>
        public static void SetFailure(this ModelParsingInfo mpi, string statusMessage = "Model parsing failed")
        {
            mpi.SetStatus(SimulatorModelRevisionStatus.failure, true, true, statusMessage);
        }

        private static void SetStatus(this ModelParsingInfo mpi, SimulatorModelRevisionStatus status, bool isParsed, bool isError, string statusMessage)
        {
            mpi.Status = status;
            mpi.Parsed = isParsed;
            mpi.Error = isError;
            mpi.StatusMessage = statusMessage;
            mpi.LastUpdatedTime = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
