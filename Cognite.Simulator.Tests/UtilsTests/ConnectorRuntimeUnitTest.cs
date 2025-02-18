using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Moq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

using Cognite.Simulator.Utils;
using CogniteSdk.Alpha;

using Cognite.Simulator.Utils.Automation;
using Cognite.Extractor.Logging;
using System.Net.Http;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class ConnectorRuntimeUnitTest
    {
        private static readonly IDictionary<Func<string, bool>, (Func<HttpResponseMessage> responseFunc, int callCount, int? maxCalls)> endpointMappings =
            new ConcurrentDictionary<Func<string, bool>, (Func<HttpResponseMessage>, int, int?)>(
                new Dictionary<Func<string, bool>, (Func<HttpResponseMessage>, int, int?)>
                {
                    { uri => uri.Contains("/extpipes"), (MockExtPipesEndpoint, 0, null) },
                    { uri => uri.EndsWith("/simulators/list") || uri.EndsWith("/simulators"), (MockSimulatorsEndpoint, 0, null) },
                    { uri => uri.Contains("/simulators/integrations"), (MockSimulatorsIntegrationsEndpoint, 0, null) },
                    { uri => uri.Contains("/simulators/routines/revisions/list"), (MockSimulatorRoutineRevEndpoint, 0, 1) }
                }
            );

        // We need to mock the HttpClientFactory to return the mocked responses
        // First few requests return the mocked responses, then we return a 410 Gone
        // This should cause a "soft" restart of the connector
        [Fact]
        public async Task TestConnectorRuntimeWithRestart()
        {
            WriteConfig();

            var logger = LoggingUtils.GetDefault();
            var mockedLogger = new Mock<ILogger<DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>>>();

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var mocks = GetMockedHttpClientFactory(mockRequestsAsync(endpointMappings));
            var mockFactory = mocks.factory;

            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConfigureServices = (services) => {
                services.AddScoped<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, EmptySimulatorAutomationClient>();
                services.AddSingleton(mockFactory.Object);
                services.AddSingleton(mockedLogger.Object);
            };
            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConnectorName = "Empty";
            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.SimulatorDefinition = SeedData.SimulatorCreate;
            
            try {
                await DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.Run(logger, cts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) {}

            // Check the logs, it should first succeed on the startup, then fail and restart
            VerifyLog(mockedLogger, LogLevel.Information, "Starting the connector...", Times.Once());
            VerifyLog(mockedLogger, LogLevel.Information, "Connector can reach CDF!", Times.Once());
            VerifyLog(mockedLogger, LogLevel.Debug, "Updating simulator definition", Times.Once());
            VerifyLog(mockedLogger, LogLevel.Error, "Request to CDF failed with code 410", Times.Once(), true);
            VerifyLog(mockedLogger, LogLevel.Warning, "Restarting connector in 10 seconds", Times.AtLeastOnce());
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

            public Task TestConnection(CancellationToken token)
            {
                return Task.CompletedTask;
            }

            protected override void PreShutdown()
            {
                throw new NotImplementedException();
            }
        }

        internal class CalculatorRoutineAutomation : RoutineImplementationBase
        {
            public CalculatorRoutineAutomation(SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger )
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
