using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.Logging;
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
    [Collection(nameof(SequentialTestCollection))]
    public class SimulationRunnerUnitTest
    {
        /// <summary>
        /// Tests that when network error occurs during UpdateSimulationRunStatus callback,
        /// the HttpRequestException is caught by DefaultConnectorRuntime and handled gracefully.
        /// This simulates internet disconnection during the run status update.
        /// </summary>
        [Fact]
        public async Task TestSimulationRunner_CallbackNetworkError_HandledGracefully()
        {
            var runsListCallCount = 0;

            var networkMocks = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.EndsWith("/token/inspect"), MockTokenInspectEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/extpipes"), MockExtPipesEndpoint),
                new SimpleRequestMocker(uri => uri.EndsWith("/simulators/list") || uri.EndsWith("/simulators") || uri.EndsWith("/simulators/update"), MockSimulatorsEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/integrations/list"), MockSimulatorIntegrationsListEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/integrations/update"), MockSimulatorsIntegrationsEndpoint),

                new SimpleRequestMocker(uri => uri.Contains("/simulators/runs/list"), () =>
                {
                    runsListCallCount++;
                    if (runsListCallCount == 1)
                    {
                        return MockSimulationRunsListEndpoint();
                    }
                    return MockSimulationRunsListEmptyEndpoint();
                }),

                new SimpleRequestMocker(uri => uri.Contains("/simulators/run/callback"), () =>
                {
                    throw new HttpRequestException("Network error during callback update");
                }),

                new SimpleRequestMocker(uri => uri.Contains("/simulators/routines/revisions/list") || uri.Contains("/simulators/routines/revisions/byids"), MockSimulatorRoutineRevWithIntegrationEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions"), MockSimulatorModelRevListEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1)),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/logs"), () => OkItemsResponse("{}")),
            };

            Environment.SetEnvironmentVariable("COGNITE_HOST", "https://api.cognitedata.com");
            Environment.SetEnvironmentVariable("COGNITE_PROJECT", "test-project");

            WriteConfig();

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            var mockedLogger = new Mock<ILogger<DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>>();
            var mockedSimulationRunnerLogger = new Mock<ILogger<DefaultSimulationRunner<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>>();
            var mockedConnectorLogger = new Mock<ILogger<DefaultConnector<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>>();
            var mockFactory = GetMockedHttpClientFactory(MockRequestsAsync(networkErrorMocks));

            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConfigureServices = (services) =>
            {
                services.AddScoped<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, EmptySimulatorAutomationClient>();
                services.AddSingleton(mockFactory.Object);
                services.AddSingleton(mockedLogger.Object);
                services.AddSingleton(mockedSimulationRunnerLogger.Object);
                services.AddSingleton(mockedConnectorLogger.Object);
            };
            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConnectorName = "Empty";
            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.SimulatorDefinition = SeedData.SimulatorCreate;

            try
            {
                await DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.Run(mockedLogger.Object, cts.Token);
            }
            catch (OperationCanceledException) { }

            VerifyLog(mockedSimulationRunnerLogger, LogLevel.Information, "simulation runs(s) ready to run found in CDF", Times.AtLeastOnce(), true);
            VerifyLog(mockedSimulationRunnerLogger, LogLevel.Warning, $"Simulation run {SeedData.TestSimulationRunId} failed with error:", Times.AtLeastOnce(), true);
            VerifyLog(mockedSimulationRunnerLogger, LogLevel.Warning, "Network error during callback update", Times.AtLeastOnce(), true);
            VerifyLog(mockedConnectorLogger, LogLevel.Information, "Connector started", Times.AtLeast(2), true);
        }

    }
}
