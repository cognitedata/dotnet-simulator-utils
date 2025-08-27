using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Cognite.Extensions;

using CogniteSdk.Resources;

namespace Cognite.Simulator.Extensions;

/// <summary>
/// Class containing extensions to the CDF Files resource with utility methods
/// for simulator integrations
/// </summary>
public static class FilesExtensions
{
    /// <summary>
    /// Retrieves files metadata and their download URIs by their IDs.
    /// Ignores unknown IDs.
    /// </summary>
    public static async Task<List<DownloadableFile>> RetrieveDownloadableFiles(this FilesResource cdfFiles, IEnumerable<long> fileIds)
    {
        if (fileIds == null || !fileIds.Any())
        {
            return new() { };
        }

        var files = await cdfFiles
            .RetrieveAsync(fileIds)
            .ConfigureAwait(false);

        if (!files.Any())
        {
            return new() { };
        }

        var downloadUrisRes = await cdfFiles
            .DownloadAsync(files.Select(f => f.Id))
            .ConfigureAwait(false);

        var downloadUrisMap = downloadUrisRes
            .ToDictionarySafe(res => res.Id, res => res.DownloadUrl);

        return files.Select(file => new DownloadableFile
        {
            Entity = file,
            DownloadUrl = downloadUrisMap.TryGetValue(file.Id, out var url) ? url : null
        }).ToList();
    }

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

    /// <summary>
    /// Represents a Cognite file with an associated download URL.
    /// </summary>
    public class DownloadableFile
    {
        /// <summary>
        /// Url from which file can be downloaded.
        /// </summary>

        public Uri DownloadUrl { get; set; }

        /// <summary>
        /// File metadata.
        /// </summary>
        public CogniteSdk.File Entity { get; set; }
    }
}
