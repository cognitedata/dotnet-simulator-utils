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

        return GetFileExtension(file.Name);
    }

    /// <summary>
    /// Returns the file extension of a given file name.
    /// This is based on the file name and returns the extension in lowercase without the leading period
    /// </summary>
    public static string GetFileExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException("File name cannot be null or empty.");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            throw new ArgumentException($"File name does not contain a valid extension: {fileName}");
        }

        return extension.TrimStart('.').ToLowerInvariant();
    }
}
