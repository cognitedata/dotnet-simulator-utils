using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Serilog.Context;
using System.Threading;
using System.Threading.Tasks;
using CogniteSdk.Alpha;
using Cognite.Extensions;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents a local model file library. This is a <see cref="FileLibrary{T, U}"/> that
    /// fetches simulator model files from CDF, save a local copy and process the model (extract information).
    /// This library only keeps the latest version of a given model file
    /// </summary>
    /// <typeparam name="T">Type of the state object used in this library</typeparam>
    /// <typeparam name="U">Type of the data object used to serialize and deserialize state</typeparam>
    /// <typeparam name="V">Type of the model parsing information object</typeparam>
    public abstract class ModelLibraryBase<T, U, V> : FileLibrary<T, U>, IModelProvider<T>
        where T : ModelStateBase
        where U : ModelStateBasePoco
        where V : ModelParsingInfo, new()
    {
        private readonly ILogger _logger;
        
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
            IExtractionStateStore store = null): 
            base(SimulatorDataType.ModelFile, config, simulators, cdf, logger, downloadClient, store)
        {
            _logger = logger;
        }

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
                var state = AddModelRevisionToState(modelRevision, model); // TODO what happens if the other thread is downloading it as well :(())
                var downloaded = await DownloadFileAsync(state).ConfigureAwait(false);
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
                            statesToDelete.Add(revision);
                            State.Remove(revision.Id);
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
                        await RemoveStates(statesToDeleteWithFile, token).ConfigureAwait(false);
                    }
                }   
            }
            catch (System.Exception e)
            {
                Logger.LogError("Error verifying local model state: {Message}", e.Message);
                return;
            }
            
        }
        
        /// <summary>
        /// Process model files that have been downloaded
        /// </summary>
        /// <param name="token">Cancellation token</param>
        protected override void ProcessDownloadedFiles(CancellationToken token)
        {
            Task.Run(async () => await ExtractModelInformationAndUpdateState(token).ConfigureAwait(false), token)
                .Wait(token);
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
        protected virtual async Task ExtractModelInformationAndUpdateState(CancellationToken token)
        {
            // Find all model files for which we need to extract data
            // The models are grouped by (simulator, model name)
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

                // // Keep only a local copy of the latest model version. After the data is extracted,
                // // not need to keep a local copy of versions that are not used in calculations
                // RemoveLocalFiles(group.Key.ModelExternalId);
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
    }

    /// <summary>
    /// This base class represents the state of a model file
    /// </summary>
    public abstract class ModelStateBase : FileState
    {
        private int _version;
        
        /// <summary>
        /// Model version
        /// </summary>
        public int Version
        {
            get => _version;
            set
            {
                if (value == _version) return;
                LastTimeModified = DateTime.UtcNow;
                _version = value;
            }
        }

        /// <summary>
        /// Information about model parsing
        /// </summary>
        public ModelParsingInfo ParsingInfo { get; set; }

        /// <summary>
        /// Indicates if information has been extracted from the model file
        /// </summary>
        public abstract bool IsExtracted { get; }
        
        /// <summary>
        /// Indicates if the simulator can read the model file and
        /// its data
        /// </summary>
        public bool CanRead { get; set; } = true;

        /// <summary>
        /// Creates a new model file state with the provided id
        /// </summary>
        /// <param name="id"></param>
        public ModelStateBase(string id) : base(id)
        {
        }

        /// <summary>
        /// Data type of the file. For model files, this is <see cref="SimulatorDataType.ModelFile"/> 
        /// </summary>
        /// <returns>String representation of <see cref="SimulatorDataType.ModelFile"/></returns>
        public override string GetDataType()
        {
            return SimulatorDataType.ModelFile.MetadataValue();
        }

        /// <summary>
        /// Model data associated with this state
        /// </summary>
        public SimulatorModelInfo Model => new SimulatorModelInfo()
        {
            Name = ModelName,
            ExternalId = ModelExternalId,
            Simulator = Source
        };

        /// <summary>
        /// Initialize this model state using a data object from the state store
        /// </summary>
        /// <param name="poco">Data object</param>
        public override void Init(FileStatePoco poco)
        {
            base.Init(poco);
            if (poco is ModelStateBasePoco mPoco)
            {
                _version = mPoco.Version;
            }
        }
        /// <summary>
        /// Get the data object with the model state properties to be persisted by
        /// the state store
        /// </summary>
        /// <returns>File data object</returns>
        public override FileStatePoco GetPoco()
        {
            return new ModelStateBasePoco
            {
                Id = Id,
                ModelName = ModelName,
                ModelExternalId = ModelExternalId,
                Source = Source,
                DataSetId = DataSetId,
                FilePath = FilePath,
                CreatedTime = CreatedTime,
                CdfId = CdfId,
                Version = Version,
                IsInDirectory = IsInDirectory
            };
        }
    }

    /// <summary>
    /// Data object that contains the model state properties to be persisted
    /// by the state store. These properties are restored to the state on initialization
    /// </summary>
    public class ModelStateBasePoco : FileStatePoco
    {
        /// <summary>
        /// Model version
        /// </summary>
        [StateStoreProperty("version")]
        public int Version { get; set; }
    }
}
