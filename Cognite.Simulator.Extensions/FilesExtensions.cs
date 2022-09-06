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

        /// <summary>
        /// Find all simulator model file versions stored in CDF that match the given
        /// <paramref name="simulator"/> and <paramref name="modelName"/> parameters.
        /// All versions of the model are returned
        /// </summary>
        /// <param name="cdfFiles">CDF Files resource</param>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        /// <param name="dataset">Optional dataset containing the files</param>
        /// <param name="token">Cancellation token</param>
        public static async Task<IEnumerable<File>> FindModelVersions(
            this FilesResource cdfFiles,
            string simulator,
            string modelName,
            long? dataset,
            CancellationToken token
            )
        {
            var metadata = new Dictionary<string, string>
            {
                { BaseMetadata.DataTypeKey, ModelMetadata.DataType.MetadataValue()},
                { ModelMetadata.NameKey, modelName}
            };
            return await FetchFilesFromCdf(
                cdfFiles,
                metadata,
                new Dictionary<string, long?> { { simulator, dataset } },
                null,
                token).ConfigureAwait(false);
        }

        /// <summary>
        /// Find all simulation configuration files stored in CDF that match the given
        /// <paramref name="simulator"/>, <paramref name="modelName"/> and <paramref name="calcType"/> parameters.
        /// </summary>
        /// <param name="cdfFiles">CDF Files resource</param>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        /// <param name="calcType">Calculation type</param>
        /// <param name="calcTypeUserDefined">User defined type, if any</param>
        /// <param name="dataset">Optional dataset containing the files</param>
        /// <param name="token">Cancellation token</param>
        public static async Task<IEnumerable<File>> FindConfigurationFiles(
            this FilesResource cdfFiles,
            string simulator,
            string modelName,
            string calcType,
            string calcTypeUserDefined,
            long? dataset,
            CancellationToken token
            )
        {
            var metadata = new Dictionary<string, string>
            {
                { BaseMetadata.DataTypeKey, CalculationMetadata.DataType.MetadataValue() },
                { ModelMetadata.NameKey, modelName },
                { CalculationMetadata.TypeKey, calcType }
            };
            if (!string.IsNullOrEmpty(calcTypeUserDefined))
            {
                metadata.Add(CalculationMetadata.UserDefinedTypeKey, calcTypeUserDefined);
            }
            return await FetchFilesFromCdf(
                cdfFiles,
                metadata,
                new Dictionary<string, long?> { { simulator, dataset } },
                null,
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
                metadata[BaseMetadata.SimulatorKey] = source.Key;
                string cursor = null;
                var filter = new FileFilter
                {
                    Source = source.Key,
                    Uploaded = true,
                    Metadata = metadata,
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
