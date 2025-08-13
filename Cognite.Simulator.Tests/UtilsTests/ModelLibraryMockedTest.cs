using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.StateStorage;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{
    /// <summary>
    /// Tests for the DefaultModelLibrary class with mocked API endpoints.
    /// </summary>
    [Collection(nameof(SequentialTestCollection))]
    public class ModelLibraryMockedTest : IDisposable
    {
        StateStoreConfig? stateConfig;
        ModelLibraryConfig? modelLibraryConfig;

        public void Dispose()
        {
            CleanUpFiles(modelLibraryConfig, stateConfig);
        }

        private (
            DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>,
            Mock<ILogger<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>>
            ) SetupRuntime(List<SimpleRequestMocker> endpointMockTemplates, [CallerMemberName] string? testCallerName = null)
        {
            var simulatorDefinition = SeedData.GetSimulatorCreateObj();

            var (provider, mockedLogger) = BuildModelLibraryTestSetup(endpointMockTemplates, simulatorDefinition, testCallerName);

            stateConfig = provider.GetRequiredService<StateStoreConfig>();
            modelLibraryConfig = provider.GetRequiredService<DefaultConfig<AutomationConfig>>().Connector.ModelLibrary;
            var lib = provider.GetRequiredService<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>();

            return (lib, mockedLogger);
        }

        /// <summary>
        /// Basic test for the DefaultModelLibrary with mocked API endpoints.
        /// This test verifies that the library can fetch a model revision and its files correctly.
        /// </summary>
        [Fact]
        public async Task TestModelLibraryWithMockedApi()
        {
            // Arrange
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), () => MockSimulatorModelRevEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), () => MockSimulatorModelRevEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/update"), () => MockSimulatorModelRevEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 1),
                new SimpleRequestMocker(uri => true, GoneResponse).ShouldBeCalled(Times.AtMost(100)) // doesn't matter for the test
            };

            var (lib, mockedLogger) = SetupRuntime(endpointMockTemplates);

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

            foreach (var mocker in endpointMockTemplates)
            {
                mocker.AssertCallCount();
            }

            VerifyLog(mockedLogger, LogLevel.Debug, "Model revision not found locally, adding to the local state: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Downloading 1 file(s) for model revision external ID: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Downloading file (1/1): 100. Model revision external ID: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 100. Model revision: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "Using cached model revision for TestModelExternalId-v1", Times.Exactly(1), true);
        }
    }
}
