using Cognite.Extractor.StateStorage;
using Cognite.Simulator.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents the state of a simulator file
    /// </summary>
    public class FileState : IExtractionState
    {

        /// <summary>
        /// File id. Typically CDF external id
        /// </summary>
        public string Id { get; set; }
        
        private string _externalId;
        /// <summary>
        /// External ID of the entity that is represented by this object.
        /// </summary>
        public string ExternalId
        {
            get => _externalId;
            set
            {
                if (value == _externalId) return;
                LastTimeModified = DateTime.UtcNow;
                _externalId = value;
            }
        }

        /// <summary>
        /// Last time this state was modified
        /// </summary>
        public DateTime? LastTimeModified { get; protected set; }

        /// <summary>
        /// Creates a new file state with the provided id
        /// </summary>
        /// <param name="id">File id</param>
        public FileState()
        {
        }

        private string _modelExternalId;
        
        /// <summary>
        /// External ID of the model associated with this file. The model is
        /// typically the object being simulated.
        /// Each model can have multiple revisions, where each revision is stored in a CDF file.
        /// </summary>
        public string ModelExternalId
        {
            get => _modelExternalId;
            set
            {
                if (value == _modelExternalId) return;
                LastTimeModified = DateTime.UtcNow;
                _modelExternalId = value;
            }
        }

        private string _source;

        private bool _isInDirectory;

        /// <summary>
        /// If the file is stored in a directory, or as a single file
        /// </summary>
        public bool IsInDirectory {
            get => _isInDirectory;
            set
            {
                if (value == _isInDirectory) return;
                LastTimeModified = DateTime.UtcNow;
                _isInDirectory = value;
            }
        }

        /// <summary>
        /// Source of this file. Typically the name of the simulator
        /// </summary>
        public string Source
        {
            get => _source;
            set
            {
                if (value == _source) return;
                LastTimeModified = DateTime.UtcNow;
                _source = value;
            }
        }

        private long? _dataSetId;
        
        /// <summary>
        /// Dataset id that contains the file in CDF
        /// </summary>
        public long? DataSetId
        {
            get => _dataSetId;
            set
            {
                if (value == _dataSetId) return;
                LastTimeModified = DateTime.UtcNow;
                _dataSetId = value;
            }
        }

        private string _filePath;
        
        /// <summary>
        /// Path of the file in the local disk. This is only available
        /// once the file has been downloaded from CDF and saved locally.
        /// </summary>
        public string FilePath
        {
            get => _filePath;
            internal set
            {
                if (value == _filePath) return;
                LastTimeModified = DateTime.UtcNow;
                _filePath = value;
            }
        }

        private long _createdTime;
        
        /// <summary>
        /// Time the file was created in CDF
        /// </summary>
        public long CreatedTime
        {
            get => _createdTime;
            set
            {
                if (value == _createdTime) return;
                LastTimeModified = DateTime.UtcNow;
                _createdTime = value;
            }
        }

        private long _updatedTime;
        
        /// <summary>
        /// Last time the file was updated in CDF
        /// </summary>
        public long UpdatedTime
        {
            get => _updatedTime;
            set
            {
                if (value == _updatedTime) return;
                LastTimeModified = DateTime.UtcNow;
                _updatedTime = value;
            }
        }

        private long _cdfId;
        
        /// <summary>
        /// Internal (numeric) id of the file in CDF
        /// </summary>
        public long CdfId
        {
            get => _cdfId;
            set
            {
                if (value == _cdfId) return;
                LastTimeModified = DateTime.UtcNow;
                _cdfId = value;
            }
        }

        private long _logId;
        /// <summary>
        /// Model revision logId
        /// </summary>
        public long LogId {
            get => _logId;
            set
            {
                if (value == _logId) return;
                LastTimeModified = DateTime.UtcNow;
                _logId = value;
            }
        }

        /// <summary>
        /// Initialize this state using a data object from the state store
        /// </summary>
        /// <param name="poco">Data object</param>
        public virtual void Init(FileStatePoco poco)
        {
            if (poco == null)
            {
                throw new ArgumentNullException(nameof(poco));
            }
            _source = poco.Source;
            _dataSetId = poco.DataSetId;
            _filePath = poco.FilePath;
            _createdTime = poco.CreatedTime;
            _cdfId = poco.CdfId;
            _updatedTime = poco.UpdatedTime;
            _isInDirectory = poco.IsInDirectory;
            _externalId = poco.ExternalId;
            _logId = poco.LogId;
        }

        /// <summary>
        /// Get the data object with the state properties to be persisted by
        /// the state store
        /// </summary>
        /// <returns></returns>
        public virtual FileStatePoco GetPoco()
        {
            return new FileStatePoco
            {
                Id = Id,
                Source = Source,
                DataSetId = DataSetId,
                FilePath = FilePath,
                CreatedTime = CreatedTime,
                CdfId = CdfId,
                UpdatedTime = UpdatedTime,
                IsInDirectory = IsInDirectory,
                ExternalId = ExternalId,
            };
        }
    }
    
    /// <summary>
    /// Data object that contains the state properties to be persisted
    /// by the state store. These properties are restored to the state on initialization
    /// </summary>
    public class FileStatePoco : BaseStorableState
    {
        /// <summary>
        /// External Id of the entity represented by this object
        /// </summary>
        [StateStoreProperty("external-id")]
        public string ExternalId { get; set; }

        /// <summary>
        /// External ID of the model associated with the file
        /// </summary>
        [StateStoreProperty("model-external-id")]
        public string ModelExternalId { get; set; }
        
        /// <summary>
        /// Source of the file (simulator)
        /// </summary>
        [StateStoreProperty("source")]
        public string Source { get; set; }
        
        /// <summary>
        /// Dataset id in CDF
        /// </summary>
        [StateStoreProperty("data-set-id")]
        public long? DataSetId { get; set; }
        
        /// <summary>
        /// Path to the file in the local disk
        /// </summary>
        [StateStoreProperty("file-path")]
        public string FilePath { get; set; }
        
        /// <summary>
        /// Time the file was created in CDF
        /// </summary>
        [StateStoreProperty("created-time")]
        public long CreatedTime { get; set; }
        
        /// <summary>
        /// CDF internal id of the file 
        /// </summary>
        [StateStoreProperty("cdf-id")]
        public long CdfId { get; set; }
        
        /// <summary>
        /// Last time the file was updated in CDF
        /// </summary>
        [StateStoreProperty("updated-time")]
        public long UpdatedTime { get; set; }

        /// <summary>
        /// Storage directory for the file
        /// </summary>
        [StateStoreProperty("is-stored-in-directory")]
        public bool IsInDirectory { get; set; }

        /// <summary>
        /// Model revision logId
        /// </summary>
        [StateStoreProperty("log-id")]
        public long LogId { get; set; }
    }
}
