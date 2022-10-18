﻿using Cognite.Extensions;
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
