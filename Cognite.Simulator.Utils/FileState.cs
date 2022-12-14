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
        public string Id { get; protected set; }

        /// <summary>
        /// Last time this state was modified
        /// </summary>
        public DateTime? LastTimeModified { get; protected set; }

        /// <summary>
        /// Creates a new file state with the provided id
        /// </summary>
        /// <param name="id">File id</param>
        public FileState(string id)
        {
            Id = id;
        }

        private string _name;
        
        /// <summary>
        /// Name of the model associated with this file. The model is
        /// typically the object being simulated. Models can consist of several CDF files, including 
        /// model versions, simulation configurations, etc
        /// </summary>
        public string ModelName
        {
            get => _name;
            set
            {
                if (value == _name) return;
                LastTimeModified = DateTime.UtcNow;
                _name = value;
            }
        }

        private string _source;
        
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

        /// <summary>
        /// File extension to use when saving this file locally.
        /// Should conform with the extension expected by the simulator
        /// </summary>
        /// <returns>File extension</returns>
        public virtual string GetExtension()
        {
            return "bin";
        }

        /// <summary>
        /// Data type of the file. Typically, one of the <see cref="SimulatorDataType"/> values.
        /// </summary>
        /// <returns>File data type</returns>
        public virtual string GetDataType()
        {
            return "none";
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
            _name = poco.ModelName;
            _source = poco.Source;
            _dataSetId = poco.DataSetId;
            _filePath = poco.FilePath;
            _createdTime = poco.CreatedTime;
            _cdfId = poco.CdfId;
            _updatedTime = poco.UpdatedTime;
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
                ModelName = ModelName,
                Source = Source,
                DataSetId = DataSetId,
                FilePath = FilePath,
                CreatedTime = CreatedTime,
                CdfId = CdfId,
                UpdatedTime = UpdatedTime
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
        /// Name of the model associated with the file
        /// </summary>
        [StateStoreProperty("model-name")]
        public string ModelName { get; set; }
        
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
    }

}
