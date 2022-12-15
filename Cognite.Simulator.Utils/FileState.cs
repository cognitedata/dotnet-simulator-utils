using Cognite.Extractor.StateStorage;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cognite.Simulator.Utils
{
    public class FileState : IExtractionState
    {
        public string Id { get; protected set; }

        public DateTime? LastTimeModified { get; protected set; }

        public FileState(string id)
        {
            Id = id;
        }

        private string _name;
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

        public virtual string GetExtension()
        {
            return "bin";
        }

        public virtual string GetDataType()
        {
            return "none";
        }

        internal virtual void Init(FileStatePoco poco)
        {
            _name = poco.ModelName;
            _source = poco.Source;
            _dataSetId = poco.DataSetId;
            _filePath = poco.FilePath;
            _createdTime = poco.CreatedTime;
            _cdfId = poco.CdfId;
            _updatedTime = poco.UpdatedTime;
        }

        internal virtual FileStatePoco GetPoco()
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
    public class FileStatePoco : BaseStorableState
    {
        [StateStoreProperty("model-name")]
        public string ModelName { get; internal set; }
        [StateStoreProperty("source")]
        public string Source { get; internal set; }
        [StateStoreProperty("data-set-id")]
        public long? DataSetId { get; internal set; }
        [StateStoreProperty("file-path")]
        public string FilePath { get; internal set; }
        [StateStoreProperty("created-time")]
        public long CreatedTime { get; internal set; }
        [StateStoreProperty("cdf-id")]
        public long CdfId { get; internal set; }
        [StateStoreProperty("updated-time")]
        public long UpdatedTime { get; internal set; }
    }

}
