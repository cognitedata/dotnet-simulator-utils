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
    public abstract class ModelLibraryBase<A, T, U, V> : IModelProvider<A, T>
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
        /// Dictionary holding tasks for processing model revisions. The keys are the external ids of the
        /// CDF simulator model revisions and the values are LazyTask objects for the state objects of type <typeparamref name="T"/>.
        /// This ensures that only one thread processes a given model revision at a time.
        /// </summary>
        private readonly ConcurrentDictionary<string, Lazy<Task<T>>> _revisionsTasks = new();

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

        // Internal objects
        private readonly BaseExtractionState _libState;
        private string _modelFolder;

        private List<(string, string)> _simulatorFileExtMap;


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

        private void CopyNonBaseProperties(T source, T target)
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

        private void SetFileExtensionOnState(T state, string SimulatorExternalId)
        {
            var fileExtension = _simulatorFileExtMap.Find(item => item.Item1 == SimulatorExternalId);
            state.FileExtension = fileExtension.Item2;
        }

        /// <summary>
        /// Initializes the local model library from the state store (sqlite database)
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Init(CancellationToken token)
        {
            var simulators = await _cdfSimulatorResources.ListAsync(new SimulatorQuery(), token).ConfigureAwait(false);


            _simulatorFileExtMap = simulators.Items.Select(sim =>
            {
                var ext = sim.FileExtensionTypes.First();
                return (sim.ExternalId, ext);
            }).ToList();

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
        /// If model revision is not found in the local state, this method will try to read it from CDF, download the file and extract the model information.
        /// If the model revision is already in the local state and has not been changed, it will return the existing state.
        /// </summary>
        /// <param name="modelRevisionExternalId">Model revision External ID</param>
        /// <param name="remoteRevision">Remote model revision object, if available, otherwise it will be fetched from CDF</param>
        /// <param name="token">Cancellation token</param>
        private async Task<T> GetOrAddModelRevisionImpl(string modelRevisionExternalId, SimulatorModelRevision remoteRevision = null, CancellationToken token = default)
        {
            var modelRevision = remoteRevision ?? await TryReadRemoteModelRevision(modelRevisionExternalId, CancellationToken.None).ConfigureAwait(false);

            if (modelRevision == null)
            {
                _logger.LogError("Model revision {ModelRevisionExternalId} not found in CDF", modelRevisionExternalId);
                return null;
            }

            var modelVersions = _state.Values
                .Where(state => state.Id == modelRevision.Id.ToString())
                .OrderByDescending(s => s.CreatedTime);

            var modelState = modelVersions.FirstOrDefault();

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
                downloaded = await DownloadFileAsync(modelState).ConfigureAwait(false);
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
            return GetOrAddModelRevision(modelRevisionExternalId, null, token);
        }

        /// <summary>
        /// This method gives a safe way to get a processed model revision.
        /// Internally, it uses a Lazy Task to ensure that only one thread processes a given model revision at a time.
        /// </summary>
        /// <param name="modelRevisionExternalId">Model revision External ID</param>
        /// <param name="remoteRevision">Remote model revision object, if available, otherwise it will be fetched from CDF</param>
        /// <param name="token">Cancellation token</param>
        private async Task<T> GetOrAddModelRevision(string modelRevisionExternalId, SimulatorModelRevision remoteRevision = null, CancellationToken token = default)
        {
            var lazyTask = _revisionsTasks.GetOrAdd(modelRevisionExternalId, id =>
                new Lazy<Task<T>>(async () =>
                {
                    // It will only ever be executed ONCE for a given key, by the first thread that accesses ".Value".
                    try
                    {
                        return await GetOrAddModelRevisionImpl(id, remoteRevision, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _logger.LogDebug("Processing finished. Removing processing task for model revision {ExternalId}", id);
                        _revisionsTasks.TryRemove(id, out _);
                    }
                })
            );

            try
            {
                // All threads (the winner and the waiters) simply await the lazy task's Value.
                // The first thread to access .Value will trigger the factory.
                // Subsequent threads will get the same, already-running or completed Task.
                return await lazyTask.Value.ConfigureAwait(false);
            }
            catch (Exception)
            {
                _logger.LogDebug("Processing failed for model revision {ExternalId}", modelRevisionExternalId);
                return null;
            }
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
                    if (statesToDelete.Any())
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
            SetFileExtensionOnState(newState, modelRevision.SimulatorExternalId);
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
                    var state = await GetOrAddModelRevision(revision.ExternalId, revision, token).ConfigureAwait(false);
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

        private void CreateDirectoryIfNotExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        /// <summary>
        /// Downloads a file from CDF and stores it locally
        /// </summary>
        /// <param name="modelState">State object representing the file to download</param>
        /// Such files are used once to run a simulation with a model that is not available in the state upon at a give time.</param>
        /// <returns>True if the file was downloaded successfully, false otherwise</returns>
        private async Task<bool> DownloadFileAsync(T modelState)
        {
            if (modelState == null)
            {
                throw new ArgumentNullException(nameof(modelState));
            }
            var fileId = new Identity(modelState.CdfId);
            modelState.DownloadAttempts++;
            _logger.LogInformation("Downloading file: {Id}. Model revision external id: {ExternalId}. Attempt: {DownloadAttempts}",
                modelState.CdfId,
                modelState.ExternalId,
                modelState.DownloadAttempts);

            try
            {
                var response = await _cdfFiles
                    .DownloadAsync(new[] { fileId })
                    .ConfigureAwait(false);
                if (response.Any() && response.First().DownloadUrl != null)
                {
                    var uri = response.First().DownloadUrl;

                    string filename;

                    var modelFolder = _modelFolder;
                    var storageFolder = Path.Combine(modelFolder, $"{modelState.CdfId}");
                    CreateDirectoryIfNotExists(storageFolder);
                    filename = Path.Combine(storageFolder, $"{modelState.CdfId}.{modelState.FileExtension}");
                    modelState.IsInDirectory = true;

                    bool downloaded = await _downloadClient
                        .DownloadFileAsync(uri, filename)
                        .ConfigureAwait(false);
                    if (downloaded)
                    {
                        _logger.LogDebug("File downloaded: {Id}. Model revision: {ExternalId}. File path: {FilePath}",
                            modelState.CdfId,
                            modelState.ExternalId,
                            filename);
                        modelState.FilePath = filename;
                        return true;
                    }
                }
            }
            catch (ResponseException e)
            {
                // File cannot be downloaded, skip for now and try again later
                _logger.LogWarning("Failed to fetch file url from CDF: {Message}. Model revision: {ExternalId}",
                    e.Message,
                    modelState.ExternalId
                );
            }
            catch (ConnectorException e)
            {
                _logger.LogWarning("Failed to download file: {Message}. Model revision: {ExternalId}",
                    e.Message,
                    modelState.ExternalId
                );
            }
            catch (Exception e)
            {
                _logger.LogError("Error occurred while downloading the file for model revision {ExternalId}: {Error}",
                    modelState.ExternalId,
                    e
                );
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
