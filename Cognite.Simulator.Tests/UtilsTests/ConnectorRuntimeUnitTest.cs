using System;
using System.Collections.Concurrent;
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
    public class ConnectorRuntimeUnitTest
    {
        private static readonly IList<SimpleRequestMocker> endpointMockTemplates = new List<SimpleRequestMocker>
        {
            new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint),
            new SimpleRequestMocker(uri => uri.Contains("/extpipes"), MockExtPipesEndpoint),
            new SimpleRequestMocker(uri => uri.EndsWith("/simulators/list") || uri.EndsWith("/simulators") || uri.EndsWith("/simulators/update"), MockSimulatorsEndpoint),
            new SimpleRequestMocker(uri => uri.Contains("/simulators/integrations"), MockSimulatorsIntegrationsEndpoint),
            new SimpleRequestMocker(uri => uri.Contains("/simulators/routines/revisions/list"), MockSimulatorRoutineRevEndpoint, 1),
            new SimpleRequestMocker(uri => true, () => GoneResponse)
        };

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

            var mocks = GetMockedHttpClientFactory(MockRequestsAsync(endpointMockTemplates));
            var mockFactory = mocks.factory;

            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConfigureServices = (services) =>
            {
                services.AddScoped<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, EmptySimulatorAutomationClient>();
                services.AddSingleton(mockFactory.Object);
                services.AddSingleton(mockedLogger.Object);
            };
            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConnectorName = "Empty";
            DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.SimulatorDefinition = SeedData.SimulatorCreate;

            try
            {
                await DefaultConnectorRuntime<DefaultAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.Run(logger, cts.Token);
            }
            catch (OperationCanceledException) { }

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
            public CalculatorRoutineAutomation(SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger)
            {
            }

            public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments, CancellationToken token)
            {
                throw new NotImplementedException();
            }

            public override void RunCommand(Dictionary<string, string> arguments, CancellationToken token)
            {
            }

            public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments, CancellationToken token)
            {
            }
        }
    }
}
