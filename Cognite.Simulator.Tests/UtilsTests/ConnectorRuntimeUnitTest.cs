using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Moq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

using Cognite.Simulator.Utils;
using CogniteSdk;
using CogniteSdk.Alpha;

using Cognite.Simulator.Utils.Automation;
using Cognite.Extractor.Logging;
using System.Net.Http;
using System.Net;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class ConnectorRuntimeUnitTest
    {

        public string ConnectorExternalId = SeedData.TestIntegrationExternalId;

        private static int requestCount = 0;

        private static async Task<HttpResponseMessage> mockSimintRequestsAsync(HttpRequestMessage message, CancellationToken token)
        {

            var uri = message.RequestUri?.ToString();
            if (uri != null && uri.Contains($"/extpipes"))
            {
                var item = $@"{{
                    ""externalId"": ""{SeedData.TestExtPipelineId}"",
                    ""name"": ""Test connector extraction pipeline"",
                    ""dataSetId"": 123,
                    ""schedule"": ""Continuous"",
                    ""source"": ""Test"",
                    ""createdBy"": ""unknown"",
                    ""id"": 123,
                    ""lastMessage"": ""Connector available"",
                }}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"items\":[{item}]}}") };
            }

            if (uri != null && (uri.EndsWith($"/simulators/list") || uri.EndsWith($"/simulators")))
            {
                var item = $@"{{
                    ""externalId"": ""{SeedData.TestSimulatorExternalId}"",
                    ""name"": ""{SeedData.TestSimulatorExternalId}"",
                    ""fileExtensionTypes"": [""csv""],
                }}";
                    
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"items\":[{item}]}}") };
            }

            if (uri != null && uri.Contains($"/simulators/integrations"))
            {
                // throw error if not the first time
                // if (requestCount > 0)
                // {
                //     return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"error\": {\"code\": 403,\"message\": \"Forbidden\"}}") };
                // }
                // requestCount++;
                var item = $@"{{
                    ""externalId"": ""{SeedData.TestIntegrationExternalId}"",
                    ""name"": ""Test connector integration"",
                    ""dataSetId"": 123,
                }}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"items\":[{item}]}}") };
            }

            if (uri != null && uri.Contains($"/simulators/routines/revisions/list"))
            {
                if (requestCount > 0)
                {
                    return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"error\": {\"code\": 403,\"message\": \"Forbidden\"}}") };
                }
                requestCount++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"items\":[]}}") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") };
        }
    

        [Fact]
        public async Task TestConnectorRuntimeWithRestart()
        {
            var services = new ServiceCollection()
                .AddSingleton<DefaultConfig<AutomationConfig>>()
                .AddCogniteTestClient()
                .BuildServiceProvider();
            var testCdfClient = services.GetRequiredService<Client>();
            TestUtilities.WriteConfig();

            var logger = LoggingUtils.GetDefault();
            var mockedLogger = new Mock<ILogger<DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>>();

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var mocks = TestUtilities.GetMockedHttpClientFactory(mockSimintRequestsAsync);
            var mockHttpMessageHandler = mocks.handler;
            var mockFactory = mocks.factory;

            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConfigureServices = (services) => {
                services.AddScoped<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, EmptySimulatorAutomationClient>();
                services.AddSingleton(mockFactory.Object); // inject the mock factory
                services.AddSingleton(mockedLogger.Object);
            };
            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConnectorName = "Empty";
            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.SimulatorDefinition = SeedData.SimulatorCreate;
            
            try {
                await DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.Run(logger, cts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) {}
        
            // check if restart happened by reading the "Restarting connector in" log message
            TestUtilities.VerifyLog(mockedLogger, LogLevel.Information, "Starting the connector...", Times.Once());
            TestUtilities.VerifyLog(mockedLogger, LogLevel.Information, "Connector can reach CDF!", Times.Once());
            TestUtilities.VerifyLog(mockedLogger, LogLevel.Debug, "Updating simulator definition", Times.Once());
            TestUtilities.VerifyLog(mockedLogger, LogLevel.Warning, "Restarting connector in 5 seconds", Times.AtLeastOnce());
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
                throw new NotImplementedException();
            }

            public string GetConnectorVersion()
            {
                return CommonUtils.GetAssemblyVersion();
            }

            public string GetSimulatorVersion()
            {
                return "2.0.1";
            }

            public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(
                DefaultModelFilestate modelState,
                SimulatorRoutineRevision routineRevision,
                Dictionary<string, SimulatorValueItem> inputData
            )
            {
                throw new NotImplementedException();
            }

            protected override void PreShutdown()
            {
                throw new NotImplementedException();
            }
        }

        internal class CalculatorRoutineAutomation : RoutineImplementationBase
        {
            public CalculatorRoutineAutomation(SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData) : base(routineRevision, inputData)
            {
            }

            public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments)
            {
                throw new NotImplementedException();
            }

            public override void RunCommand(Dictionary<string, string> arguments)
            {
            }

            public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments)
            {
            }
        }
    }
}
