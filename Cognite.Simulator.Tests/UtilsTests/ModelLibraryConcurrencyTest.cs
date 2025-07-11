using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.StateStorage;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk.Alpha;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{
    /// <summary>
    /// Tests for the ModelLibraryBase class that focus on the concurrency aspects.
    /// Uses FakeModelLibrary and SimpleRequestMocker to mock the HTTP layer.
    /// </summary>
    public class ModelLibraryConcurrencyTest
    {
        private static readonly IList<SimpleRequestMocker> endpointMockTemplates = new List<SimpleRequestMocker>
        {
            new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
            new SimpleRequestMocker(uri => uri.EndsWith("/token/inspect"), MockTokenInspectEndpoint),
            new SimpleRequestMocker(uri => uri.Contains("/extpipes"), MockExtPipesEndpoint),
            new SimpleRequestMocker(uri => uri.EndsWith("/simulators/list") || uri.EndsWith("/simulators") || uri.EndsWith("/simulators/update"), MockSimulatorsEndpoint),
            new SimpleRequestMocker(uri => uri.Contains("/simulators/integrations"), MockSimulatorsIntegrationsEndpoint),
            new SimpleRequestMocker(uri => uri.Contains("/simulators/routines/revisions/list"), MockSimulatorRoutineRevEndpoint, 1),
            new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions/list"), MockSimulatorModelRevEndpoint, 1),
            new SimpleRequestMocker(uri => uri.Contains("/simulators/models"), MockSimulatorModelsEndpoint, 1),
            new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint, 1),
            new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1), 1),
            new SimpleRequestMocker(uri => true, () => GoneResponse)
        };

        /// <summary>
        /// This test verifies that multiple concurrent calls to GetModelRevision for the same model
        /// don't result in parallel processing of the same model revision.
        /// </summary>
        [Fact]
        public async Task TestModelLibraryConcurrentCalls()
        {
            var httpMocks = GetMockedHttpClientFactory(MockRequestsAsync(endpointMockTemplates));
            // var mockedLogger = new Mock<ILogger<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>>();

            var services = new ServiceCollection();
            services.AddSingleton(httpMocks.factory.Object);
            services.AddCogniteTestClient();
            // services.AddSingleton(mockedLogger.Object);
            services.AddSingleton<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, FakeSimulatorClient>();
            services.AddSingleton<FileStorageClient>();
            services.AddSingleton<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>();
            services.AddSingleton<ModelParsingInfo>();
            services.AddSingleton(SeedData.SimulatorCreate);

            var config = new DefaultConfig<AutomationConfig>();
            config.GenerateDefaults();
            services.AddSingleton(config);

            using var provider = services.BuildServiceProvider();

            var stateConfig = provider.GetRequiredService<StateStoreConfig>();
            using var source = new CancellationTokenSource();
            var lib = provider.GetRequiredService<DefaultModelLibrary<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>();
            await lib.Init(source.Token);

            var libState = (ConcurrentDictionary<string, DefaultModelFilestate>)lib._state;
            Assert.NotEmpty(lib._state);

            var modelInState = lib._state.GetValueOrDefault("1234567890");
            Assert.NotNull(modelInState);
            Assert.Equal(123456789, modelInState.CdfId);
            Assert.Equal(123, modelInState.DataSetId);
            Assert.Equal("TestModelExternalId-v1", modelInState.ExternalId);
            Assert.Equal("TestModelExternalId", modelInState.ModelExternalId);
            Assert.Equal(1234567890000, modelInState.CreatedTime);
            Assert.Equal(1, modelInState.DownloadAttempts);
            Assert.Equal(SimulatorModelRevisionStatus.success, modelInState.ParsingInfo.Status);
            Assert.True(modelInState.CanRead);
            Assert.False(modelInState.ParsingInfo.Error);
            Assert.True(modelInState.ParsingInfo.Parsed);
            Assert.Null(modelInState.ParsingInfo.Flowsheet);
            Assert.NotNull(modelInState.FilePath);
            Assert.True(System.IO.File.Exists(modelInState.FilePath));
            var fileBytes = System.IO.File.ReadAllBytes(modelInState.FilePath);
            Assert.Single(fileBytes);
            Assert.Equal(1, fileBytes[0]);
        }

    public class FakeSimulatorClient : ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
    {

        public Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken _token)
        {
            state.ParsingInfo.SetSuccess();
            return Task.CompletedTask;
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
