using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.StateStorage;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk.Alpha;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{
    /// <summary>
    /// Tests for the DefaultModelLibrary class with focus on external dependencies. 
    /// Uses SimpleRequestMocker to mock the HTTP layer.
    /// </summary>
    [Collection(nameof(SequentialTestCollection))]
    public class ModelLibraryExtertnalDepsTest : IDisposable
    {
        StateStoreConfig? stateConfig;
        ModelLibraryConfig? modelLibraryConfig;

        public void Dispose()
        {
            CleanUpFiles(modelLibraryConfig, stateConfig);
        }

        public static HttpResponseMessage MockFilesByIdsEndpoint()
        {
            var fileInfos = new[]
            {
            new { id = 100, name = "test_model.csv", mimeType = "text/csv" },
            new { id = 101, name = "test_model_dep1.xml", mimeType = "text/xml" },
            new { id = 102, name = "test_model_dep2.xml", mimeType = "text/xml" },
            new { id = 200, name = "test_model2.csv", mimeType = "text/csv" }
            };

            var items = string.Join(",\n", fileInfos.Select(f => $@"{{
                ""id"": {f.id},
                ""name"": ""{f.name}"",
                ""mimeType"": ""{f.mimeType}"",
                ""dataSetId"": 123,
                ""uploaded"": true
            }}"));

            return OkItemsResponse(items);
        }


        private static HttpResponseMessage MockSimulatorModelRevEndpoint(long id = 1000, string version = "v1")
        {
            var externalDependencies = Enumerable.Range(1, 2)
                .Select(i => $@"{{
                    ""file"": {{ ""id"": {100 + i} }},
                    ""arguments"": {{ ""address"": ""test.address.{i}"" }}
                }}").ToList();

            var item = $@"{{
                ""id"": {id},
                ""externalId"": ""TestModelExternalId-{version}"",
                ""name"": ""Test Model Revision"",
                ""description"": ""Test model revision description"",
                ""simulatorExternalId"": ""{SeedData.TestSimulatorExternalId}"",
                ""modelExternalId"": ""TestModelExternalId"",
                ""externalDependencies"": [
                    {string.Join(",", externalDependencies)}
                ],
                ""fileId"": {id / 10},
                ""createdByUserId"": ""n/a"",
                ""status"": ""unknown"",
                ""dataSetId"": 123,
                ""versionNumber"": 1,
                ""logId"": 1234567890,
                ""createdTime"": 1234567890000,
                ""lastUpdatedTime"": 1234567890000
            }}";
            return OkItemsResponse(item);
        }

        private (
            DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>,
            Mock<ILogger<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>>
            ) SetupServices(List<SimpleRequestMocker> endpointMockTemplates, [CallerMemberName] string? testCallerName = null)
        {
            var mockedLogger = new Mock<ILogger<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>>();
            var mockedSimulatorClient = new Mock<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>>();
            mockedSimulatorClient.Setup(client => client.ExtractModelInformation(It.IsAny<DefaultModelFilestate>(), It.IsAny<CancellationToken>()))
                .Returns((DefaultModelFilestate state, CancellationToken token) =>
                {
                    state.ParsingInfo.SetSuccess();
                    return Task.CompletedTask;
                });

            var services = new ServiceCollection();
            services.AddMockedHttpClientFactory(MockRequestsAsync(endpointMockTemplates));
            services.AddSingleton(mockedLogger.Object);
            services.AddCogniteTestClient(testCallerName);
            services.AddSingleton(mockedSimulatorClient.Object);
            services.AddSingleton<FileStorageClient>();
            services.AddSingleton<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>();
            services.AddSingleton(SeedData.SimulatorCreate);
            services.AddDefaultConfig();

            var provider = services.BuildServiceProvider();

            stateConfig = provider.GetRequiredService<StateStoreConfig>();
            modelLibraryConfig = provider.GetRequiredService<DefaultConfig<AutomationConfig>>().Connector.ModelLibrary;
            var lib = provider.GetRequiredService<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>();

            return (lib, mockedLogger);
        }

        /// <summary>
        /// This test verifies that the ModelLibrary can handle external dependencies correctly.
        /// It checks that the model revision is fetched, files are downloaded, and model is processed correctly.
        /// </summary>
        [Fact]
        public async Task TestModelLibraryWithExternalDependencies()
        {
            // Arrange
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), () => MockSimulatorModelRevEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), () => MockSimulatorModelRevEndpoint(), 2),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/update"), () => MockSimulatorModelRevEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint, 3),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 3),
                new SimpleRequestMocker(uri => true, GoneResponse).ShouldBeCalled(Times.AtMost(100)) // doesn't matter for the test
            };

            var (lib, mockedLogger) = SetupServices(endpointMockTemplates);

            await lib.Init(CancellationToken.None);
            Assert.NotEmpty(lib._state);

            // Act
            var modelInState = await lib.GetModelRevision("TestModelExternalId-v1");

            // Assert
            Assert.NotNull(modelInState);
            Assert.Equal("TestModelExternalId-v1", modelInState.ExternalId);
            Assert.Equal(1, modelInState.DownloadAttempts);
            Assert.True(modelInState.ParsingInfo.Parsed);
            Assert.EndsWith(Path.Combine("100", "100.csv"), modelInState.FilePath);
            Assert.Equal("csv", modelInState.FileExtension);
            Assert.True(modelInState.Downloaded);

            var filesDirectory = Path.GetFullPath(modelLibraryConfig?.FilesDirectory ?? string.Empty);
            Assert.True(File.Exists(Path.Combine(filesDirectory, "100", "100.csv")));
            Assert.True(File.Exists(Path.Combine(filesDirectory, "101", "101.xml")));
            Assert.True(File.Exists(Path.Combine(filesDirectory, "102", "102.xml")));

            Assert.NotNull(modelInState.DependencyFiles);
            Assert.Equal(2, modelInState.DependencyFiles.Count);

            var expectedDependencyFiles = Enumerable.Range(1, 2)
                .Select(i => new DependencyFile
                {
                    Id = 100 + i,
                    FilePath = Path.Combine(filesDirectory, $"{100 + i}", $"{100 + i}.xml"),
                    Arguments = new() { { "address", "test.address." + i } }
                })
                .ToArray();
            Assert.Equivalent(expectedDependencyFiles, modelInState.DependencyFiles);

            foreach (var mocker in endpointMockTemplates)
            {
                mocker.AssertCallCount();
            }

            VerifyLog(mockedLogger, LogLevel.Debug, "Model revision not found locally, adding to the local state: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Downloading 3 file(s) for model revision external ID: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Downloading file (1/3): 100. Model revision external ID: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Downloading file (2/3): 101. Model revision external ID: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Downloading file (3/3): 102. Model revision external ID: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 100. Model revision: TestModelExternalId-v1", Times.Exactly(1), true);

            // clear the state, run Init to load from the store to check the deserialization from LiteDB
            modelInState.DependencyFiles.Clear();
            Assert.NotNull(modelInState);
            Assert.Empty(modelInState.DependencyFiles);
            await lib.Init(CancellationToken.None); // TODO: we should be able to lib._state.Clear() & lib.Init() to restore state completely from LiteDB 
            // And make sure redundant file download/processing is not happening https://cognitedata.atlassian.net/browse/POFSP-1138

            Assert.NotNull(modelInState);
            Assert.Equivalent(expectedDependencyFiles, modelInState.DependencyFiles);
        }

        /// <summary>
        /// This test verifies that the ModelLibrary handles single file downloads with external dependencies correctly.
        /// </summary>
        [Fact]
        public async Task TestModelLibraryWithExternalDependenciesLoadFailure()
        {
            // Arrange
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), () => OkItemsResponse(""), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), () => MockSimulatorModelRevEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint, 3),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), GoneResponse, 1), // one file download fails
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 1),
                new SimpleRequestMocker(uri => true, GoneResponse).ShouldBeCalled(Times.AtMost(100)) // doesn't matter for the test
            };

            var (lib, mockedLogger) = SetupServices(endpointMockTemplates);
            await lib.Init(CancellationToken.None);
            Assert.Empty(lib._state);

            // Act
            var modelInState = await lib.GetModelRevision("TestModelExternalId-v1");

            // Assert
            Assert.NotNull(modelInState);
            Assert.Equal("TestModelExternalId-v1", modelInState.ExternalId);
            Assert.NotNull(modelInState.FilePath);
            Assert.False(modelInState.ParsingInfo.Parsed);
            Assert.False(modelInState.Downloaded); // not all files were downloaded successfully

            var filesDirectory = Path.GetFullPath(modelLibraryConfig?.FilesDirectory ?? string.Empty);
            Assert.True(File.Exists(Path.Combine(filesDirectory, "100", "100.csv")));
            Assert.False(File.Exists(Path.Combine(filesDirectory, "101", "101.xml"))); // this file download failed
            Assert.True(File.Exists(Path.Combine(filesDirectory, "102", "102.xml")));

            Assert.Equal(2, modelInState.DependencyFiles.Count);

            var expectedDependencyFiles = Enumerable.Range(1, 2)
                .Select(i => new DependencyFile
                {
                    Id = 100 + i,
                    FilePath = i == 1 ? null : Path.Combine(filesDirectory, $"{100 + i}", $"{100 + i}.xml"),
                    Arguments = new() { { "address", "test.address." + i } }
                })
                .ToArray();
            Assert.Equivalent(expectedDependencyFiles, modelInState.DependencyFiles);

            foreach (var mocker in endpointMockTemplates)
            {
                mocker.AssertCallCount();
            }

            VerifyLog(mockedLogger, LogLevel.Information, "Downloading 3 file(s) for model revision external ID: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 100. Model revision: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 102. Model revision: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 101. Model revision: TestModelExternalId-v1", Times.Never(), true);
        }

        /// <summary>
        /// This test verifies that the ModelLibrary can handle external dependencies with deduplicated downloads.
        /// It checks that the model revisions are fetched, duplicate files are downloaded only once, and model is processed correctly.
        /// If a file is missing on the disk, it should be redownloaded even if the Path exists in the state.
        /// </summary>
        [Fact]
        public async Task TestModelLibraryWithExternalDependenciesDeduplicatedDownload()
        {
            // Arrange
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), () => OkItemsResponse(""), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), () => MockSimulatorModelRevEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), () => MockSimulatorModelRevEndpoint(2000, "v2"), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/update"), () => MockSimulatorModelRevEndpoint(), 2),
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint, 2),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint, 5),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 5),
                new SimpleRequestMocker(uri => true, GoneResponse).ShouldBeCalled(Times.AtMost(100)) // doesn't matter for the test
            };

            var (lib, mockedLogger) = SetupServices(endpointMockTemplates);
            var filesDirectory = Path.GetFullPath(modelLibraryConfig?.FilesDirectory ?? string.Empty);
            await lib.Init(CancellationToken.None);
            Assert.Empty(lib._state);

            // Act
            var v1 = await lib.GetModelRevision("TestModelExternalId-v1");
            File.Delete(Path.Combine(filesDirectory, "102", "102.xml")); // simulate missing file on disk
            var v2 = await lib.GetModelRevision("TestModelExternalId-v2");

            // Assert
            Assert.Equal("TestModelExternalId-v1", v1.ExternalId);
            Assert.Equal("TestModelExternalId-v2", v2.ExternalId);
            Assert.True(v1.Downloaded);
            Assert.True(v2.Downloaded);

            Assert.True(File.Exists(Path.Combine(filesDirectory, "100", "100.csv")));
            Assert.True(File.Exists(Path.Combine(filesDirectory, "101", "101.xml")));
            Assert.True(File.Exists(Path.Combine(filesDirectory, "102", "102.xml")));
            Assert.True(File.Exists(Path.Combine(filesDirectory, "200", "200.csv")));

            Assert.Equal(2, v1.DependencyFiles.Count);

            var expectedDependencyFiles = Enumerable.Range(1, 2)
                .Select(i => new DependencyFile
                {
                    Id = 100 + i,
                    FilePath = Path.Combine(filesDirectory, $"{100 + i}", $"{100 + i}.xml"),
                    Arguments = new() { { "address", "test.address." + i } }
                })
                .ToArray();
            Assert.Equivalent(expectedDependencyFiles, v1.DependencyFiles);
            Assert.Equivalent(expectedDependencyFiles, v2.DependencyFiles);

            foreach (var mocker in endpointMockTemplates)
            {
                mocker.AssertCallCount();
            }

            VerifyLog(mockedLogger, LogLevel.Information, "Downloading 3 file(s) for model revision external ID: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 100. Model revision: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 101. Model revision: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 102. Model revision: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File 101 already exists locally", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File 102 already exists locally", Times.Exactly(0), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 102. Model revision: TestModelExternalId-v2", Times.Exactly(1), true); // redownloaded because it was missing on the disk
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 200. Model revision: TestModelExternalId-v2", Times.Exactly(1), true);
        }
    }
}
