using System.Collections.Generic;
using System.Text.Json;

using Cognite.Extractor.StateStorage;
using Cognite.Simulator.Utils;

using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public class StateStoreTest
    {
        [Fact]
        public void DependencyFile_Serialization()
        {
            // Arrange
            var dependencyFile = new DependencyFile
            {
                Id = 123,
                FilePath = "/some/path/doc.txt",
                Arguments = new Dictionary<string, string> { { "key", "value" } }
            };

            var mapper = StateStoreUtils.BuildMapper();

            // Act
            var raw = StateStoreUtils.BsonToDict(mapper.ToDocument(dependencyFile));
            var json = JsonSerializer.Serialize(raw);

            // Assert
            var expectedJson = "{\"_id\":123,\"arguments\":{\"key\":\"value\"},\"file-path\":\"/some/path/doc.txt\"}";
            Assert.Equal(expectedJson, json);
        }

        [Fact]
        public void ModelStateBasePoco_Serialization()
        {
            // Arrange
            var modelState = new ModelStateBasePoco
            {
                Id = "1",
                CdfId = 789,
                FileExtension = "bin",
                Version = 2,
                ExternalId = "ext-id",
                ModelExternalId = "model-ext-id",
                Source = "simulator",
                DataSetId = 456,
                CreatedTime = 1234567890000,
                UpdatedTime = 1234567891000,
                IsInDirectory = true,
                LogId = 555,
                DownloadAttempts = 3,
                DependencyFiles = new List<DependencyFile>(),
                FilePath = "/some/path/model.bin",
            };

            var mapper = StateStoreUtils.BuildMapper();

            // Act
            var raw = StateStoreUtils.BsonToDict(mapper.ToDocument(modelState));
            var json = JsonSerializer.Serialize(raw);

            // Assert
            var expectedJson = "{\"fileext\":\"bin\",\"version\":2,\"external-id\":\"ext-id\",\"model-external-id\":\"model-ext-id\",\"source\":\"simulator\",\"data-set-id\":456,\"file-path\":\"/some/path/model.bin\",\"created-time\":1234567890000,\"cdf-id\":789,\"updated-time\":1234567891000,\"is-stored-in-directory\":true,\"log-id\":555,\"dependency-files\":[],\"download-attempts\":3,\"_id\":\"1\"}";
            Assert.Equal(expectedJson, json);
        }
    }
}
