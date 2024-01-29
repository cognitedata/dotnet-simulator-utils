using Cognite.Extractor.Common;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents a local file library. The library can be configured to fetch files
    /// associated to a simulator and stored in a CDF dataset (see <seealso cref="SimulatorConfig"/>).
    /// The files are stored locally at the folder specified in the library configuration (<seealso cref="FileLibraryConfig"/>).
    /// Files are kept in sync with CDF, so that updates in the remote files are reflected on the local files.
    /// </summary>
    /// <typeparam name="T">Type of the state object used in this library</typeparam>
    /// <typeparam name="U">Type of the data object used to serialize and deserialize state</typeparam>
    public abstract class FileLibrary<T, U>
        where T : FileState
        where U : FileStatePoco
    {
        /// <summary>
        /// Dictionary holding the file states. The keys are the external ids of the
        /// CDF files and the values are the state objects of type <typeparamref name="T"/>
        /// </summary>
        public Dictionary<string, T> State { get; private set; }

        /// <summary>
        /// Object holding the CDF client. Can be used to read and write data to various
        /// CDF resources
        /// </summary>
        protected CogniteDestination Cdf { get; private set; }
        
        /// <summary>
        /// CDF files resource. Can be used to read and write files in CDF
        /// </summary>
        protected CogniteSdk.Resources.FilesResource CdfFiles { get; private set; }

        /// <summary>
        /// CDF files resource. Can be used to read and write files in CDF
        /// </summary>
        protected CogniteSdk.Resources.Alpha.SimulatorsResource CdfSimulatorResources { get; private set; }
        
        /// <summary>
        /// Logger object
        /// </summary>
        protected ILogger Logger { get; private set; }

        // Other injected services
        private readonly FileLibraryConfig _config;
        private readonly IList<SimulatorConfig> _simulators;
        private readonly IExtractionStateStore _store;
        private readonly FileDownloadClient _downloadClient;

        // Internal objects
        private readonly BaseExtractionState _libState;
        private readonly SimulatorDataType _resourceType;
        private string _modelFolder;

        /// <summary>
        /// Creates a new instance of this library using the provided parameters.
        /// These parameters are intended to be injected by using a <see cref="IServiceCollection"/>
        /// </summary>
        /// <param name="resourceType">Type of simulator file</param>
        /// <param name="config">Library configuration</param>
        /// <param name="simulators">Dictionary of simulators</param>
        /// <param name="cdf">CDF destination object</param>
        /// <param name="logger">Logger</param>
        /// <param name="downloadClient">HTTP client to download files</param>
        /// <param name="store">State store for files state</param>
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
            Cdf = cdf;
            CdfFiles = Cdf.CogniteClient.Files;
            CdfSimulatorResources = Cdf.CogniteClient.Alpha.Simulators;
            _store = store;
            Logger = logger;
            State = new Dictionary<string, T>();
            _libState = new BaseExtractionState(_config.LibraryId);
            _modelFolder = _config.FilesDirectory;
            _resourceType = resourceType;
            _downloadClient = downloadClient;
        }
        
        /// <summary>
        /// Initializes the local file library. Creates the folder, find the files in CDF and restore state.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Init(CancellationToken token)
        {
            Logger.LogDebug("Ensuring directory to store files exists: {Path}", _modelFolder);
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
                    State,
                    _config.FilesTable,
                    (state, poco) =>
                    {
                        state.Init(poco);
                    },
                    token).ConfigureAwait(false);
                if (_store is LiteDBStateStore ldbStore)
                {
                    HashSet<string> idsToKeep = new HashSet<string>(State.Select(s => s.Value.Id));
                    ldbStore.RemoveUnusedState(_config.FilesTable, idsToKeep);
                }
            }
            Logger.LogInformation("Local state store {Table} initiated. Tracking {Num} files", _config.FilesTable, State.Count);
        }

        /// <summary>
        /// Remove the provided file states from the state store
        /// </summary>
        /// <param name="states">States to remove</param>
        /// <param name="token">Cancellation token</param>
        protected async Task RemoveStates(
            IEnumerable<T> states,
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

        /// <summary>
        /// Creates a state object of type <typeparamref name="T"/> from a
        /// CDF file passed as parameter
        /// </summary>
        /// <returns>File state object</returns>
        protected abstract T StateFromRoutineRevision(CogniteSdk.Alpha.SimulatorRoutineRevision routineRevision, CogniteSdk.Alpha.SimulatorRoutine routine);

        /// <summary>
        /// Creates a state object of type <typeparamref name="T"/> from a
        /// CDF Simulator model revision passed as parameter
        /// </summary>
        /// <param name="modelRevision">CDF Simulator model revision</param>
        /// <param name="model">CDF Simulator model</param>
        /// <returns>File state object</returns>
        protected abstract T StateFromModelRevision(SimulatorModelRevision modelRevision, CogniteSdk.Alpha.SimulatorModel model);

        /// <summary>
        /// Find file ids of model revisions that have been created after the latest timestamp in the local store
        /// Build a local state to keep track of what files exist and which ones have
        /// been downloaded.
        /// </summary>
        private async Task FindFilesByRevisions(
            bool onlyLatest,
            CancellationToken token)
        {
            long createdAfter = 
                onlyLatest && !_libState.DestinationExtractedRange.IsEmpty ?
                    _libState.DestinationExtractedRange.Last.ToUnixTimeMilliseconds() : 0;

            var simulatorsExternalIds = _simulators.Select(s => s.Name).ToList();

            var modelsRes = await CdfSimulatorResources
                .ListSimulatorModelsAsync(new SimulatorModelQuery() {
                    Filter = new SimulatorModelFilter() {
                        SimulatorExternalIds = simulatorsExternalIds
                    }
                }, token).ConfigureAwait(false);

            var modelsMap = modelsRes.Items.ToDictionary(m => m.ExternalId, m => m);

            var modelExternalIds = modelsRes.Items.Select(m => m.ExternalId).ToList();

            var modelRevisionsRes = await CdfSimulatorResources
                .ListSimulatorModelRevisionsAsync(
                    new SimulatorModelRevisionQuery() {
                        Filter = new SimulatorModelRevisionFilter() {
                            CreatedTime = new CogniteSdk.TimeRange() {  Min = createdAfter + 1 },
                            ModelExternalIds = modelExternalIds,  
                        }
                    },
                    token
                ).ConfigureAwait(false);

            foreach (var revision in modelRevisionsRes.Items) {
                var model = modelsMap[revision.ModelExternalId];
                T rState = StateFromModelRevision(revision, model);
                if (rState == null)
                {
                    continue;
                }
                var revisionId = revision.Id.ToString();
                if (!State.ContainsKey(revisionId))
                {
                    // If the revision does not exist locally, add it to the state store
                    State.Add(revisionId, rState);
                }
            }
        }

        // /// <summary>
        // /// Deprecated: based on files API.
        // /// Used only for calculations now.
        // /// </summary>
        // private async Task FindFilesByMetadata(
        //     bool onlyLatest,
        //     CancellationToken token)
        // {
        //     DateTime? updatedAfter = null;
        //     if (onlyLatest && !_libState.DestinationExtractedRange.IsEmpty)
        //     {
        //         updatedAfter = _libState.DestinationExtractedRange.Last;
        //     }
        //     var files = await CdfFiles.FindSimulatorFiles(
        //         _resourceType,
        //         _simulators.ToDictionary(s => s.Name, s => (long?)s.DataSetId),
        //         updatedAfter,
        //         token).ConfigureAwait(false);

        //     foreach (var file in files)
        //     {
        //         T fState = StateFromFile(file);
        //         if (fState == null)
        //         {
        //             continue;
        //         }
        //         if (!State.ContainsKey(file.ExternalId))
        //         {
        //             // If the file does not exist locally, add it to the state store
        //             State.Add(file.ExternalId, fState);
        //         }
        //         else if (State[fState.Id].UpdatedTime < fState.UpdatedTime)
        //         {
        //             // If the file exists in the state store but was updated in CDF, use the new file instead
        //             await _store.RemoveFileStates(
        //                 _config.FilesTable,
        //                 new List<FileState> { State[fState.Id] },
        //                 token).ConfigureAwait(false);
        //             State[fState.Id] = fState;
        //         }
        //     }
        // }

        /// <summary>
        /// Fetch the Files from CDF for the configured simulators and datasets.
        /// Build a local state to keep track of what files exist and which ones have 
        /// been downloaded.
        /// </summary>
        /// <param name="onlyLatest">Fetch only the files updated after the latest timestamp in the local store</param>
        /// <param name="token">Cancellation token</param>
        private async Task FindFiles(
            bool onlyLatest,
            CancellationToken token)
        {
            if (_resourceType == SimulatorDataType.ModelFile) {
                // Use the simulator model revisions API
                await FindFilesByRevisions(onlyLatest, token).ConfigureAwait(false);
            } else {
                
                // throw new Exception("Only model files are supported");
                // await FindFilesByMetadata(onlyLatest, token).ConfigureAwait(false);
            }
            
        }

        /// <summary>
        /// Creates a list of the tasks performed by this library.
        /// These include searching and downloading files ans saving state.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of tasks</returns>
        public IEnumerable<Task> GetRunTasks(CancellationToken token)
        {
            return new List<Task> { SaveStates(token), SearchAndDownloadFiles(token) };
        }

        private void CreateDirectoryIfNotExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
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
                Logger.LogDebug("Updating file library. There are currently {Num} files. Extracted range: {TimeRange}",
                    State.Count,
                    timeRange
                    );

                // Find new model files in CDF and add the to the local state.
                await FindFiles(true, token)
                    .ConfigureAwait(false);

                // Find the files that are not yet saved locally (no file path)
                var files = State.Values
                    .Where(f => string.IsNullOrEmpty(f.FilePath))
                    .OrderBy(f => f.UpdatedTime)
                    .ToList();

                foreach (var file in files)
                {
                    // Get the download URL for the file. Could fetch more than one per request, but the 
                    // URL expires after 30 seconds. Best to do one by one.
                    Logger.LogInformation("Downloading file: {Id}. Created on {CreatedTime}. Updated on {UpdatedTime}",
                        file.CdfId,
                        CogniteTime.FromUnixTimeMilliseconds(file.CreatedTime).ToISOString(),
                        CogniteTime.FromUnixTimeMilliseconds(file.UpdatedTime).ToISOString());
                    try
                    {   
                        if (_resourceType != SimulatorDataType.ModelFile) {
                            continue; // TODO this is handled by routines now, we don't need to download files
                            // TODO: this method shouldn't even run for routines (?)
                        }
                        var fileId = new Identity(file.CdfId);
                        var response = await CdfFiles
                            .DownloadAsync(new[] { fileId }, token)
                            .ConfigureAwait(false);
                        if (response.Any() && response.First().DownloadUrl != null)
                        {
                            var uri = response.First().DownloadUrl;

                            var filename = "";    
                                                 
                            if (file.GetExtension() == "json")
                            {
                                filename = Path.Combine(_modelFolder, $"{file.CdfId}.{file.GetExtension()}");
                            } else {
                                var storageFolder = Path.Combine(_modelFolder, $"{file.CdfId}");
                                CreateDirectoryIfNotExists(storageFolder);
                                filename = Path.Combine(  storageFolder, $"{file.CdfId}.{file.GetExtension()}");
                                file.IsInDirectory = true;
                            }

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
                        Logger.LogWarning("Failed to fetch file url from CDF: {Message}", e.Message);
                        continue;
                    }
                }

                ProcessDownloadedFiles(token);

                if (State.Any())
                {
                    var maxUpdatedMs = State
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

        /// <summary>
        /// Process files that have been downloaded. This method should open 
        /// the file in a simulator, verify that it is valid, extract relevant data
        /// and ingest it to CDF. 
        /// </summary>
        /// <param name="token">Cancellation token</param>
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

        /// <summary>
        /// Persists the library state from memory to the store
        /// </summary>
        /// <param name="token">Cancellation token</param>
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
                State.Values,
                _config.FilesTable,
                (state) => state.GetPoco(),
                token).ConfigureAwait(false);
        }
    }
}
