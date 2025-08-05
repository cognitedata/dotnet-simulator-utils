using System;
using System.IO;

namespace Cognite.Simulator.Extensions;

/// <summary>
/// Class containing extensions to the CDF Files resource with utility methods
/// for simulator integrations
/// </summary>
public static class FilesExtensions
{
    /// <summary>
    /// Returns the file extension of a given CDF file.
    /// This is based on the file name and returns the extension in lowercase without the leading period.
    /// </summary>
    public static string GetExtension(this CogniteSdk.File file)
    {
        if (file == null)
        {
            throw new ArgumentNullException(nameof(file), "File cannot be null.");
        }

        if (string.IsNullOrEmpty(file.Name))
        {
            throw new ArgumentException($"File name cannot be null or empty. File ID: {file.Id}");
        }

        var extension = Path.GetExtension(file.Name);

        if (string.IsNullOrEmpty(extension))
        {
            throw new ArgumentException($"File name does not contain a valid extension: {file.Name}");
        }

        return extension.TrimStart('.').ToLowerInvariant();
    }
}
