using Cognite.Extractor.Common;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    public abstract class FileLibrary<T, U>
        where T : FileState
        where U : FileStatePoco
    {
        // Public properties
        public Dictionary<string, T> State => _state;


        // Injected services
        protected readonly CogniteDestination _cdf;
        protected readonly CogniteSdk.Resources.FilesResource _cdfFiles;
        protected readonly ILogger _logger;
        private readonly FileLibraryConfig _config;
        private readonly IList<SimulatorConfig> _simulators;
        private readonly IExtractionStateStore _store;
        private readonly FileDownloadClient _downloadClient;

        // Internal objects
        protected readonly Dictionary<string, T> _state;
        private readonly BaseExtractionState _libState;
        private readonly SimulatorDataType _resourceType;
        private string _modelFolder;

        public FileLibrary(
            SimulatorDataType resourceType,
            FileLibraryConfig config,
            IList<SimulatorConfig> simulators,
            CogniteDestination cdf,
            ILogger logger,
            FileDownloadClient downloadClient,
            IExtractionStateStore store = null)
        {
            _config = config;
            _simulators = simulators;
            _cdf = cdf;
            _cdfFiles = _cdf.CogniteClient.Files;
            _store = store;
            _logger = logger;
            _state = new Dictionary<string, T>();
            _libState = new BaseExtractionState(_config.LibraryId);
            _modelFolder = _config.FilesDirectory;
            _resourceType = resourceType;
            _downloadClient = downloadClient;
        }
        
        /// <summary>
        /// Initializes the local file library. Create the directory, find the files in CDF and restore state.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Init(CancellationToken token)
        {
            _logger.LogDebug("Ensuring directory to store files exists: {Path}", _modelFolder);
            var dir = Directory.CreateDirectory(_modelFolder);
            _modelFolder = dir.FullName;

            if (_store != null)
            {
                await _store.RestoreExtractionState(
                    new Dictionary<string, BaseExtractionState>() { { _config.LibraryId, _libState } },
                    _config.LibraryTable,
                    true,
                    token).ConfigureAwait(false);
            }
            await FindFiles(false, token)
                .ConfigureAwait(false);
            if (_store != null)
            {
                await _store.RestoreExtractionState<U, T>(
                    _state,
                    _config.FilesTable,
                    (state, poco) =>
                    {
                        state.Init(poco);
                    },
                    token).ConfigureAwait(false);
                if (_store is LiteDBStateStore ldbStore)
                {
                    HashSet<string> idsToKeep = new HashSet<string>(_state.Select(s => s.Value.Id));
                    ldbStore.RemoveUnusedState(_config.FilesTable, idsToKeep);
                }
            }
            _logger.LogInformation("Local state store {Table} initiated. Tracking {Num} files", _config.FilesTable, _state.Count);
        }

        protected async Task RemoveStates(
            IEnumerable<FileState> states,
            CancellationToken token)
        {
            if (states == null || !states.Any())
            {
                return;
            }
            await _store
                .RemoveFileStates(_config.FilesTable, states, token)
                .ConfigureAwait(false);
        }

        protected abstract T StateFromFile(CogniteSdk.File file);

        /// <summary>
        /// Fetch the Files from CDF for the configured Source and Data Sets.
        /// Build a local state to keep track of what files exist and which ones have 
        /// been downloaded
        /// </summary>
        /// <param name="onlyLatest">Fetch only the files updated after the latest timestamp in the local store</param>
        /// <param name="token">Cancellation token</param>
        private async Task FindFiles(
            bool onlyLatest,
            CancellationToken token)
        {
            DateTime? updatedAfter = null;
            if (onlyLatest && !_libState.DestinationExtractedRange.IsEmpty)
            {
                updatedAfter = _libState.DestinationExtractedRange.Last;
            }
            var files = await _cdfFiles.FindSimulatorFiles(
                _resourceType,
                _simulators.ToDictionary(s => s.Name, s => (long?)s.DataSetId),
                updatedAfter,
                token).ConfigureAwait(false);

            foreach (var file in files)
            {
                T fState = StateFromFile(file);
                if (fState == null)
                {
                    continue;
                }
                if (!_state.ContainsKey(file.ExternalId))
                {
                    // If the file does not exist locally, add it to the state store
                    _state.Add(file.ExternalId, fState);
                }
                else if (_state[fState.Id].UpdatedTime < fState.UpdatedTime)
                {
                    // If the file exists in the state store but was updated in CDF, use the new file instead
                    await _store.RemoveFileStates(
                        _config.FilesTable,
                        new List<FileState> { _state[fState.Id] },
                        token).ConfigureAwait(false);
                    _state[fState.Id] = fState;
                }
            }
        }

        public IEnumerable<Task> GetRunTasks(CancellationToken token)
        {
            return new List<Task> { SaveStates(token), SearchAndDownloadFiles(token) };
        }


        /// <summary>
        /// Periodically searches for new files in CDF, in case new ones are found, download them and store locally.
        /// Files are saved with the internal CDF id as name
        /// </summary>
        /// <param name="token">Cancellation token</param>
        private async Task SearchAndDownloadFiles(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                string timeRange = _libState.DestinationExtractedRange.IsEmpty ? "Empty" : _libState.DestinationExtractedRange.ToString();
                _logger.LogDebug("Updating file file library. There are currently {Num} files. Extracted range: {TimeRange}",
                    _state.Count,
                    timeRange
                    );

                // Find new model files in CDF and add the to the local state.
                await FindFiles(true, token)
                    .ConfigureAwait(false);

                // Find the files that are not yet saved locally (no file path)
                var files = _state.Values
                    .Where(f => string.IsNullOrEmpty(f.FilePath))
                    .OrderBy(f => f.UpdatedTime)
                    .ToList();

                foreach (var file in files)
                {
                    // Get the download URL for the file. Could fetch more than one per request, but the 
                    // URL expires after 30 seconds. Best to do one by one.
                    _logger.LogInformation("Downloading file: {Id}. Created on {CreatedTime}. Updated on {UpdatedTime}",
                        file.Id,
                        CogniteTime.FromUnixTimeMilliseconds(file.CreatedTime).ToISOString(),
                        CogniteTime.FromUnixTimeMilliseconds(file.UpdatedTime).ToISOString());
                    try
                    {
                        var response = await _cdfFiles
                            .DownloadAsync(new[] { new Identity(file.Id) }, token)
                            .ConfigureAwait(false);
                        if (response.Any() && response.First().DownloadUrl != null)
                        {
                            var uri = response.First().DownloadUrl;
                            var filename = Path.Combine(_modelFolder, $"{file.CdfId}.{file.GetExtension()}");
                            bool downloaded = await _downloadClient
                                .DownloadFileAsync(uri, filename)
                                .ConfigureAwait(false);
                            if (!downloaded)
                            {
                                // Could not download. Skip and try again later
                                continue;
                            }
                            file.FilePath = filename;

                            // Update the timestamp of the last time the file changed. Next run, no need to fetch files changed before this timestamp.
                            // The code below only expands the time range.
                            _libState.UpdateDestinationRange(
                                CogniteTime.FromUnixTimeMilliseconds(file.UpdatedTime),
                                CogniteTime.FromUnixTimeMilliseconds(file.UpdatedTime));
                        }
                    }
                    catch (ResponseException e)
                    {
                        // File cannot be downloaded, skip for now and try again later
                        _logger.LogWarning("Failed to fetch file url from CDF: {Message}", e.Message);
                        continue;
                    }
                }

                ProcessDownloadedFiles(token);

                if (_state.Any())
                {
                    var maxUpdatedMs = _state
                        .Select(s => s.Value.UpdatedTime)
                        .Max();
                    var maxUpdatedDt = CogniteTime.FromUnixTimeMilliseconds(maxUpdatedMs);
                    _libState.UpdateDestinationRange(
                        maxUpdatedDt,
                        maxUpdatedDt);
                }

                await Task
                    .Delay(TimeSpan.FromSeconds(_config.StateStoreInterval), token)
                    .ConfigureAwait(false);
            }
        }

        protected abstract void ProcessDownloadedFiles(CancellationToken token);

        /// <summary>
        /// Save periodically the library state (first and last file creation timestamp) and the 
        /// files state (path to the file stored locally)
        /// </summary>
        /// <param name="token">Cancellation token</param>
        private async Task SaveStates(CancellationToken token)
        {
            if (_store == null)
            {
                return;
            }
            while (!token.IsCancellationRequested)
            {
                var waitTask = Task.Delay(TimeSpan.FromSeconds(_config.StateStoreInterval), token);
                var storeTask = StoreLibraryState(token);
                await Task.WhenAll(waitTask, storeTask).ConfigureAwait(false);
            }
        }

        public async Task StoreLibraryState(CancellationToken token)
        {
            if (_store == null)
            {
                return;
            }
            await _store.StoreExtractionState(
                new[] { _libState },
                _config.LibraryTable,
                token).ConfigureAwait(false);
            await _store.StoreExtractionState(
                _state.Values,
                _config.FilesTable,
                (state) => state.GetPoco(),
                token).ConfigureAwait(false);
        }
    }
}
