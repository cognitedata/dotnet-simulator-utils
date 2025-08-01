using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extensions;
using Cognite.Extractor.Common;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk;
using CogniteSdk.Alpha;
using CogniteSdk.Resources;
using CogniteSdk.Resources.Alpha;

using Microsoft.Extensions.Logging;

using Serilog.Context;

using static Cognite.Simulator.Utils.SimulatorLoggingUtils;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents a local model file library.
    /// It fetches simulator model files from CDF, save a local copy and process the model (extract information).
    /// This library only keeps the latest version of a given model file
    /// </summary>
    /// <typeparam name="A">Type of the automation configuration object</typeparam>
    /// <typeparam name="T">Type of the state object used in this library</typeparam>
    /// <typeparam name="U">Type of the data object used to serialize and deserialize state</typeparam>
    /// <typeparam name="V">Type of the model parsing information object</typeparam>
    public abstract class ModelLibraryBase<A, T, U, V> : IModelProvider<A, T>, IDisposable
        where A : AutomationConfig
        where T : ModelStateBase
        where U : ModelStateBasePoco
        where V : ModelParsingInfo, new()
    {
        /// <summary>
        /// Dictionary holding the file states. The keys are the ids of the
        /// CDF simulator model revisions and the values are the state objects of type <typeparamref name="T"/>
        /// Should only be modified from a single thread to avoid multi-threading issues.
        /// </summary>
        public ConcurrentDictionary<string, T> _state { get; private set; }


        /// <summary>
        /// CDF files resource. Can be used to read and write files in CDF
        /// </summary>
        protected FilesResource _cdfFiles { get; private set; }

        /// <summary>
        /// CDF simulator resource. Can be used to read and write files in CDF
        /// </summary>
        protected SimulatorsResource _cdfSimulatorResources { get; private set; }

        /// <summary>
        /// Logger object
        /// </summary>
        protected ILogger _logger { get; private set; }

        // Other injected services
        private readonly ModelLibraryConfig _config;
        private readonly SimulatorCreate _simulatorDefinition;
        private readonly IExtractionStateStore _store;
        private readonly FileStorageClient _downloadClient;

        /// <summary>
        /// This manages the tasks for processing model revisions. It ensures:
        /// 1. Only one task executes at a time across all model revisions (serialized execution)
        /// 2. Concurrent requests for the same model revision return the same task result (deduplication)
        /// 3. Proper cleanup of resources after task completion or failure
        /// 4. Cancellation propagation to ongoing tasks
        /// </summary>
        /// <remarks>
        /// The task holder maintains a semaphore to serialize execution and a dictionary of ongoing tasks.
        /// When a model revision is requested, if a task is already processing that revision, subsequent
        /// callers will receive the result of the ongoing task rather than starting a new one.
        /// </remarks>
        private readonly ModelLibraryTaskHolder<string, T> _revisionsTasks = new();

        // Internal objects
        private readonly BaseExtractionState _libState;
        private string _modelFolder;


        /// <summary>
        /// Creates a new instance of the library using the provided parameters
        /// </summary>
        /// <param name="config">Library configuration</param>
        /// <param name="simulatorDefinition">Simulator definition</param>
        /// <param name="cdf">CDF destination object</param>
        /// <param name="logger">Logger</param>
        /// <param name="downloadClient">HTTP client to download files</param>
        /// <param name="store">State store for models state</param>
        public ModelLibraryBase(
            ModelLibraryConfig config,
            SimulatorCreate simulatorDefinition,
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
            _simulatorDefinition = simulatorDefinition;
            _cdfFiles = cdf.CogniteClient.Files;
            _cdfSimulatorResources = cdf.CogniteClient.Alpha.Simulators;
            _store = store;
            _logger = logger;
            _state = new ConcurrentDictionary<string, T>();
            _libState = new BaseExtractionState(_config.LibraryId);
            _modelFolder = _config.FilesDirectory;
            _downloadClient = downloadClient;
        }

        private static void CopyNonBaseProperties(T source, T target)
        {
            Type type = typeof(T);
            Type baseType = type.BaseType;

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead && property.CanWrite && property.DeclaringType != baseType)
                {
                    var value = property.GetValue(source);
                    property.SetValue(target, value);
                }
            }
        }

        private T GetOrUpdateState(string key, T inputValue)
        {
            var updatedValue = _state.AddOrUpdate(
                key,
                inputValue, // Value to add if key does not exist
                (existingKey, existingValue) =>
                {
                    // Copy non-base properties from existingValue to inputValue.
                    // This ensures that any properties saved by an extension of the ModelStateBase class
                    // are set back into the state.
                    CopyNonBaseProperties(existingValue, inputValue);
                    return inputValue;
                } // Value to update if key exists
            );

            return updatedValue;
        }

        /// <summary>
        /// Initializes the local model library from the state store (sqlite database)
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

                await FindModelRevisions(false, token).ConfigureAwait(false);

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

        /// <summary>
        /// Used when an operation (model parsing, simulation, etc.) needs to be performed on the model.
        /// This method will try to read the model revision from CDF and return it.
        /// </summary>
        private async Task<SimulatorModelRevision> TryReadRemoteModelRevision(string modelRevisionExternalId, CancellationToken token)
        {
            try
            {
                var modelRevisionRes = await _cdfSimulatorResources.RetrieveSimulatorModelRevisionsAsync(
                    new List<Identity> { new Identity(modelRevisionExternalId) }, token).ConfigureAwait(false);
                var modelRevision = modelRevisionRes.FirstOrDefault();
                return modelRevision;
            }
            catch (Exception e)
            {
                _logger.LogError("Error reading model revision from CDF: {Message}", e.Message);
            }
            return null;
        }

        /// <summary>
        /// This may do multiple steps depending on the local vs remote state of the model revision:
        /// 1. Will try to read the model revision from CDF.
        /// 2.  - If model revision is not available locally or has been changed since the last check check it will download the file and extract the model information.
        ///     - If the model revision is already in the local state and has not been changed, it will return the existing state.
        /// </summary>
        /// <param name="modelRevisionExternalId">Model revision External ID</param>
        /// <param name="token">Cancellation token</param>
        private async Task<T> GetOrAddModelRevisionImpl(string modelRevisionExternalId, CancellationToken token = default)
        {
            var modelRevision = await TryReadRemoteModelRevision(modelRevisionExternalId, token).ConfigureAwait(false);

            if (modelRevision == null)
            {
                _logger.LogError("Model revision {ModelRevisionExternalId} not found in CDF", modelRevisionExternalId);
                return null;
            }

            _state.TryGetValue(modelRevision.Id.ToString(), out var modelState);

            if (modelState == null)
            {
                _logger.LogDebug("Model revision not found locally, adding to the local state: {ModelRevisionExternalId}", modelRevisionExternalId);
                modelState = UpsertModelRevisionInState(modelRevision);
            }

            if (modelState == null)
            {
                _logger.LogError("Failed to get model state for {ModelRevisionExternalId}", modelRevisionExternalId);
                return null;
            }

            UpdateModelParsingInfo(modelState, modelRevision);

            var downloaded = modelState.Downloaded;

            if (!downloaded && modelState.DownloadAttempts < _config.MaxDownloadAttempts)
            {
                downloaded = await DownloadAllFilesForModelAsync(modelState).ConfigureAwait(false);
            }

            if (downloaded && modelState.ShouldProcess())
            {
                await ExtractModelInformationAndPersist(modelState, token).ConfigureAwait(false);
            }

            return modelState;
        }

        /// <inheritdoc/>
        public Task<T> GetModelRevision(string modelRevisionExternalId, CancellationToken token = default)
        {
            return GetOrAddModelRevision(modelRevisionExternalId, token);
        }

        /// <summary>
        /// This method gives a safe way to get a processed model revision.
        /// Internally, it uses a <see cref="ModelLibraryTaskHolder{TKey,TValue}"/> to ensure that only one thread processes a given model revision at a time.
        /// </summary>
        /// <param name="modelRevisionExternalId">Model revision External ID</param>
        /// <param name="token">Cancellation token</param>
        private Task<T> GetOrAddModelRevision(string modelRevisionExternalId, CancellationToken token = default)
        {
            return _revisionsTasks.ExecuteAsync(modelRevisionExternalId, (cancellationToken) => GetOrAddModelRevisionImpl(modelRevisionExternalId, cancellationToken), token);
        }

        /// <inheritdoc/>
        private IEnumerable<T> GetAllModelRevisions(string modelExternalId)
        {
            var modelVersions = _state.Values
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
        private async Task VerifyLocalModelState(
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
                    var revisionsInCdfRes = await _cdfSimulatorResources.ListSimulatorModelRevisionsAsync(
                        new SimulatorModelRevisionQuery
                        {
                            Filter = new SimulatorModelRevisionFilter()
                            {
                                ModelExternalIds = new List<string> { state.ModelExternalId }
                            }
                        }, token).ConfigureAwait(false);

                    var revisionsInCdf = revisionsInCdfRes.Items.ToDictionarySafe(r => r.Id.ToString(), r => r);

                    var statesToDelete = new List<T>();
                    foreach (var revision in localRevisions)
                    {
                        if (!revisionsInCdf.ContainsKey(revision.Id))
                        {
                            if (_state.TryRemove(revision.Id, out _))
                            {
                                statesToDelete.Add(revision);
                            }
                        }
                    }
                    var filesInUseMap = _state.Values
                        .Where(f => !string.IsNullOrEmpty(f.FilePath))
                        .ToDictionarySafe(f => f.FilePath, f => true);
                    if (statesToDelete.Count != 0)
                    {
                        _logger.LogWarning("Removing {Num} model versions not found in CDF: {Versions}",
                            statesToDelete.Count,
                            string.Join(", ", statesToDelete.Select(s => s.ModelExternalId + " v" + s.Version)));

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
                _logger.LogError("Error verifying local model state: {Message}", e.Message);
                return;
            }

        }

        private async Task ExtractModelInformationAndPersist(T modelState, CancellationToken token)
        {
            if (modelState.ShouldProcess())
            {
                var logId = modelState.LogId;
                using (LogContext.Push(await GetLogEnrichers(_cdfSimulatorResources, logId).ConfigureAwait(false)))
                {
                    try
                    {
                        _logger.LogInformation("Extracting model information for {ModelExtid} v{Version}", modelState.ModelExternalId, modelState.Version);
                        await ExtractModelInformation(modelState, token).ConfigureAwait(false);
                        await PersistModelInformation(modelState, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        await PersistModelStatus(modelState, token).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task PersistModelStatus(T modelState, CancellationToken token)
        {
            if (modelState.ParsingInfo != null && modelState.ParsingInfo.Status != SimulatorModelRevisionStatus.unknown)
            {
                var newStatus = modelState.ParsingInfo.Status;

                await _cdfSimulatorResources.UpdateSimulatorModelRevisionParsingStatus(
                    long.Parse(modelState.Id),
                    newStatus,
                    modelState.ParsingInfo.StatusMessage,
                    token).ConfigureAwait(false);
            }
        }

        private void UpdateModelParsingInfo(T modelState, SimulatorModelRevision modelRevision)
        {
            var status = modelRevision.Status;
            var needsUpdate = modelState.UpdatedTime < modelRevision.LastUpdatedTime;
            if (needsUpdate && status == SimulatorModelRevisionStatus.unknown)
            {
                _logger.LogDebug("Resetting download attempts counter for {ModelExtid} due to unknown status", modelState.ModelExternalId);
                modelState.DownloadAttempts = 0;
            }
            if (needsUpdate || modelState.ParsingInfo == null)
            {
                var isError = status == SimulatorModelRevisionStatus.failure;
                var parsed = isError || status == SimulatorModelRevisionStatus.success;
                V info = new V()
                {
                    Parsed = parsed,
                    Status = status,
                    Error = isError,
                };
                modelState.ParsingInfo = info;
                modelState.CanRead = !isError; // when model parsing info is updated, this allows to read the model once again
            }
        }

        private async Task PersistModelInformation(T modelState, CancellationToken token)
        {
            if (modelState.ParsingInfo != null &&
            (modelState.ParsingInfo.Flowsheet != null ||
            modelState.ParsingInfo.RevisionDataInfo != null))
            {
                try
                {
                    await _cdfSimulatorResources.UpdateSimulatorModelRevisionData(
                        modelState.ExternalId,
                        modelState.ParsingInfo.Flowsheet,
                        modelState.ParsingInfo.RevisionDataInfo,
                        token
                    ).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger.LogError("Error persisting model information: {Message}", e.Message);
                }
            }
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
        /// <returns>File state object</returns>
        protected abstract T StateFromModelRevision(SimulatorModelRevision modelRevision);

        /// <summary>
        /// Add a single model revision to the local state store.
        /// Updates model parsing info if the model revision is already in the state.
        /// Returns the existing state object if the model revision is already in the state.
        /// </summary>
        /// <param name="modelRevision">Model revision to add</param>
        /// <returns>State object for the model revision. Null if the model revision is invalid</returns>
        private T UpsertModelRevisionInState(
            SimulatorModelRevision modelRevision)
        {
            T newState = StateFromModelRevision(modelRevision);
            if (newState == null || modelRevision == null)
            {
                return null;
            }
            var revisionId = modelRevision.Id.ToString();
            var state = GetOrUpdateState(revisionId, newState);
            return state;
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
            try
            {

                long updatedAfter =
                    onlyLatest && !_libState.DestinationExtractedRange.IsEmpty ?
                        _libState.DestinationExtractedRange.Last.ToUnixTimeMilliseconds() : 0;

                var modelRevisionsAllRes = await _cdfSimulatorResources
                    .ListSimulatorModelRevisionsAsync(
                        new SimulatorModelRevisionQuery()
                        {
                            Filter = new SimulatorModelRevisionFilter()
                            {
                                LastUpdatedTime = new CogniteSdk.TimeRange() { Min = updatedAfter + 1 },
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
                    .Where(r => _simulatorDefinition.ExternalId == r.SimulatorExternalId)
                    .ToList();

                foreach (var revision in modelRevisionsRes)
                {
                    var state = await GetOrAddModelRevision(revision.ExternalId, token).ConfigureAwait(false);
                    if (state != null && state.Downloaded)
                    {
                        _libState.UpdateDestinationRange(
                            CogniteTime.FromUnixTimeMilliseconds(state.UpdatedTime),
                            CogniteTime.FromUnixTimeMilliseconds(state.UpdatedTime));
                    }
                }
                // TODO: this logic has to reviewed, seems like we aren't doing this correctly/efficiently
                // JIRA: https://cognitedata.atlassian.net/jira/software/c/projects/POFSP/boards/852/backlog?selectedIssue=POFSP-914
                var modelGroupsToVerify = _state.Values
                    .Where(f => f.Downloaded && !f.IsExtracted && f.CanRead)
                    .GroupBy(f => new { f.ModelExternalId });
                foreach (var group in modelGroupsToVerify)
                {
                    await VerifyLocalModelState(group.First(), token).ConfigureAwait(false);
                }
            }
            catch (ResponseException e)
            {
                _logger.LogWarning("Failed to fetch model revisions from CDF: {Message}", e.Message);
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
            return new List<Task> { SearchAndDownloadFiles(token) };
        }

        private static void CreateDirectoryIfNotExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        /// <summary>
        /// Downloads all files associated with a model state and stores them locally.
        /// </summary>
        /// <param name="modelState">State object representing the model to download
        /// Such files are used once to run a simulation with a model that is not available in the state upon at a give time.</param>
        /// <returns>True if the file was downloaded successfully, false otherwise</returns>
        private async Task<bool> DownloadAllFilesForModelAsync(T modelState)
        {
            if (modelState == null)
            {
                throw new ArgumentNullException(nameof(modelState));
            }

            modelState.DownloadAttempts++;


            var fileIds = modelState.GetPendingDownloadFileIds(); // TODO: this whould only list non-existing ones

            _logger.LogInformation("Downloading {Count} file(s) for model revision external ID: {ExternalId}. Attempt: {DownloadAttempts}",
                fileIds.Count,
                modelState.ExternalId,
                modelState.DownloadAttempts);

            var files = await _cdfFiles
                .RetrieveAsync(fileIds, ignoreUnknownIds: true)
                .ConfigureAwait(false); // todo: make batch version

            var filesMap = files.ToDictionarySafe(file => file.Id, file => file);

            bool allFilesDownloaded = true;

            for (int fileIndex = 0; fileIndex < fileIds.Count; fileIndex++)
            {
                var downloaded = false;
                var fileId = fileIds[fileIndex];
                var isMainFile = fileId == modelState.CdfId;
                if (filesMap.TryGetValue(fileId, out var file))
                {
                    _logger.LogInformation("Downloading file ({FileNumber}/{FilesTotal}): {Id}. Model revision external ID: {ExternalId}.",
                        fileIndex + 1,
                        fileIds.Count,
                        fileId,
                        modelState.ExternalId);
                    var fileExtension = file.GetExtension();
                    string filePath = await DownloadFileAsync(fileId, fileExtension, modelState.ExternalId).ConfigureAwait(false);
                    downloaded = !string.IsNullOrEmpty(filePath);
                    if (downloaded)
                    {
                        if (isMainFile)
                        {
                            modelState.IsInDirectory = true;
                            modelState.FilePath = filePath;
                            modelState.FileExtension = fileExtension;
                        }
                        else
                        {
                            modelState.UpdateDependencyFile(fileId, (dependency) =>
                            {
                                dependency.FilePath = filePath;
                                return dependency;
                            });
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("File with ID {FileId} not found in CDF for model revision {ExternalId}",
                        fileId, modelState.ExternalId);
                }
                allFilesDownloaded &= downloaded;
            }

            return allFilesDownloaded;
        }

        /// <summary>
        /// Downloads a file from CDF and stores it locally
        /// Such files are used once to run a simulation with a model that is not available in the state upon at a give time.
        /// Each file serve as either a main model file or as a dependency to a model.
        /// </summary>
        /// <param name="fileId">ID of the file to download</param>
        /// <param name="fileExtension">File extension to use for the downloaded file</param>
        /// <param name="modelRevisionExternalId">External ID of the model revision to which the file belongs.</param>
        /// <returns>File path where the file was downloaded, or null if the download failed</returns>
        private async Task<string> DownloadFileAsync(long fileId, string fileExtension, string modelRevisionExternalId)
        {
            try
            {
                var downloadUriRes = await _cdfFiles
                    .DownloadAsync([new Identity(fileId)])
                    .ConfigureAwait(false);

                var downloadUri = downloadUriRes.FirstOrDefault()?.DownloadUrl;

                if (downloadUri != null)
                {
                    var storageFolder = Path.Combine(_modelFolder, $"{fileId}");
                    CreateDirectoryIfNotExists(storageFolder);

                    var filePath = Path.Combine(storageFolder, $"{fileId}.{fileExtension}");
                    bool downloaded = await _downloadClient
                        .DownloadFileAsync(downloadUri, filePath)
                        .ConfigureAwait(false);

                    if (downloaded)
                    {
                        _logger.LogDebug("File downloaded: {Id}. Model revision: {ExternalId}. File path: {FilePath}",
                            fileId,
                            modelRevisionExternalId,
                            filePath);

                        return filePath;
                    }
                }
            }
            catch (ResponseException e)
            {
                // File cannot be downloaded, skip for now and try again later
                _logger.LogWarning("Failed to fetch file url from CDF: {Message}. Model revision: {ExternalId}. File ID: {FileId}",
                    e.Message,
                    modelRevisionExternalId,
                    fileId
                );
            }
            catch (ConnectorException e)
            {
                _logger.LogWarning("Failed to download file: {Message}. Model revision: {ExternalId}. File ID: {FileId}",
                    e.Message,
                    modelRevisionExternalId,
                    fileId
                );
            }
            catch (Exception e)
            {
                _logger.LogError("Error occurred while downloading the file for model revision {ExternalId}. File ID {FileId}: {Error}",
                    modelRevisionExternalId,
                    fileId,
                    e
                );
            }
            return null;
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
                _logger.LogInformation("Updating file library. There are currently {Num} files. Extracted range: {TimeRange}",
                    _state.Count,
                    timeRange
                    );

                // Find new model files in CDF and add the to the local state.
                await FindModelRevisions(true, token)
                    .ConfigureAwait(false);

                await SaveStates(token).ConfigureAwait(false);

                await Task
                    .Delay(TimeSpan.FromSeconds(_config.LibraryUpdateInterval), token)
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
            await StoreLibraryState(token).ConfigureAwait(false);
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
                _state.Values,
                _config.FilesTable,
                (state) =>
                {
                    var poco = state.GetPoco();
                    return poco;
                },
            token).ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes the model library and releases any resources it holds.
        /// </summary>
        public void Dispose()
        {
            _revisionsTasks?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Interface for libraries that can provide model information
    /// </summary>
    /// <typeparam name="A">Type of the automation configuration object</typeparam>
    /// <typeparam name="T">Model state type</typeparam>
    public interface IModelProvider<A, T>
    {
        /// <summary>
        /// Returns the state object of the given version of the given model
        /// </summary>
        /// <param name="modelRevisionExternalId">Model revision external id</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>State object</returns>
        Task<T> GetModelRevision(string modelRevisionExternalId, CancellationToken token = default);
    }
}
