using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;
using CogniteSdk;
using CogniteSdk.Alpha;

using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils.Automation;
using Microsoft.Extensions.Logging;
using Cognite.Extractor.Logging;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class ConnectorRuntimeTest
    {

        public class SampleModelFilestate : ModelStateBase
        {
            public SampleModelFilestate() : base()
            {
            }

            public override bool IsExtracted => false;
        }

        public class SampleModelFileStatePoco : ModelStateBasePoco
        {
            [StateStoreProperty("info-extracted")]
            public bool InformationExtracted { get; internal set; }
        }
        static void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<ISimulatorClient<SampleModelFilestate, SimulatorRoutineRevision>, CalculatorSimulatorAutomationClient>();
        }
        public class CustomAutomationConfig : AutomationConfig { }

        public Client TestCdfClient;

        public string ConnectorExternalId = SeedData.TestIntegrationExternalId;

        public async Task WriteConfig()
        {
            var host = Environment.GetEnvironmentVariable("COGNITE_HOST");
            var project = Environment.GetEnvironmentVariable("COGNITE_PROJECT");
            var tenant = Environment.GetEnvironmentVariable("AZURE_TENANT");
            var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            var secret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
            var simulatorName = SeedData.TestSimulatorExternalId;
            var datasetId = SeedData.TestDataSetId;
            string directory = Directory.GetCurrentDirectory();
            string filePath = Path.Combine(directory, "config.yml");
            string yamlContent = $@"
version: 1
logger:
  console:
    level: ""debug""
cognite:
  project:  {project}
  host: {host}
  idp-authentication:
    tenant: {tenant}
    client-id: {clientId}
    secret: {secret}
    scopes:
      - ""{host}/.default""
simulator:
  name: {simulatorName}
  data-set-id: {datasetId}

connector:
  status-interval: 3
  name-prefix: {ConnectorExternalId}
  add-machine-name-suffix: false
  api-logger:
    level: ""Information""
";

            // Write the content to the file
            System.IO.File.WriteAllText(filePath, yamlContent);

            // Optionally assert that the file was created
            Assert.True(System.IO.File.Exists(filePath), $"Failed to create {filePath}");
        }

        public async Task SetupTest() {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            using var provider = services.BuildServiceProvider();
            TestCdfClient = provider.GetRequiredService<Client>();

            await SeedData.GetOrCreateSimulator(TestCdfClient, SeedData.SimulatorCreate);
            await WriteConfig();
        }

        [Fact]
        public async Task TestConnectorRuntime()
        {
            await SetupTest();

            // Create an ILogger instance
            var logger = LoggingUtils.GetDefault();

            var FIVE_SECONDS = 5;
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(TimeSpan.FromSeconds(FIVE_SECONDS));

                DefaultConnectorRuntime<CustomAutomationConfig,SampleModelFilestate, SampleModelFileStatePoco>.ConfigureServices = ConfigureServices;
                DefaultConnectorRuntime<CustomAutomationConfig,SampleModelFilestate, SampleModelFileStatePoco>.ConnectorName = "Calculator";
                try
                {
                    await DefaultConnectorRuntime<CustomAutomationConfig,SampleModelFilestate, SampleModelFileStatePoco>.Run(logger, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Operation was cancelled.");
                }
                finally
                {
                    var integrations = await TestCdfClient.Alpha.Simulators.ListSimulatorIntegrationsAsync(
                        new SimulatorIntegrationQuery
                        {
                            Filter = new SimulatorIntegrationFilter() {
                                simulatorExternalIds = new List<string> { SeedData.TestSimulatorExternalId },
                            }
                        }
                    ).ConfigureAwait(false);
                    var existing = integrations.Items.FirstOrDefault(i => i.ExternalId == ConnectorExternalId);
                    Assert.True(existing.ExternalId == ConnectorExternalId);
                    await SeedData.DeleteSimulator(TestCdfClient, SeedData.TestSimulatorExternalId);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(FIVE_SECONDS + 1)).ConfigureAwait(false);
        }

        public class CalculatorSimulatorAutomationClient :
            AutomationClient,
            ISimulatorClient<SampleModelFilestate, SimulatorRoutineRevision>
        {

            private readonly ILogger<CalculatorSimulatorAutomationClient> _logger;

            public CalculatorSimulatorAutomationClient(
                ILogger<CalculatorSimulatorAutomationClient> logger,
                DefaultConfig<CustomAutomationConfig> config) : base(logger, config.Automation)
            {
                _logger = logger;
            }

            public Task ExtractModelInformation(SampleModelFilestate state, CancellationToken _token)
            {
                state.CanRead = false;
                state.ParsingInfo.SetFailure();
                return Task.CompletedTask;
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
                SampleModelFilestate modelState,
                SimulatorRoutineRevision routineRevision,
                Dictionary<string, SimulatorValueItem> inputData
            )
            {
                _logger.LogInformation("CalculatorClient Running a simulation");
                try
                {
                    Dictionary<string, SimulatorValueItem> result = new Dictionary<string, SimulatorValueItem>();
                    var routine = new CalculatorRoutineAutomation(routineRevision, inputData);
                    result = routine.PerformSimulation();
                    return Task.FromResult(result);
                }
                finally
                {
                }

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
                var resultItem = new SimulatorValueItem()
                    {
                        SimulatorObjectReference = new Dictionary<string, string> {
                        { "objectName", "a" },
                        { "objectProperty", "b" },
                    },
                    TimeseriesExternalId = outputConfig.SaveTimeseriesExternalId,
                    ReferenceId = outputConfig.ReferenceId,
                    ValueType = outputConfig.ValueType,
                    Value = SimulatorValue.Create("1.0"),
                };
                return resultItem;
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