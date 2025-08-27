using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
    /// Tests for the ModelLibraryBase class with focus on the concurrency aspects.
    /// Uses FakeModelLibrary and SimpleRequestMocker to mock the HTTP layer.
    /// </summary>
    [Collection(nameof(SequentialTestCollection))]
    public class ModelLibraryConcurrencyTest : IDisposable
    {
        private StateStoreConfig? stateConfig;
        private ModelLibraryConfig? modelLibraryConfig;

        private static readonly IList<SimpleRequestMocker> endpointMockTemplates = new List<SimpleRequestMocker>
        {
            new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
            new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), () => OkItemsResponse(""), 1), // Returns empty to simulate no revisions found on first call
            new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/byids"), MockSimulatorModelRevEndpoint, 5),
            new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/update"), MockSimulatorModelRevEndpoint, 1),
            new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint, 1),
            new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint([100]), 1),
            new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 1),
            new SimpleRequestMocker(uri => true, GoneResponse).ShouldBeCalled(Times.AtMost(100)) // doesn't matter for the test
        };

        public void Dispose()
        {
            CleanUpFiles(modelLibraryConfig, stateConfig);
        }

        /// <summary>
        /// This test verifies that multiple concurrent calls to GetModelRevision for the same model
        /// don't result in parallel processing of the same model revision.
        /// </summary>
        [Fact]
        public async Task TestModelLibraryConcurrentCalls()
        {
            var mockedLogger = new Mock<ILogger<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>>();

            var services = new ServiceCollection();
            services.AddMockedHttpClientFactory(MockRequestsAsync(endpointMockTemplates));
            services.AddSingleton(mockedLogger.Object);
            services.AddCogniteTestClient();
            services.AddSingleton<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, FakeSimulatorClient>();
            services.AddSingleton<FileStorageClient>();
            services.AddSingleton<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>();
            services.AddSingleton(SeedData.SimulatorCreate);
            services.AddDefaultConfig();

            using var provider = services.BuildServiceProvider();

            modelLibraryConfig = provider.GetRequiredService<DefaultConfig<AutomationConfig>>().Connector.ModelLibrary;
            stateConfig = provider.GetRequiredService<StateStoreConfig>();
            var lib = provider.GetRequiredService<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>();
            await lib.Init(CancellationToken.None);
            Assert.Empty(lib._state);

            var client = provider.GetRequiredService<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>>() as FakeSimulatorClient;
            Assert.NotNull(client);

            const int concurrentRequests = 5;
            var tasks = new List<Task<DefaultModelFilestate>>();
            for (int i = 0; i < concurrentRequests; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    return lib.GetModelRevision("TestModelExternalId-v1");
                }));
            }

            var results = await Task.WhenAll(tasks);

            var modelInState = results[0];
            Assert.NotNull(modelInState);

            Assert.Equal(1, client.ExtractModelInformationCallCount);

            // Verify all results are the same
            foreach (var result in results)
            {
                Assert.Same(modelInState, result);
            }

            // Basic validations
            Assert.NotEmpty(lib._state);
            Assert.NotNull(modelInState);
            Assert.Equal(100, modelInState.CdfId);
            Assert.Equal(123, modelInState.DataSetId);
            Assert.Equal("TestModelExternalId-v1", modelInState.ExternalId);
            Assert.Equal("TestModelExternalId", modelInState.ModelExternalId);
            Assert.Equal(1234567890000, modelInState.CreatedTime);
            Assert.Equal(1, modelInState.DownloadAttempts);
            Assert.Equal(SimulatorModelRevisionStatus.success, modelInState.ParsingInfo.Status);
            Assert.True(modelInState.CanRead);
            Assert.False(modelInState.ParsingInfo.Error);
            Assert.True(modelInState.ParsingInfo.Parsed);
            Assert.EndsWith(Path.Combine("100", "100.csv"), modelInState.FilePath);
            Assert.Equal("csv", modelInState.FileExtension);

            Assert.True(modelInState.Downloaded);
            var fileBytes = System.IO.File.ReadAllBytes(modelInState.FilePath);
            Assert.Single(fileBytes);
            Assert.Equal(1, fileBytes[0]);

            foreach (var mocker in endpointMockTemplates)
            {
                mocker.AssertCallCount();
            }

            VerifyLog(mockedLogger, LogLevel.Debug, "Model revision not found locally, adding to the local state: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Downloading 1 file(s) for model revision external ID: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Information, "Downloading file: 100. Model revision external ID: TestModelExternalId-v1", Times.Exactly(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "File downloaded: 100. Model revision: TestModelExternalId-v1", Times.Exactly(1), true);
        }

        public class FakeSimulatorClient : ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
        {
            private int _extractModelInformationCallCount = 0;
            public int ExtractModelInformationCallCount => _extractModelInformationCallCount;
            private readonly ILogger<FakeSimulatorClient> _logger;

            public FakeSimulatorClient(ILogger<FakeSimulatorClient> logger)
            {
                _logger = logger;
            }

            public async Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken _token)
            {
                Interlocked.Increment(ref _extractModelInformationCallCount);
                _logger.LogInformation("Simulating long-running operation for ExtractModelInformation");

                await Task.Delay(500);

                state.ParsingInfo.SetSuccess();
            }

            public string GetConnectorVersion(CancellationToken _token)
            {
                return "1.0.0";
            }

            public string GetSimulatorVersion(CancellationToken _token)
            {
                return "1.0.0";
            }

            public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(
                DefaultModelFilestate modelState,
                SimulatorRoutineRevision simulationConfiguration,
                Dictionary<string, SimulatorValueItem> inputData,
                CancellationToken token)
            {
                throw new NotImplementedException();
            }

            public Task TestConnection(CancellationToken token)
            {
                return Task.CompletedTask;
            }
        }
    }
}
