using System;
using System.Collections.Generic;
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
    }
}
