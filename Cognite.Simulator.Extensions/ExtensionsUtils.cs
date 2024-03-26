using Cognite.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
            if (newEntries != null && newEntries.Any())
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

        internal static string GetModelNameForIds(this SimulatorModelInfo model)
        {
            return model.Name.ReplaceSpecialCharacters("_");
        }

        internal static string GetModelNameForNames(this SimulatorModelInfo model)
        {
            return model.Name.ReplaceSlashAndBackslash("_");
        }

        // internal static string GetCalcTypeForIds(this SimulatorCalculation calc)
        // {
        //     if (calc.Type == "UserDefined" && !string.IsNullOrEmpty(calc.UserDefinedType))
        //     {
        //         return calc.UserDefinedType.ReplaceSpecialCharacters("_");
        //     }
        //     return calc.Type.ReplaceSpecialCharacters("_");
        // }
        
        // internal static string GetCalcTypeForNames(this SimulatorRoutineRevisionInfo calc)
        // {
        //     // if (calc.Type == "UserDefined" && !string.IsNullOrEmpty(calc.UserDefinedType))
        //     // {
        //     //     return $"{calc.Type.ReplaceSlashAndBackslash("_")}-{calc.UserDefinedType.ReplaceSlashAndBackslash("_")}";
        //     // }
        //     return calc..ReplaceSlashAndBackslash("_");
        // }

        internal static string GetCalcNameForNames(this SimulatorRoutineRevisionInfo calc)
        {
            return calc.RoutineExternalId.ReplaceSlashAndBackslash("_");
        }

        internal static Dictionary<string, string> GetCommonMetadata(
            this SimulatorRoutineRevisionInfo calc,
            SimulatorDataType dataType)
        {
            var metadata = calc.Model.GetCommonMetadata(dataType);
            metadata.AddRange(
                new Dictionary<string, string>()
                {
                    // { CalculationMetadata.TypeKey, calc.RoutineExternalId },
                    { RoutineRevisionMetadataForTS.RoutineExternalId, calc.RoutineExternalId },
                    { RoutineRevisionMetadataForTS.RoutineRevisionExternalId, calc.ExternalId },
                });
            // if (calc.Type == "UserDefined" && !string.IsNullOrEmpty(calc.UserDefinedType))
            // {
            //     metadata.Add(CalculationMetadata.UserDefinedTypeKey, calc.UserDefinedType);
            // }
            return metadata;
        }

        internal static Dictionary<string, string> GetCommonMetadata(
            this SimulatorModelInfo model,
            SimulatorDataType dataType)
        {
            return new Dictionary<string, string>()
            {
                { BaseMetadata.DataModelVersionKey, BaseMetadata.DataModelVersionValue },
                { BaseMetadata.SimulatorKey, model.Simulator },
                { ModelMetadata.NameKey, model.Name },
                { BaseMetadata.DataTypeKey, dataType.MetadataValue() }
            };
        }
    }

    /// <summary>
    /// Represents a time range used for sampling data points in a time series
    /// </summary>
    public class SamplingRange
    {
        internal CogniteSdk.TimeRange TimeRange { get; }
        
        /// <summary>
        /// Midpoint between the start and end timestamps, in milliseconds
        /// </summary>
        public long Midpoint { get; }
        
        /// <summary>
        /// Start of the sampling range, in milliseconds
        /// </summary>
        public long? Start => TimeRange.Min;
        
        /// <summary>
        /// End of the sampling range, in milliseconds
        /// </summary>
        public long? End => TimeRange.Max;

        /// <summary>
        /// Constructs a sampling range from a time range
        /// </summary>
        /// <param name="timeRange">Time range</param>
        /// <exception cref="ArgumentNullException">Thrown when the time range is null</exception>
        public SamplingRange(CogniteSdk.TimeRange timeRange)
        {
            if (timeRange == null)
            {
                throw new ArgumentNullException(nameof(timeRange));
            }
            TimeRange = timeRange;
            Midpoint = (long)(timeRange.Min + (timeRange.Max - timeRange.Min) / 2);
        }

        /// <summary>
        /// Implicitly constructs a sampling range from a time range
        /// </summary>
        /// <param name="timeRange">Time range</param>
        public static implicit operator SamplingRange(CogniteSdk.TimeRange timeRange)
        {
            return new SamplingRange(timeRange);
        }
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
        public CogniteException(string message, IEnumerable<CogniteError> errors)
            : base(message)
        {
            CogniteErrors = errors;
        }
    }

}
