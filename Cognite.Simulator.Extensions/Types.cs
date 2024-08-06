using System.Collections.Generic;
using System.Linq;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Represents a simulator model file
    /// </summary>
    public class SimulatorModelInfo
    {
        /// <summary>
        /// Simulator name
        /// </summary>
        public string Simulator { get; set; }

        /// <summary>
        /// Model external id
        /// </summary>
        public string ExternalId { get; set; }
    }

    /// <summary>
    /// Represents a simulator routine short info 
    /// </summary>
    public class SimulatorRoutineRevisionInfo
    {
        /// <summary>
        /// Routine revision external id
        /// </summary>
        public string ExternalId { get; set; }

        /// <summary>
        /// Routine external id
        /// </summary>
        public string RoutineExternalId { get; set; }

        /// <summary>
        /// Simulator model associated with this routine
        /// </summary>
        public SimulatorModelInfo Model { get; set; }

        /// <summary>
        /// Routine external id with special characters replaced
        /// </summary>
        public string ExternalIdSafeChars
        {
            get {
                return ExternalId.ReplaceSlashAndBackslash("_");
            }
        }
    }

    /// <summary>
    /// Represents the sampled inputs used in a simulation
    /// </summary>
    public class SimulationInput : SimulationTimeSeries
    {
        internal override string TimeSeriesName =>
            $"{Name} - INPUT - {ReferenceId} - {RoutineRevisionInfo.ExternalIdSafeChars}";

        internal override string TimeSeriesDescription =>
            $"Input {ReferenceId} sampled for {RoutineRevisionInfo.ExternalId}";

        /// <summary>
        /// Indicates if the time series should be saved back to CDF
        /// </summary>
        public bool ShouldSaveToTimeSeries {
            get {
                return !string.IsNullOrEmpty(SaveTimeseriesExternalId);
            }
        }
    }

    /// <summary>
    /// Represents the results of a simulation run
    /// </summary>
    public class SimulationOutput : SimulationTimeSeries
    {
        internal override string TimeSeriesName => 
            $"{Name} - OUTPUT - {ReferenceId} - {RoutineRevisionInfo.ExternalIdSafeChars}";

        internal override string TimeSeriesDescription =>
            $"Simulation result {ReferenceId} for {RoutineRevisionInfo.ExternalId}";
    }

    /// <summary>
    /// Represents a simulation variable associated with a simulation run. For instance,
    /// simulation sampled inputs and result outputs
    /// </summary>
    public abstract class SimulationTimeSeries
    {
        /// <summary>
        /// Allows saving the time series value back to CDF
        /// with the one provided by the user
        /// </summary>
        public string SaveTimeseriesExternalId { get; set; }

        /// <summary>
        /// Routine revision associated with this variable
        /// </summary>
        public SimulatorRoutineRevisionInfo RoutineRevisionInfo { get; set; }

        /// <summary>
        /// Unique identifier in a given routine
        /// </summary>
        public string ReferenceId { get; set; }

        /// <summary>
        /// Variable name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Variable unit
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// Any other metadata related to this variable
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        internal abstract string TimeSeriesName
        {
            get;
        }
        internal abstract string TimeSeriesDescription
        {
            get;
        }

        /// <summary>
        /// Add a (key, value) pair as metadata
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        public void AddMetadata(string key, string value)
        {
            if (Metadata == null)
            {
                Metadata = new Dictionary<string, string>();
            }
            Metadata[key] = value;
        }

        /// <summary>
        /// Get the metadata value associated with the given key, if any
        /// </summary>
        /// <param name="key">Key</param>
        /// <returns>Metadata value, if key exists. Else, <c>null</c></returns>
        public string GetMetadata(string key)
        {
            if (Metadata != null && Metadata.ContainsKey(key))
            {
                return Metadata[key];
            }
            return null;
        }
    }
}
