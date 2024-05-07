using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Serilog.Context;

using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;

using CogniteSdk;
using CogniteSdk.Alpha;
using CogniteSdk.Resources;
using CogniteSdk.Resources.Alpha;
using Cognite.Extensions;
using Cognite.Extractor.Common;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents a local model file library.
    /// It fetches simulator model files from CDF, save a local copy and process the model (extract information).
    /// This library only keeps the latest version of a given model file
    /// </summary>
    /// <typeparam name="T">Type of the state object used in this library</typeparam>
    /// <typeparam name="U">Type of the data object used to serialize and deserialize state</typeparam>
    /// <typeparam name="V">Type of the model parsing information object</typeparam>
    public abstract class ModelLibraryBase<T, U, V> : IModelProvider<T>
        where T : ModelStateBase
        where U : ModelStateBasePoco
        where V : ModelParsingInfo, new()
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Dictionary holding the file states. The keys are the ids of the
        /// CDF simulator model revisions and the values are the state objects of type <typeparamref name="T"/>
        /// Should only be modified from a single thread to avoid multi-threading issues.
        /// </summary>
        public ConcurrentDictionary<string, T> State { get; private set; }

        /// <summary>
        /// Temporary model states that are used once and then deleted after the run.
        /// We keep them in memory to avoid saving them to the state store and hence to avoid the multi-threading issues.
        /// This state is used to store temporary model file paths, used only in case a run event is received before the model file has been downloaded. 
        /// </summary>
        public Dictionary<string, T> TemporaryState { get; private set; }
        
        /// <summary>
        /// CDF files resource. Can be used to read and write files in CDF
        /// </summary>
        protected FilesResource CdfFiles { get; private set; }

        /// <summary>
        /// CDF simulator resource. Can be used to read and write files in CDF
        /// </summary>
        protected SimulatorsResource CdfSimulatorResources { get; private set; }
        
        /// <summary>
        /// Logger object
        /// </summary>
        protected ILogger Logger { get; private set; }

        // Other injected services
        private readonly FileLibraryConfig _config;
        private readonly IList<SimulatorConfig> _simulators;
        private readonly IExtractionStateStore _store;
        private readonly FileStorageClient _downloadClient;

        // Internal objects
        private readonly BaseExtractionState _libState;
        private string _modelFolder;

        
        /// <summary>
        /// Creates a new instance of the library using the provided parameters
        /// </summary>
        /// <param name="config">Library configuration</param>
        /// <param name="simulators">Dictionary of simulators</param>
        /// <param name="cdf">CDF destination object</param>
        /// <param name="logger">Logger</param>
        /// <param name="downloadClient">HTTP client to download files</param>
        /// <param name="store">State store for models state</param>
        public ModelLibraryBase(
            FileLibraryConfig config, 
            IList<SimulatorConfig> simulators, 
            CogniteDestination cdf, 
            ILogger logger, 
            FileStorageClient downloadClient,
            IExtractionStateStore store = null) 
        {
            if (cdf == null)
            {
                throw new ArgumentNullException(nameof(cdf));
            }

            _config = config;
            _simulators = simulators;
            CdfFiles = cdf.CogniteClient.Files;
            CdfSimulatorResources = cdf.CogniteClient.Alpha.Simulators;
            _store = store;
            Logger = logger;
            State = new ConcurrentDictionary<string, T>();
            TemporaryState = new Dictionary<string, T>();
            _libState = new BaseExtractionState(_config.LibraryId);
            _modelFolder = _config.FilesDirectory;
            _downloadClient = downloadClient;

            _logger = logger;
        }

        /// <summary>
        /// Initializes the local model library from the state store (sqlite database)
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

                await FindModelRevisions(false, token).ConfigureAwait(false);

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
        /// Used when model library state lacks a model file needed for the current simulation run.
        /// This method will download the file from CDF, extract the model information and run the simulation.
        /// The file will be deleted after the simulation run, and only kept in TemporaryState dictionary.
        /// </summary>
        private async Task<T> TryReadModelRevisionFromCdf(string modelRevisionExternalId, CancellationToken token)
        {
            try
            {
                var modelRevisionRes = await CdfSimulatorResources.RetrieveSimulatorModelRevisionsAsync(
                    new List<Identity> { new Identity(modelRevisionExternalId) }, token).ConfigureAwait(false);
                var modelRevision = modelRevisionRes.FirstOrDefault();
                var modelRes = await CdfSimulatorResources.RetrieveSimulatorModelsAsync(
                    new List<Identity> { new Identity(modelRevision.ModelExternalId) }, token).ConfigureAwait(false);
                var model = modelRes.FirstOrDefault();
                var state = StateFromModelRevision(modelRevision, model);
                var downloaded = await DownloadFileAsync(state, true).ConfigureAwait(false);
                if (downloaded)
                {
                    await ExtractModelInformationAndPersist(state, token).ConfigureAwait(false);
                    return state;
                }
            }
            catch (Exception e)
            {
                _logger.LogError("Error reading model revision from CDF: {Message}", e.Message);
            }
            return null;
        }

        /// <inheritdoc/>
        public async Task<T> GetModelRevision(string modelRevisionExternalId)
        {
            var modelVersions = State.Values
                .Where(s => s.ExternalId == modelRevisionExternalId
                    && s.IsExtracted
                    && !string.IsNullOrEmpty(s.FilePath))
                .OrderByDescending(s => s.CreatedTime);
            
            var model = modelVersions.FirstOrDefault();

            if (model != null)
            {
                return model;
            }

            return await TryReadModelRevisionFromCdf(modelRevisionExternalId, CancellationToken.None).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public IEnumerable<T> GetAllModelRevisions(string modelExternalId)
        {
            var modelVersions = State.Values
                .Where(s => s.ModelExternalId == modelExternalId
                    && s.Version > 0
                    && !string.IsNullOrEmpty(s.FilePath))
                .OrderByDescending(s => s.CreatedTime);
            return modelVersions;
        }

        /// <summary>
        /// Verify that the model files stored locally have an equivalent model revisions in CDF.
        /// This ensures that model revisions deleted from CDF will also
        /// be removed from the local library
        /// </summary>
        /// <param name="state">Model file state to verify</param>
        /// <param name="token">Cancellation token</param>
        protected async Task VerifyLocalModelState(
            T state,
            CancellationToken token)
        {
            if (state == null)
            {
                return;
            }
            try
            {
                var localRevisions = GetAllModelRevisions(state.ModelExternalId);
                if (localRevisions.Any())
                {
                    var revisionsInCdfRes = await CdfSimulatorResources.ListSimulatorModelRevisionsAsync(
                        new SimulatorModelRevisionQuery
                        {
                            Filter = new SimulatorModelRevisionFilter() {
                                ModelExternalIds = new List<string> { state.ModelExternalId }
                            }
                        }, token).ConfigureAwait(false);
                    
                    var revisionsInCdf = revisionsInCdfRes.Items.ToDictionarySafe(r => r.Id.ToString(), r => r);

                    var statesToDelete = new List<T>();
                    foreach (var revision in localRevisions)
                    {
                        if (!revisionsInCdf.ContainsKey(revision.Id))
                        {
                            if (State.TryRemove(revision.Id, out _))
                            {
                                statesToDelete.Add(revision);
                            }
                        }
                    }
                    var filesInUseMap = State.Values
                        .Where(f => !string.IsNullOrEmpty(f.FilePath))
                        .ToDictionarySafe(f => f.FilePath, f => true);
                    if (statesToDelete.Any())
                    {
                        Logger.LogWarning("Removing {Num} model versions not found in CDF: {Versions}",
                            statesToDelete.Count,
                            string.Join(", ", statesToDelete.Select(s => s.ModelName + " v" + s.Version)));

                        var statesToDeleteWithFile = statesToDelete
                            .Select(s => (s, !filesInUseMap.ContainsKey(s.FilePath)));

                        await _store
                            .RemoveFileStates(_config.FilesTable, statesToDeleteWithFile, token)
                            .ConfigureAwait(false);
                    }
                }   
            }
            catch (System.Exception e)
            {
                Logger.LogError("Error verifying local model state: {Message}", e.Message);
                return;
            }
            
        }

        private async Task ExtractModelInformationAndPersist(T modelState, CancellationToken token)
        {
            InitModelParsingInfo(modelState);
            var logId = modelState.LogId;
            using (LogContext.PushProperty("LogId", logId)) {
                try
                {
                    _logger.LogInformation("Extracting model information for {ModelName} v{Version}", modelState.ModelName, modelState.Version);
                    await ExtractModelInformation(modelState, token).ConfigureAwait(false);
                }
                finally
                {
                    await PersistModelStatus(modelState, token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// This method find all model versions that have not been processed and calls
        /// the <see cref="ExtractModelInformation(T, CancellationToken)"/> method 
        /// to process the models.  
        /// </summary>
        /// <param name="token">Cancellation token</param>
        private async Task ProcessDownloadedFiles(CancellationToken token)
        {
            // Find all model files for which we need to extract data
            // The models are grouped by (model external id)
            var modelGroups = State.Values
                .Where(f => !string.IsNullOrEmpty(f.FilePath) && !f.IsExtracted && f.CanRead)
                .GroupBy(f => new { f.ModelExternalId });
            foreach (var group in modelGroups)
            {
                // Extract the data for each model file (version) in this group
                foreach (var item in group){
                    await ExtractModelInformationAndPersist(item, token).ConfigureAwait(false);
                }

                // Verify that the local version history matches the one in CDF. Else,
                // delete the local state and files for the missing versions.
                await VerifyLocalModelState(group.First(), token).ConfigureAwait(false);
            }
        }

        private async Task PersistModelStatus(T modelState, CancellationToken token)
        {
            if (modelState.ParsingInfo != null && modelState.ParsingInfo.Status != ParsingStatus.ready)
            {
                var newStatus = SimulatorModelRevisionStatus.unknown;
                switch (modelState.ParsingInfo.Status)
                {
                    case ParsingStatus.success:
                        newStatus = SimulatorModelRevisionStatus.success;
                        break;
                    
                    case ParsingStatus.failure:
                        newStatus = SimulatorModelRevisionStatus.failure;
                        break;
                }

                var modelRevisionPatch =
                    new SimulatorModelRevisionUpdateItem(long.Parse(modelState.Id)) {
                        Update =
                            new SimulatorModelRevisionUpdate {
                                Status = new Update<SimulatorModelRevisionStatus>(newStatus),
                            }
                    };

                await CdfSimulatorResources.UpdateSimulatorModelRevisionsAsync(new [] { modelRevisionPatch }, token).ConfigureAwait(false);
            }
        }

        private void InitModelParsingInfo(T modelState)
        {
            V info = new V(){
                Parsed = false,
                ModelName = modelState.Model.Name,
                Simulator = modelState.Model.Simulator,
                ModelVersion = modelState.Version,
                Status = ParsingStatus.ready,
            };
            modelState.ParsingInfo = info;
            modelState.LogId = modelState.LogId;
        }

        /// <summary>
        /// This method should open the model versions in the simulator, extract the required information and
        /// ingest it to CDF. 
        /// </summary>
        /// <param name="modelState">Model file states</param>
        /// <param name="token">Cancellation token</param>
        protected abstract Task ExtractModelInformation(
            T modelState,
            CancellationToken token);

        /// <summary>
        /// Creates a state object of type <typeparamref name="T"/> from a
        /// CDF Simulator model revision passed as parameter
        /// </summary>
        /// <param name="modelRevision">CDF Simulator model revision</param>
        /// <param name="model">CDF Simulator model</param>
        /// <returns>File state object</returns>
        protected abstract T StateFromModelRevision(SimulatorModelRevision modelRevision, SimulatorModel model);

        /// <summary>
        /// Add a single model revision to the local state store.
        /// Returns the existing state object if the revision already exists in the store.
        /// </summary>
        /// <param name="modelRevision">Model revision to add</param>
        /// <param name="model">Model associated to the revision</param>
        /// <returns>State object for the model revision. Null if the model revision is invalid</returns>
        public T AddModelRevisionToState(
            SimulatorModelRevision modelRevision,
            SimulatorModel model)
        {
            T rState = StateFromModelRevision(modelRevision, model);
            if (rState == null || modelRevision == null)
            {
                return null;
            }
            var revisionId = modelRevision.Id.ToString();
            return State.GetOrAdd(revisionId, rState);
        }

        /// <summary>
        /// Find file ids of model revisions that have been created after the latest timestamp in the local store
        /// Build a local state to keep track of what files exist and which ones have
        /// been downloaded.
        /// </summary>
        /// <param name="onlyLatest">Fetch only the latest model revisions</param>
        /// <param name="token">Cancellation token</param>
        private async Task FindModelRevisions(
            bool onlyLatest,
            CancellationToken token)
        {
            try {
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

                if (modelExternalIds.Count == 0)
                {
                    return;
                }

                var modelRevisionsAllRes = await CdfSimulatorResources
                    .ListSimulatorModelRevisionsAsync(
                        new SimulatorModelRevisionQuery() {
                            Filter = new SimulatorModelRevisionFilter() {
                                CreatedTime = new CogniteSdk.TimeRange() {  Min = createdAfter + 1 },
                            },
                            Sort = new List<SimulatorSortItem>() {
                                new SimulatorSortItem() {
                                    Order = SimulatorSortOrder.desc,
                                    Property = "createdTime"
                                }
                            }
                        }, token
                    ).ConfigureAwait(false);
                var modelRevisionsRes = modelRevisionsAllRes.Items
                    .Where(r => modelExternalIds.Contains(r.ModelExternalId))
                    .ToList();

                foreach (var revision in modelRevisionsRes) {
                    var model = modelsMap[revision.ModelExternalId];
                    AddModelRevisionToState(revision, model);
                }
            }
            catch (System.Exception e)
            {
                Logger.LogDebug("Failed to fetch model revisions from CDF: {Message}", e.Message);
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
        /// Wipes all temporary model files stored in memory
        /// </summary>
        public void WipeTemporaryModelFiles()
        {
            foreach (var state in TemporaryState.Values)
            {
                StateUtils.DeleteFileAndDirectory(state.FilePath, state.IsInDirectory);
            }
            TemporaryState.Clear();
        }
        
        /// <summary>
        /// Downloads a file from CDF and stores it locally
        /// </summary>
        /// <param name="modelState">State object representing the file to download</param>
        /// <param name="isTemporary">True if the file is temporary and should be deleted after use.
        /// Such files are used once to run a simulation with a model that is not available in the state upon at a give time.</param>
        /// <returns>True if the file was downloaded successfully, false otherwise</returns>
        public async Task<bool> DownloadFileAsync(T modelState, bool isTemporary = false)
        {
            if (modelState == null)
            {
                throw new ArgumentNullException(nameof(modelState));
            }
            var fileId = new Identity(modelState.CdfId);
            Logger.LogInformation("Downloading file: {Id}. Model external id: {ModelExternalId}, model revision external id: {ExternalId}",
                modelState.CdfId,
                modelState.ModelExternalId,
                modelState.ExternalId);

            try {
                var response = await CdfFiles
                    .DownloadAsync(new[] { fileId })
                    .ConfigureAwait(false);
                if (response.Any() && response.First().DownloadUrl != null)
                {
                    var uri = response.First().DownloadUrl;

                    string filename;
                                                
                    var modelFolder = _modelFolder;
                    if (isTemporary) // Temporary files are stored in a different folder
                    {
                        modelFolder = Path.Combine(modelFolder, "temp");
                        TemporaryState[modelState.Id] = modelState;
                    }
                    var storageFolder = Path.Combine(modelFolder, $"{modelState.CdfId}");
                    CreateDirectoryIfNotExists(storageFolder);
                    filename = Path.Combine(storageFolder, $"{modelState.CdfId}.{modelState.GetExtension()}");
                    modelState.IsInDirectory = true;
                    
                    bool downloaded = await _downloadClient
                        .DownloadFileAsync(uri, filename)
                        .ConfigureAwait(false);
                    if (downloaded)
                    {
                        modelState.FilePath = filename;
                        return true;
                    }
                }
            } catch (ResponseException e) {
                // File cannot be downloaded, skip for now and try again later
                Logger.LogWarning("Failed to fetch file url from CDF: {Message}", e.Message);
            }
            return false;
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
                Logger.LogInformation("Updating file library. There are currently {Num} files. Extracted range: {TimeRange}",
                    State.Count,
                    timeRange
                    );

                // Find new model files in CDF and add the to the local state.
                await FindModelRevisions(true, token)
                    .ConfigureAwait(false);

                // Find the files that are not yet saved locally (no file path)
                var files = State.Values
                    .Where(f => string.IsNullOrEmpty(f.FilePath))
                    .OrderBy(f => f.UpdatedTime)
                    .ToList();

                foreach (var file in files)
                {
                    var downloaded = await DownloadFileAsync(file).ConfigureAwait(false);
                    if (downloaded) {
                        _libState.UpdateDestinationRange(
                            CogniteTime.FromUnixTimeMilliseconds(file.UpdatedTime),
                            CogniteTime.FromUnixTimeMilliseconds(file.UpdatedTime));
                    }
                }

                ProcessDownloadedFiles(token).Wait(token);

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

    /// <summary>
    /// Interface for libraries that can provide model information
    /// </summary>
    /// <typeparam name="T">Model state type</typeparam>
    public interface IModelProvider<T>
    {
        /// <summary>
        /// Returns the state object of the given version of the given model
        /// </summary>
        /// <param name="modelRevisionExternalId">Model revision external id</param>
        /// <returns>State object</returns>
        Task<T> GetModelRevision(string modelRevisionExternalId);

        /// <summary>
        /// Returns the state objects of all the versions of the given model
        /// </summary>
        /// <param name="modelExternalId">Model external id</param>
        /// <returns>List of state objects</returns>
        IEnumerable<T> GetAllModelRevisions(string modelExternalId);

        /// <summary>
        /// Delete all temporary model files stored in memory and on disk
        /// </summary>
        void WipeTemporaryModelFiles();
    }
}
