using System;
using System.Collections.Generic;
using System.IO;
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
            ) SetupRuntime(
                List<SimpleRequestMocker> endpointMockTemplates,
                [CallerMemberName] string? testCallerName = null,
                Action<DefaultConfig<AutomationConfig>>? configModifier = null,
                Func<DefaultModelFilestate, CancellationToken, Task>? extractModelInfoOverride = null)
        {
            var simulatorDefinition = SeedData.SimulatorCreate;

            Action<DefaultConfig<AutomationConfig>> mergedConfigModifier = config =>
            {
                config.Connector.SimulationRunLoadBalancingEnabled = true;
                configModifier?.Invoke(config);
            };

            var (provider, mockedLogger) = BuildModelLibraryTestSetup(
                endpointMockTemplates,
                simulatorDefinition,
                testCallerName,
                mergedConfigModifier,
                extractModelInfoOverride);

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
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/update"), () => MockSimulatorModelRevEndpoint(), 2), // parsing status + success status
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
            Assert.Equal(0, modelInState.DownloadAttempts);
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

        /// <summary>
        /// Verifies that when model parsing exceeds the configured timeout, the model revision status is
        /// set to "failure" (not "unknown") and the appropriate warning is logged.
        /// </summary>
        [Fact]
        public async Task TestModelLibraryParsingTimeoutSetsFailureStatus()
        {
            // Arrange
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), () => OkItemsResponse(""), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), () => MockSimulatorModelRevEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/update"), () => MockSimulatorModelRevEndpoint(), 2), // parsing status + failure status = 2
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 1),
                new SimpleRequestMocker(uri => true, GoneResponse).ShouldBeCalled(Times.AtMost(100))
            };

            var (lib, mockedLogger) = SetupRuntime(
                endpointMockTemplates,
                configModifier: config => config.Connector.ModelLibrary.ModelParsingTimeout = 1,
                extractModelInfoOverride: async (state, token) =>
                {
                    // Block indefinitely until the timeout cancellation token fires
                    await Task.Delay(Timeout.Infinite, token);
                });

            await lib.Init(CancellationToken.None);
            Assert.Empty(lib._state);

            // Act
            var modelInState = await lib.GetModelRevision("TestModelExternalId-v1");

            // Assert
            Assert.NotNull(modelInState);
            Assert.True(modelInState.ParsingInfo.Error);
            Assert.True(modelInState.ParsingInfo.Parsed);
            Assert.Equal(SimulatorModelRevisionStatus.failure, modelInState.ParsingInfo.Status);
            Assert.Contains("timed out", modelInState.ParsingInfo.StatusMessage, StringComparison.OrdinalIgnoreCase);

            foreach (var mocker in endpointMockTemplates)
            {
                mocker.AssertCallCount();
            }

            VerifyLog(mockedLogger, LogLevel.Warning, "Model parsing timed out", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Extracting model information for TestModelExternalId v1", Times.Exactly(1), true);
        }

        /// <summary>
        /// Verifies that when ExtractModelInformation throws an unexpected exception, the model revision
        /// status is set to "failure" (not left stuck as "parsing") and the error is logged.
        /// </summary>
        [Fact]
        public async Task TestModelLibraryParsingExceptionSetsFailureStatus()
        {
            // Arrange
            const string parsingErrorMessage = "Simulator crashed unexpectedly";
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), () => OkItemsResponse(""), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), () => MockSimulatorModelRevEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/update"), () => MockSimulatorModelRevEndpoint(), 2), // parsing status + failure status = 2
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 1),
                new SimpleRequestMocker(uri => true, GoneResponse).ShouldBeCalled(Times.AtMost(100))
            };

            var (lib, mockedLogger) = SetupRuntime(
                endpointMockTemplates,
                extractModelInfoOverride: (state, token) =>
                {
                    throw new InvalidOperationException(parsingErrorMessage);
                });

            await lib.Init(CancellationToken.None);
            Assert.Empty(lib._state);

            // Act
            var modelInState = await lib.GetModelRevision("TestModelExternalId-v1");

            // Assert — model should be accessible but marked as failed, not stuck in "parsing"
            Assert.NotNull(modelInState);
            Assert.True(modelInState.ParsingInfo.Error);
            Assert.True(modelInState.ParsingInfo.Parsed);
            Assert.Equal(SimulatorModelRevisionStatus.failure, modelInState.ParsingInfo.Status);
            Assert.Contains(parsingErrorMessage, modelInState.ParsingInfo.StatusMessage, StringComparison.OrdinalIgnoreCase);

            foreach (var mocker in endpointMockTemplates)
            {
                mocker.AssertCallCount();
            }

            VerifyLog(mockedLogger, LogLevel.Error, "Model parsing failed for TestModelExternalId v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Extracting model information for TestModelExternalId v1", Times.Exactly(1), true);
        }

        /// <summary>
        /// Verifies that when load balancing is disabled, ExtractModelInformation is still called
        /// (old behaviour preserved) and no 'parsing' status is sent before extraction.
        /// Only 1 update call is expected (success status).
        /// </summary>
        [Fact]
        public async Task TestModelLibraryLoadBalancingDisabledCallsExtractModelInformation()
        {
            // Arrange
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), () => OkItemsResponse(""), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), () => MockSimulatorModelRevEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/update"), () => MockSimulatorModelRevEndpoint(), 1), // success status only
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 1),
                new SimpleRequestMocker(uri => true, GoneResponse).ShouldBeCalled(Times.AtMost(100))
            };

            var (lib, mockedLogger) = SetupRuntime(
                endpointMockTemplates,
                configModifier: config => config.Connector.SimulationRunLoadBalancingEnabled = false);

            await lib.Init(CancellationToken.None);
            Assert.Empty(lib._state);

            // Act
            var modelInState = await lib.GetModelRevision("TestModelExternalId-v1");

            // Assert
            Assert.NotNull(modelInState);
            Assert.True(modelInState.ParsingInfo.Parsed);
            Assert.False(modelInState.ParsingInfo.Error);
            Assert.Equal(SimulatorModelRevisionStatus.success, modelInState.ParsingInfo.Status);

            foreach (var mocker in endpointMockTemplates)
            {
                mocker.AssertCallCount();
            }

            VerifyLog(mockedLogger, LogLevel.Information, "Extracting model information for TestModelExternalId v1", Times.Exactly(1), true);
        }

        /// <summary>
        /// Verifies that when a model revision is already in 'parsing' status and the parsing started
        /// recently (within the timeout), a second connector skips it.
        /// </summary>
        [Fact]
        public async Task TestModelLibrarySkipsRevisionAlreadyBeingParsedByAnotherConnector()
        {
            // Arrange
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), () => OkItemsResponse(""), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), () => MockSimulatorModelRevParsingEndpoint(), 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 1),
                new SimpleRequestMocker(uri => true, GoneResponse).ShouldBeCalled(Times.AtMost(100))
            };

            var (lib, mockedLogger) = SetupRuntime(
                endpointMockTemplates,
                configModifier: config => config.Connector.ModelLibrary.ModelParsingTimeout = 3600);

            await lib.Init(CancellationToken.None);
            Assert.Empty(lib._state);

            // Act
            var modelInState = await lib.GetModelRevision("TestModelExternalId-v1");

            // Assert
            Assert.NotNull(modelInState);
            Assert.False(modelInState.ParsingInfo.Parsed);
            Assert.Equal(SimulatorModelRevisionStatus.parsing, modelInState.ParsingInfo.Status);

            foreach (var mocker in endpointMockTemplates)
            {
                mocker.AssertCallCount();
            }

            VerifyLog(mockedLogger, LogLevel.Debug, "Skipping model revision TestModelExternalId-v1", Times.Exactly(1), true);
        }

        /// <summary>
        /// Verifies that when a model revision is stuck in 'parsing' status past the configured timeout
        /// the connector re-parses it and sets the final status.
        /// </summary>
        [Fact]
        public async Task TestModelLibraryReparsesStaleParsingRevisionAfterCrashRecovery()
        {
            // Arrange
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), () => OkItemsResponse(""), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), () => MockSimulatorModelRevParsingEndpoint(DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeMilliseconds()), 1),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/update"), () => MockSimulatorModelRevEndpoint(), 2), // parsing + success
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint, 1),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 1),
                new SimpleRequestMocker(uri => true, GoneResponse).ShouldBeCalled(Times.AtMost(100))
            };

            var (lib, mockedLogger) = SetupRuntime(
                endpointMockTemplates,
                configModifier: config => config.Connector.ModelLibrary.ModelParsingTimeout = 3600);

            await lib.Init(CancellationToken.None);
            Assert.Empty(lib._state);

            // Act
            var modelInState = await lib.GetModelRevision("TestModelExternalId-v1");

            // Assert
            Assert.NotNull(modelInState);
            Assert.True(modelInState.ParsingInfo.Parsed);
            Assert.False(modelInState.ParsingInfo.Error);
            Assert.Equal(SimulatorModelRevisionStatus.success, modelInState.ParsingInfo.Status);

            foreach (var mocker in endpointMockTemplates)
            {
                mocker.AssertCallCount();
            }

            VerifyLog(mockedLogger, LogLevel.Warning, "has been in 'parsing' status", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Extracting model information for TestModelExternalId v1", Times.Exactly(1), true);
        }
    }
}
