using System;

using Cognite.Simulator.Extensions;
using Cognite.Extractor.StateStorage;


namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// This base class represents the state of a model file
    /// TODO: See if we can remove FileState completely and move all the variables into this class
    /// Jira: https://cognitedata.atlassian.net/browse/POFSP-558
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

        private string _fileExtension;

        /// <summary>
        /// Model version
        /// </summary>
        public string FileExtension
        {
            get => _fileExtension;
            set
            {
                if (value == _fileExtension) return;
                LastTimeModified = DateTime.UtcNow;
                _fileExtension = value;
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
        /// If true, the local file will be opened and parsed by the connector. By default, this happens only once per model revision.
        /// Can be overridden, when re-parsing on every download is preferred.
        /// </summary>
        public virtual bool ShouldProcess()
        {
            var isParsedBefore = ParsingInfo?.Parsed ?? false;
            return !string.IsNullOrEmpty(FilePath) && CanRead && !isParsedBefore;
        }

        /// <summary>
        /// Model data associated with this state
        /// </summary>
        public SimulatorModelInfo Model => new SimulatorModelInfo()
        {
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
                ModelExternalId = ModelExternalId,
                Source = Source,
                DataSetId = DataSetId,
                FilePath = FilePath,
                CreatedTime = CreatedTime,
                CdfId = CdfId,
                Version = Version,
                IsInDirectory = IsInDirectory,
                FileExtension = FileExtension
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

        /// <summary>
        /// File extension
        /// </summary>
        [StateStoreProperty("fileext")]
        public string FileExtension { get; set; }
    }
}
