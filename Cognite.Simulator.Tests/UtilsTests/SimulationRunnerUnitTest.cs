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
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var networkErrorMocks = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
                new SimpleRequestMocker(uri => uri.EndsWith("/token/inspect"), MockTokenInspectEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/extpipes"), MockExtPipesEndpoint),
                new SimpleRequestMocker(uri => uri.EndsWith("/simulators/list") || uri.EndsWith("/simulators") || uri.EndsWith("/simulators/update"), MockSimulatorsEndpoint),

                new SimpleRequestMocker(uri => uri.Contains("/simulators/integrations/list"), () =>
                {
                    var item = $@"{{
                        ""id"": 999,
                        ""externalId"": ""{SeedData.TestIntegrationExternalId}"",
                        ""simulatorExternalId"": ""{SeedData.TestSimulatorExternalId}"",
                        ""dataSetId"": {SeedData.TestDataSetId}
                    }}";
                    return OkItemsResponse(item);
                }),

                new SimpleRequestMocker(uri => uri.Contains("/simulators/integrations/update"), () =>
                {
                    return MockSimulatorsIntegrationsEndpoint();
                }),

                new SimpleRequestMocker(uri => uri.Contains("/simulators/runs/list"), () =>
                {
                    runsListCallCount++;
                    if (runsListCallCount == 1)
                    {
                        var item = $@"{{
                            ""id"": 12345,
                            ""status"": ""ready"",
                            ""simulatorExternalId"": ""{SeedData.TestSimulatorExternalId}"",
                            ""simulatorIntegrationExternalId"": ""{SeedData.TestIntegrationExternalId}"",
                            ""routineRevisionExternalId"": ""test-routine-rev"",
                            ""modelRevisionExternalId"": ""test-model-rev"",
                            ""routineExternalId"": ""test-routine"",
                            ""runType"": ""external"",
                            ""createdTime"": {now},
                            ""lastUpdatedTime"": {now}
                        }}";
                        return OkItemsResponse(item);
                    }
                    return OkItemsResponse("");
                }),

                new SimpleRequestMocker(uri => uri.Contains("/simulators/run/callback"), () =>
                {
                    throw new HttpRequestException("Network error during callback update");
                }),

                new SimpleRequestMocker(uri => uri.Contains("/simulators/routines/revisions/list") || uri.Contains("/simulators/routines/revisions/byids"), () =>
                {
                    var item = $@"{{
                        ""id"": 123,
                        ""externalId"": ""test-routine-rev"",
                        ""routineExternalId"": ""test-routine"",
                        ""simulatorExternalId"": ""{SeedData.TestSimulatorExternalId}"",
                        ""simulatorIntegrationExternalId"": ""{SeedData.TestIntegrationExternalId}"",
                        ""modelExternalId"": ""test-model"",
                        ""name"": ""Test routine revision"",
                        ""dataSetId"": 123,
                        ""createdTime"": {now},
                        ""lastUpdatedTime"": {now},
                        ""configuration"": {{}}
                    }}";
                    return OkItemsResponse(item);
                }),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/models/revisions"), () =>
                {
                    var item = $@"{{
                        ""id"": 100,
                        ""externalId"": ""test-model-rev"",
                        ""simulatorExternalId"": ""{SeedData.TestSimulatorExternalId}"",
                        ""modelExternalId"": ""test-model"",
                        ""fileId"": 100,
                        ""createdTime"": {now},
                        ""lastUpdatedTime"": {now}
                    }}";
                    return OkItemsResponse(item);
                }),
                new SimpleRequestMocker(uri => uri.Contains("/files/byids"), MockFilesByIdsEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/files/downloadlink"), MockFilesDownloadLinkEndpoint),
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), () => MockFilesDownloadEndpoint(1)),
                new SimpleRequestMocker(uri => uri.Contains("/simulators/logs"), () => OkItemsResponse("{}")),
            };

            Environment.SetEnvironmentVariable("COGNITE_HOST", "https://bluefile.cognitedata.net");
            Environment.SetEnvironmentVariable("COGNITE_PROJECT", "testProject");

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

            VerifyLog(mockedSimulationRunnerLogger, LogLevel.Warning, "Simulation run 12345 failed with error:", Times.AtLeastOnce(), true);
            VerifyLog(mockedSimulationRunnerLogger, LogLevel.Warning, "Network error during callback update", Times.AtLeastOnce(), true);
            VerifyLog(mockedConnectorLogger, LogLevel.Information, "Connector started", Times.AtLeast(2), true);
        }


        private class EmptySimulatorAutomationClient :
            AutomationClient,
            ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
        {
            public EmptySimulatorAutomationClient(
                ILogger<EmptySimulatorAutomationClient> logger,
                DefaultConfig<DefaultAutomationConfig> config) : base(logger, config.Automation)
            {
            }

            public Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken _token)
            {
                return Task.CompletedTask;
            }

            public string GetConnectorVersion(CancellationToken _token)
            {
                return CommonUtils.GetAssemblyVersion();
            }

            public string GetSimulatorVersion(CancellationToken _token)
            {
                return "2.0.1";
            }

            public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(
                DefaultModelFilestate modelState,
                SimulatorRoutineRevision routineRevision,
                Dictionary<string, SimulatorValueItem> inputData,
                CancellationToken token
            )
            {
                return Task.FromResult(new Dictionary<string, SimulatorValueItem>());
            }

            public Task TestConnection(CancellationToken token)
            {
                return Task.CompletedTask;
            }

            protected override void PreShutdown()
            {
                throw new NotImplementedException();
            }
        }
    }
}
