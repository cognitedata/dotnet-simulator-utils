using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Cognite.Simulator.Extensions
{
    public static class ExtensionsUtils
    {
        public static string ReplaceSpecialCharacters(this string s, string replaceWith)
        {
            return Regex.Replace(s, "[^a-zA-Z0-9_.]+", replaceWith, RegexOptions.Compiled);
        }

        public static string ReplaceSlashAndBackslash(this string s, string replaceWith)
        {
            return Regex.Replace(s, "[/\\\\]", replaceWith, RegexOptions.Compiled);
        }
    }
}
