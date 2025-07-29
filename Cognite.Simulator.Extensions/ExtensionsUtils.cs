using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Cognite.Extensions;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Utility class related to the extensions methods
    /// </summary>
    public static class ExtensionsUtils
    {
        /// <summary>
        /// Replace all occurrences of special characters
        /// </summary>
        /// <param name="s">Input string</param>
        /// <param name="replaceWith">String to use as replacement</param>
        /// <returns>A new string</returns>
        public static string ReplaceSpecialCharacters(this string s, string replaceWith)
        {
            return Regex.Replace(s, "[^a-zA-Z0-9_.]+", replaceWith, RegexOptions.Compiled);
        }

        /// <summary>
        /// Replace all occurrences of slash and backslash
        /// </summary>
        /// <param name="s">Input string</param>
        /// <param name="replaceWith">String to use as replacement</param>
        /// <returns>A new string</returns>
        public static string ReplaceSlashAndBackslash(this string s, string replaceWith)
        {
            return Regex.Replace(s, "[/\\\\]", replaceWith, RegexOptions.Compiled);
        }

        internal static void AddRange(
            this Dictionary<string, string> dict,
            Dictionary<string, string> newEntries)
        {
            if (newEntries != null && newEntries.Count > 0)
            {
                foreach (var pair in newEntries)
                {
                    if (dict.ContainsKey(pair.Key))
                    {
                        continue;
                    }
                    dict.Add(pair.Key, pair.Value);
                }
            }
        }

        internal static Dictionary<string, string> GetCommonMetadata(
            this SimulatorRoutineRevisionInfo calc,
            SimulatorDataType dataType)
        {
            var metadata = calc.Model.GetCommonMetadata(dataType);
            metadata.AddRange(
                new Dictionary<string, string>()
                {
                    { RoutineRevisionMetadataForTS.RoutineExternalId, calc.RoutineExternalId },
                    { RoutineRevisionMetadataForTS.RoutineRevisionExternalId, calc.ExternalId },
                });
            return metadata;
        }

        private static Dictionary<string, string> GetCommonMetadata(
            this SimulatorModelInfo model,
            SimulatorDataType dataType)
        {
            return new Dictionary<string, string>()
            {
                { BaseMetadata.SimulatorKey, model.Simulator },
                { ModelMetadata.ExternalId, model.ExternalId },
                { BaseMetadata.DataTypeKey, dataType.MetadataValue() }
            };
        }
    }

    /// <summary>
    /// Represents the configuration to sample data points in a time series
    /// </summary>
    public class SamplingConfiguration
    {
        /// <summary>
        /// Time associated with the simulation, in milliseconds 
        /// </summary>
        public long SimulationTime { get; }

        /// <summary>
        /// Start of the sampling range, in milliseconds
        /// </summary>
        public long? Start { get; }

        /// <summary>
        /// End of the sampling range, in milliseconds
        /// </summary>
        public long End { get; }

        /// <summary>
        /// Constructs a sampling configuration
        /// </summary>
        /// <param name="end">end time, in milliseconds</param>
        /// <param name="start">start time, in milliseconds. Only required when data sampling is enabled.</param>
        /// <param name="samplingPosition">Position of the simulation time relative to the sampling window. Only relevant when data sampling is enabled. Defaults to <see cref="SamplingPosition.Midpoint"/>.</param>
        public SamplingConfiguration(
            long end,
            long? start = null,
            SamplingPosition samplingPosition = SamplingPosition.Midpoint)
        {
            End = end;
            if (start.HasValue)
            {
                Start = start;
                if (samplingPosition == SamplingPosition.Start)
                    SimulationTime = Start.Value;
                else if (samplingPosition == SamplingPosition.Midpoint)
                    SimulationTime = Start.Value + (End - Start.Value) / 2;
                else if (samplingPosition == SamplingPosition.End)
                    SimulationTime = End;
                else
                    throw new ArgumentException("Invalid sampling position");
            }
            else
            {
                SimulationTime = end;
            }
        }
    }

    /// <summary>
    /// Defines the position of the simulation time relative to the sampling window
    /// </summary>
    public enum SamplingPosition
    {
        /// <summary>
        /// Simulation time is at the start of the sampling window
        /// </summary>
        Start,
        /// <summary>
        /// Simulation time is at the midpoint of the sampling window
        /// </summary>
        Midpoint,
        /// <summary>
        /// Simulation time is at the end of the sampling window
        /// </summary>
        End
    }

    /// <summary>
    /// Represent errors related to read/write data in CDF
    /// </summary>
    public class CogniteException : Exception
    {
        /// <summary>
        /// Errors that triggered this exception
        /// </summary>
        public IEnumerable<CogniteError> CogniteErrors { get; private set; }

        /// <summary>
        /// Create a new exception containing the provided <paramref name="errors"/> and <paramref name="message"/>
        /// </summary>
        protected CogniteException(string message, IEnumerable<CogniteError> errors)
            : base(message)
        {
            CogniteErrors = errors;
        }
    }

}
