using Cognite.Extractor.Common;
using CogniteSdk;
using CogniteSdk.Resources;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Class containing extensions to the CDF Files resource with utility methods
    /// for simulator integrations
    /// </summary>
    public static class FilesExtensions
    {
        /// <summary>
        /// Find all the files in CDF for the given simulator data type (<paramref name="dataType"/>), 
        /// simulator sources (<paramref name="sources"/>) and data sets (optionally).
        /// If the <paramref name="updatedAfter"/> date is specified, only fetch files updated after that date 
        /// and time
        /// </summary>
        /// <param name="cdfFiles">CDF Files resource</param>
        /// <param name="dataType">Data type of the file</param>
        /// <param name="sources">Dictionary of simulator names with associated datasets</param>
        /// <param name="updatedAfter">Only fetch files updated after this timestamp</param>
        /// <param name="token">Cancellation token</param>
        public static async Task<IEnumerable<File>> FindSimulatorFiles(
            this FilesResource cdfFiles,
            SimulatorDataType dataType,
            Dictionary<string, long?> sources,
            DateTime? updatedAfter,
            CancellationToken token
            )
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }
            if (sources.Count == 0)
            {
                throw new ArgumentException("As least one simulator should be specified", nameof(sources));
            }
            var metadata = new Dictionary<string, string>
            {
                { BaseMetadata.DataTypeKey, dataType.MetadataValue()}
            };
            return await FetchFilesFromCdf(
                cdfFiles,
                metadata,
                sources,
                updatedAfter,
                token).ConfigureAwait(false);
        }

        private static async Task<IEnumerable<File>> FetchFilesFromCdf(
            this FilesResource cdfFiles,
            Dictionary<string, string> metadata,
            Dictionary<string, long?> sources,
            DateTime? updatedAfter,
            CancellationToken token
            )
        {
            var result = new List<File>();
            foreach (var source in sources)
            {
                var filterMetadata = new Dictionary<string, string>
                {
                    { BaseMetadata.SimulatorKey, source.Key }
                };
                filterMetadata.AddRange(metadata);
                string cursor = null;
                var filter = new FileFilter
                {
                    Source = source.Key,
                    Uploaded = true,
                    Metadata = filterMetadata,
                };
                if (source.Value.HasValue)
                {
                    filter.DataSetIds = new List<Identity> { new Identity(source.Value.Value) };
                }
                if (updatedAfter.HasValue)
                {
                    filter.LastUpdatedTime = new CogniteSdk.TimeRange
                    {
                        Min = updatedAfter.Value.ToUnixTimeMilliseconds() + 1
                    };
                }
                do
                {
                    var fileList = await cdfFiles.ListAsync(new FileQuery
                    {
                        Filter = filter,
                        Cursor = cursor,
                    }, token).ConfigureAwait(false);
                    cursor = fileList.NextCursor;
                    foreach (var file in fileList.Items)
                    {
                        result.Add(file);
                    }
                }
                while (cursor != null);
            }
            return result;
        }
    }
}
