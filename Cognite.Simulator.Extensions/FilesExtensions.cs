using System;

using CogniteSdk;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Class containing extensions to the CDF Files resource with utility methods
    /// for simulator integrations
    /// </summary>
    public static class FilesExtensions
    {
        /// <summary>
        /// Returns the file extension of a given CDF file.
        /// This is based on the file name and returns the part after the last dot.
        /// </summary>
        public static string GetExtension(this File file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file), "File cannot be null.");
            }

            if (string.IsNullOrEmpty(file.Name))
            {
                throw new ArgumentException($"File name cannot be null or empty. File ID: {file.Id}");
            }

            var lastDotIndex = file.Name.LastIndexOf('.');
            if (lastDotIndex > 0 && lastDotIndex < file.Name.Length - 1)
            {
                return file.Name.Substring(lastDotIndex + 1).ToLowerInvariant();
            }
            throw new ArgumentException("File name does not contain a valid extension: {fileName}", file.Name);
        }
    }
}
