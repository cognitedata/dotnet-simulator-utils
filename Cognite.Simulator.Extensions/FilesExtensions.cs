using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.Common;

using CogniteSdk.Resources;

namespace Cognite.Simulator.Extensions;

/// <summary>
/// Class containing extensions to the CDF Files resource with utility methods
/// for simulator integrations
/// </summary>
public static class FilesExtensions
{
    private const int THROTTLE_SIZE = 5;
    private const int CHUNK_SIZE = 1000;

    /// <summary>
    /// Retrieves file medatadata by their IDs in chunks, ignores unknown IDs.
    /// Each batch can contain up to 1000 file IDs and is be processed in parallel (up to 5 batches at a time).
    /// </summary>
    /// <param name="cdfFiles">The FilesResource instance.</param>
    /// <param name="internalIds">The list of internal IDs of the files to retrieve.</param>
    /// <param name="token">The cancellation token.</param>
    public static async Task<List<CogniteSdk.File>> RetrieveBatchAsync(
        this FilesResource cdfFiles,
        IList<long> internalIds,
        CancellationToken token = default
    )
    {
        var result = new List<CogniteSdk.File>();
        object mutex = new object();

        if (internalIds == null || internalIds.Count == 0)
        {
            return result;
        }

        var fileIdsByChunks = internalIds
            .ChunkBy(CHUNK_SIZE);

        var generators = fileIdsByChunks
            .Select<IEnumerable<long>, Func<Task>>(
            (chunk, _) => async () =>
            {
                var found = await cdfFiles
                    .RetrieveAsync(chunk, true, token)
                    .ConfigureAwait(false);
                lock (mutex)
                {
                    result.AddRange(found);
                }
            });

        await generators.RunThrottled(THROTTLE_SIZE, token).ConfigureAwait(false);

        return result;
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
}
