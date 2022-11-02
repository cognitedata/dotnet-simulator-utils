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
        /// Replace all occurences of special characters
        /// </summary>
        /// <param name="s">Input string</param>
        /// <param name="replaceWith">String to use as replacement</param>
        /// <returns>A new string</returns>
        public static string ReplaceSpecialCharacters(this string s, string replaceWith)
        {
            return Regex.Replace(s, "[^a-zA-Z0-9_.]+", replaceWith, RegexOptions.Compiled);
        }

        /// <summary>
        /// Replace all occurences of slash and backslash
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

        internal static string GetCalcTypeForIds(this SimulatorCalculation calc)
        {
            if (calc.Type == "UserDefined" && !string.IsNullOrEmpty(calc.UserDefinedType))
            {
                return $"{calc.Type.ReplaceSpecialCharacters("_")}-{calc.UserDefinedType.ReplaceSpecialCharacters("_")}";
            }
            return calc.Type.ReplaceSpecialCharacters("_");
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
