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
using Cognite.Simulator.Utils;
using CogniteSdk;
using CogniteSdk.Alpha;

using Cognite.Simulator.Utils.Automation;
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
        public class CustomAutomationConfig : AutomationConfig { }

        public string ConnectorExternalId = SeedData.TestIntegrationExternalId;

        private void WriteConfig()
        {
            var host = Environment.GetEnvironmentVariable("COGNITE_HOST");
            var project = Environment.GetEnvironmentVariable("COGNITE_PROJECT");
            var tenant = Environment.GetEnvironmentVariable("AZURE_TENANT");
            var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            var secret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
            var datasetId = SeedData.TestDataSetId;
            var pipelineId = SeedData.TestExtPipelineId;
            string directory = Directory.GetCurrentDirectory();
            string filePath = Path.Combine(directory, "config.yml");
            string yamlContent = $@"
version: 1
logger:
  console:
    level: ""debug""
  remote:
    level: ""information""
    enabled: true
cognite:
  project:  {project}
  host: {host}
  idp-authentication:
    tenant: {tenant}
    client-id: {clientId}
    secret: {secret}
    scopes:
      - ""{host}/.default""
  extraction-pipeline:
    pipeline-id: {pipelineId}

connector:
  status-interval: 3
  name-prefix: {ConnectorExternalId}
  add-machine-name-suffix: false
  data-set-id: {datasetId}
";

            // Write the content to the file
            System.IO.File.WriteAllText(filePath, yamlContent);

            // Optionally assert that the file was created
            Assert.True(System.IO.File.Exists(filePath), $"Failed to create {filePath}");
        }

        [Fact]
        public async Task TestConnectorRuntime()
        {
            var services = new ServiceCollection()
                .AddCogniteTestClient()
                .BuildServiceProvider();

            var testCdfClient = services.GetRequiredService<Client>();
            WriteConfig();

            // Create an ILogger instance
            var logger = LoggingUtils.GetDefault();

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                DefaultConnectorRuntime<CustomAutomationConfig, SampleModelFilestate, SampleModelFileStatePoco>.ConfigureServices = (services) =>
                {
                    services.AddScoped<ISimulatorClient<SampleModelFilestate, SimulatorRoutineRevision>, CalculatorSimulatorAutomationClient>();
                };
                DefaultConnectorRuntime<CustomAutomationConfig, SampleModelFilestate, SampleModelFileStatePoco>.ConnectorName = "Calculator";
                DefaultConnectorRuntime<CustomAutomationConfig, SampleModelFilestate, SampleModelFileStatePoco>.SimulatorDefinition = SeedData.SimulatorCreate;

                try
                {
                    await DefaultConnectorRuntime<CustomAutomationConfig, SampleModelFilestate, SampleModelFileStatePoco>.Run(logger, cts.Token);
                }
                catch (OperationCanceledException) { }

                var integrations = await testCdfClient.Alpha.Simulators.ListSimulatorIntegrationsAsync(
                    new SimulatorIntegrationQuery
                    {
                        Filter = new SimulatorIntegrationFilter()
                        {
                            SimulatorExternalIds = new List<string> { SeedData.TestSimulatorExternalId },
                        }
                    }
                );

                var simulators = await testCdfClient.Alpha.Simulators.ListAsync(new SimulatorQuery());

                var unitQuantities = SeedData.SimulatorCreate.UnitQuantities;
                Assert.Contains(ConnectorExternalId, integrations.Items.Select(i => i.ExternalId));
                var existingIntegration = integrations.Items.FirstOrDefault(i => i.ExternalId == ConnectorExternalId);

                var simulatorDefinition = simulators.Items.FirstOrDefault(i => i.ExternalId == SeedData.TestSimulatorExternalId);
                Assert.NotNull(simulatorDefinition);

                // Simply checking the unit quantities created
                Assert.Equal(simulatorDefinition.UnitQuantities.ToString(), unitQuantities.ToString());
            }
            finally
            {
                await SeedData.DeleteSimulator(testCdfClient, SeedData.TestSimulatorExternalId);
                try
                {
                    await testCdfClient.ExtPipes.DeleteAsync(new List<string> { SeedData.TestExtPipelineId });
                }
                catch (Exception) { }
            }
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

            public string GetConnectorVersion(CancellationToken _token)
            {
                return CommonUtils.GetAssemblyVersion();
            }

            public string GetSimulatorVersion(CancellationToken _token)
            {
                return "2.0.1";
            }

            public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(
                SampleModelFilestate modelState,
                SimulatorRoutineRevision routineRevision,
                Dictionary<string, SimulatorValueItem> inputData,
                CancellationToken token
            )
            {
                _logger.LogInformation("CalculatorClient Running a simulation");
                try
                {
                    Dictionary<string, SimulatorValueItem> result = new Dictionary<string, SimulatorValueItem>();
                    var routine = new CalculatorRoutineAutomation(routineRevision, inputData, _logger);
                    result = routine.PerformSimulation(token);
                    return Task.FromResult(result);
                }
                finally
                {
                }

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

            public override void RunCommand(Dictionary<string, string> arguments, CancellationToken token)
            {
            }

            public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments, CancellationToken token)
            {
            }
        }
    }
}