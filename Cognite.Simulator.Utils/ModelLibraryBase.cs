using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    public abstract class ModelLibraryBase<T, U> : FileLibrary<T, U>
        where T : ModelStateBase
        where U : ModelStateBasePoco
    {
        public ModelLibraryBase(
            FileLibraryConfig config, 
            IList<SimulatorConfig> simulators, 
            CogniteDestination cdf, 
            ILogger logger, 
            FileDownloadClient downloadClient, 
            IExtractionStateStore store = null) : 
            base(SimulatorDataType.ModelFile, config, simulators, cdf, logger, downloadClient, store)
        {
        }

        public T GetLatestModelVersion(string simulator, string modelName)
        {
            var modelVersions = GetAllModelVersions(simulator, modelName);
            if (modelVersions.Any())
            {
                return modelVersions.First();
            }
            return null;
        }

        protected IEnumerable<T> GetAllModelVersions(string simulator, string modelName)
        {
            var modelVersions = State.Values
                .Where(s => s.ModelName == modelName
                    && s.Source == simulator
                    && s.Version > 0
                    && !string.IsNullOrEmpty(s.FilePath))
                .OrderByDescending(s => s.Version);
            return modelVersions;
        }

        protected void RemoveLocalFiles(string simulator, string modelName)
        {
            var modelVersions = State.Values
                .Where(f => !string.IsNullOrEmpty(f.FilePath)
                    && (f.IsExtracted || !f.CanRead)
                    && f.ModelName == modelName
                    && f.Source == simulator)
                .OrderByDescending(f => f.Version)
                .Skip(1)
                .ToList();
            foreach (var version in modelVersions)
            {
                StateUtils.DeleteLocalFile(version.FilePath);
            }
        }

        protected async Task VerifyLocalModelState(
            T state,
            CancellationToken token)
        {
            if (state == null)
            {
                return;
            }
            var allVersions = GetAllModelVersions(state.Source, state.ModelName);
            if (allVersions.Any())
            {
                var versionsInCdf = await CdfFiles.FindModelVersions(
                    state.Model,
                    state.DataSetId,
                    token).ConfigureAwait(false);
                var statesToDelete = new List<T>();
                foreach (var version in allVersions)
                {
                    if (!versionsInCdf.Any(v => v.ExternalId == version.Id))
                    {
                        statesToDelete.Add(version);
                        State.Remove(version.Id);
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
        protected override void ProcessDownloadedFiles(CancellationToken token)
        {
            Task.Run(async () => await ExtractModelInformationAndUpdateState(token).ConfigureAwait(false), token)
                .Wait(token);
        }

        protected virtual async Task ExtractModelInformationAndUpdateState(CancellationToken token)
        {
            // Find all model files for which we need to extract data
            // The models are grouped by (simulator, model name)
            var modelGroups = State.Values
                .Where(f => !string.IsNullOrEmpty(f.FilePath) && !f.IsExtracted && f.CanRead)
                .GroupBy(f => new { f.Source, f.ModelName });

            foreach (var group in modelGroups)
            {
                // Extract the data for each model file (version) in this group
                await ExtractModelInformation(group, token).ConfigureAwait(false);

                // Verify that the local version history matches the one in CDF. Else,
                // delete the local state and files for the missing versions.
                await VerifyLocalModelState(group.First(), token).ConfigureAwait(false);

                // Keep only a local copy of the latest model version. After the data is extracted,
                // not need to keep a local copy of versions that are not used in calculations
                RemoveLocalFiles(group.Key.Source, group.Key.ModelName);
            }
        }

        protected abstract Task ExtractModelInformation(
            IEnumerable<T> modelStates,
            CancellationToken token);
    }

    public abstract class ModelStateBase : FileState
    {
        private int _version = 0;
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

        public abstract bool IsExtracted { get; }
        public bool CanRead { get; set; } = true;

        public ModelStateBase(string id) : base(id)
        {
        }

        public override string GetDataType()
        {
            return SimulatorDataType.ModelFile.MetadataValue();
        }

        public SimulatorModel Model => new SimulatorModel()
        {
            Name = ModelName,
            Simulator = Source
        };

        public override void Init(FileStatePoco poco)
        {
            base.Init(poco);
            if (poco is ModelStateBasePoco mPoco)
            {
                _version = mPoco.Version;
            }
        }
        public override FileStatePoco GetPoco()
        {
            return new ModelStateBasePoco
            {
                Id = Id,
                ModelName = ModelName,
                Source = Source,
                DataSetId = DataSetId,
                FilePath = FilePath,
                CreatedTime = CreatedTime,
                CdfId = CdfId,
                Version = Version
            };
        }
    }
    public class ModelStateBasePoco : FileStatePoco
    {
        [StateStoreProperty("version")]
        public int Version { get; set; }
    }
}
