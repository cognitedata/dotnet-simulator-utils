using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Serilog.Context;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
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

        /// <inheritdoc/>
        public T GetLatestModelVersion(string simulator, string modelName)
        {
            var modelVersions = GetAllModelVersions(simulator, modelName);
            if (modelVersions.Any())
            {
                return modelVersions.First();
            }
            return null;
        }

        /// <inheritdoc/>
        public IEnumerable<T> GetAllModelVersions(string simulator, string modelName)
        {
            var modelVersions = State.Values
                .Where(s => s.ModelName == modelName
                    && s.Source == simulator
                    && s.Version > 0
                    && !string.IsNullOrEmpty(s.FilePath))
                .OrderByDescending(s => s.Version);
            return modelVersions;
        }

        /// <summary>
        /// Utility function to remove the local copies (files) of all model versions
        /// except the latest one
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        protected void RemoveLocalFiles(string simulator, string modelName)
        {
            var modelVersionsAll = State.Values
                .Where(f => !string.IsNullOrEmpty(f.FilePath)
                    && (f.IsExtracted || !f.CanRead)
                    && f.Source == simulator)
                .ToList();
            var currentModelVersions = modelVersionsAll
                .Where(f => f.ModelName == modelName)
                .OrderByDescending(f => f.Version);
            var otherModelVersionsMap = modelVersionsAll
                .Where(f => f.ModelName != modelName)
                .ToDictionarySafe(f => f.FilePath, f => true);
            var latestVersion = currentModelVersions.FirstOrDefault();
            var modelVersionsToDelete = currentModelVersions.Skip(1);
            foreach (var version in modelVersionsToDelete)
            {
                // multiple revisions might refer to the same file
                // delete the file only if no other revision refers to it
                if (latestVersion.FilePath != version.FilePath && !otherModelVersionsMap.ContainsKey(version.FilePath)) {
                    if (version.IsInDirectory) {
                        var dirPath = Path.GetDirectoryName(version.FilePath);
                        StateUtils.DeleteLocalDirectory(dirPath);
                    } else {
                        StateUtils.DeleteLocalFile(version.FilePath);
                    }
                }
            }
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
                var localRevisions = GetAllModelVersions(state.Source, state.ModelName);
                if (localRevisions.Any())
                {
                    var modelRevisionsInCdfAllRes = await CdfSimulatorResources.ListSimulatorModelRevisionsAsync(
                        new SimulatorModelRevisionQuery
                        {
                            Filter = new SimulatorModelRevisionFilter()
                        }, token).ConfigureAwait(false);
                    
                    var revisionsInCdf = modelRevisionsInCdfAllRes.Items.Where(v => v.SimulatorExternalId == state.Source);

                    var statesToDelete = new List<T>();
                    foreach (var revision in localRevisions)
                    {
                        if (!revisionsInCdf.Any(v => v.Id.ToString() == revision.Id))
                        {
                            statesToDelete.Add(revision);
                            State.Remove(revision.Id);
                        }
                    }
                    if (statesToDelete.Any())
                    {
                        Logger.LogWarning("Removing {Num} model versions not found in CDF: {Versions}",
                            statesToDelete.Count,
                            string.Join(", ", statesToDelete.Select(s => s.ModelName + " v" + s.Version)));
                        await RemoveStates(statesToDelete, token).ConfigureAwait(false);
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

        /// <summary>
        /// This method find all model versions that have not been processed and calls
        /// the <see cref="ExtractModelInformation(IEnumerable{T}, CancellationToken)"/> method 
        /// to process the models.  
        /// </summary>
        /// <param name="token">Cancellation token</param>
        protected virtual async Task ExtractModelInformationAndUpdateState(CancellationToken token)
        {
            // Find all model files for which we need to extract data
            // The models are grouped by (simulator, model name)
            var modelGroups = State.Values
                .Where(f => !string.IsNullOrEmpty(f.FilePath) && !f.IsExtracted && f.CanRead)
                .GroupBy(f => new { f.Source, f.ModelName });
            foreach (var group in modelGroups)
            {
                InitModelParsingInfo(group);
                
                // Extract the data for each model file (version) in this group
                foreach (var item in group){
                    var logId = item.LogId;
                    using (LogContext.PushProperty("LogId", logId)) {
                        try
                        {
                            _logger.LogInformation("Extracting model information for {ModelName} v{Version}", item.ModelName, item.Version);
                            await ExtractModelInformation(item, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            await UpdateModelParsingInfo(item, token).ConfigureAwait(false);
                        }
                    } 
                }

                // Verify that the local version history matches the one in CDF. Else,
                // delete the local state and files for the missing versions.
                await VerifyLocalModelState(group.First(), token).ConfigureAwait(false);

                // Keep only a local copy of the latest model version. After the data is extracted,
                // not need to keep a local copy of versions that are not used in calculations
                RemoveLocalFiles(group.Key.Source, group.Key.ModelName);
            }
        }

        private async Task UpdateModelParsingInfo(T modelState, CancellationToken token)
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

        private void InitModelParsingInfo(IEnumerable<T> modelStates)
        {
            foreach(var file in modelStates)
            {
                V info = new V(){
                    Parsed = false,
                    ModelName = file.Model.Name,
                    Simulator = file.Model.Simulator,
                    ModelVersion = file.Version,
                    Status = ParsingStatus.ready,
                };
                file.ParsingInfo = info;
                file.LogId = file.LogId;
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
    }

    /// <summary>
    /// Interface for libraries that can provide model information
    /// </summary>
    /// <typeparam name="T">Model state type</typeparam>
    public interface IModelProvider<T>
    {
        /// <summary>
        /// Returns the state object of the latest version of the given model
        /// </summary>
        /// <param name="simulator">Simulator</param>
        /// <param name="modelName">Model Name</param>
        /// <returns>State object</returns>
        T GetLatestModelVersion(string simulator, string modelName);

        /// <summary>
        /// Returns the state objects of all the versions of the given model
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        /// <returns>List of state objects</returns>
        IEnumerable<T> GetAllModelVersions(string simulator, string modelName);
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
