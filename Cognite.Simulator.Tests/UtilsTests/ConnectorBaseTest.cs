using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class ConnectorBaseTest
    {
        /// <summary>
        /// Test that the connector can report status back to CDF
        /// It also checks whether extraction pipeline is created  
        /// </summary>
        [Fact]
        public async Task TestConnectorBase()
        {
            var timestamp = DateTime.UtcNow.ToUnixTimeMilliseconds();
            var simulatorName = $"TestSim {timestamp}";
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddSingleton<RemoteConfigManager<Utils.BaseConfig>>(provider => null!);
            services.AddSingleton<Utils.BaseConfig>();
            services.AddTransient<TestConnector>();
            services.AddSingleton<ExtractionPipeline>();
            services.AddSingleton<DefaultConfig<AutomationConfig>>();
            services.AddSingleton<ScopedRemoteApiSink<AutomationConfig>>();
            var simConfig = new SimulatorConfig
            {
                Name = simulatorName,
                DataSetId = SeedData.TestDataSetId
            };
            services.AddSingleton(simConfig);
            var pipeConfig = new PipelineNotificationConfig();
            services.AddSingleton(pipeConfig);
            using var provider = services.BuildServiceProvider();
            using var source = new CancellationTokenSource();

            var connector = provider.GetRequiredService<TestConnector>();
            var cdf = provider.GetRequiredService<Client>();
            var cdfConfig = provider.GetRequiredService<CogniteConfig>();

            // prepopulate the TestSim simulator
            await cdf.Alpha.Simulators.CreateAsync(
                new []
                {
                    new SimulatorCreate()
                        {
                            ExternalId = simulatorName,
                            Name = "TestSim",
                            FileExtensionTypes = new List<string> { "test" },
                            ModelTypes = new List<SimulatorModelType> {
                                new SimulatorModelType {
                                    Name = "Oil and Water Well",
                                    Key = "OilWell",
                                },
                                new SimulatorModelType {
                                    Name = "Dry and Wet Gas Well",
                                    Key = "GasWell",
                                },
                                new SimulatorModelType {
                                    Name = "Retrograde Condensate Well",
                                    Key = "RetrogradeWell",
                                },
                            },
                            StepFields = new List<SimulatorStepField> {
                                new SimulatorStepField {
                                    StepType = "get/set",
                                    Fields = new List<SimulatorStepFieldParam> {
                                        new SimulatorStepFieldParam {
                                            Name = "address",
                                            Label = "OpenServer Address",
                                            Info = "Enter the address of the PROSPER variable, i.e. PROSPER.ANL. SYS. Pres",
                                        },
                                    },
                                },
                                new SimulatorStepField {
                                    StepType = "command",
                                    Fields = new List<SimulatorStepFieldParam> {
                                        new SimulatorStepFieldParam {
                                            Name = "command",
                                            Label = "OpenServer Command",
                                            Info = "Enter the command to send to the PROSPER, i.e. Simulate",
                                        },
                                    },
                                },
                            },
                        }
                }
            ).ConfigureAwait(false);

            try
            {
                await connector
                    .Init(source.Token)
                    .ConfigureAwait(false);

                var integrationsRes = await cdf.Alpha.Simulators.ListSimulatorIntegrationsAsync(
                    new SimulatorIntegrationQuery(),
                    source.Token).ConfigureAwait(false);
                var integration = integrationsRes.Items.FirstOrDefault(i => i.SimulatorExternalId == simulatorName);

                Assert.NotNull(integration);
                Assert.Equal(simulatorName, integration.SimulatorExternalId);
                Assert.Equal("1.2.3", integration.SimulatorVersion);
                Assert.Equal(SeedData.TestDataSetId, integration.DataSetId);
                Assert.Equal("v0.0.1", integration.ConnectorVersion);
                Assert.StartsWith($"Test Connector", integration.ExternalId);
                Assert.True(integration.Heartbeat >= timestamp);

                // Start the connector loop and cancel it after 5 seconds. Should be enough time
                // to report a heartbeat back to CDF at least once.
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5));
                await connector.Run(linkedToken).ConfigureAwait(false);

                var pipelines = await cdf.ExtPipes.RetrieveAsync(
                    new List<string> { cdfConfig.ExtractionPipeline.PipelineId },
                    true,
                    source.Token).ConfigureAwait(false);
                Assert.Contains(pipelines, p => p.ExternalId == cdfConfig.ExtractionPipeline.PipelineId);
                Assert.Contains(pipelines, p => p.LastSeen >= timestamp);
            }
            finally
            {
                await cdf.Alpha.Simulators.DeleteAsync(
                    new [] { new Identity(simulatorName) },
                    source.Token).ConfigureAwait(false);
                await cdf.ExtPipes
                    .DeleteAsync(new []{ cdfConfig.ExtractionPipeline?.PipelineId }, CancellationToken.None).ConfigureAwait(false); 
            }
        }
    }


    /// <summary>
    /// Implements a simple mock connector that only reports
    /// status back to CDF (Heartbeat)
    /// </summary>
    internal class TestConnector : ConnectorBase<Utils.BaseConfig, AutomationConfig>
    {
        private readonly ExtractionPipeline _pipeline;
        private readonly SimulatorConfig _config;

        public TestConnector(
            CogniteDestination cdf,
            ExtractionPipeline pipeline,
            SimulatorConfig config,
            Microsoft.Extensions.Logging.ILogger<TestConnector> logger,
            RemoteConfigManager<Utils.BaseConfig> remoteConfigManager,
            ScopedRemoteApiSink<AutomationConfig> remoteSink) :
            base(
                cdf,
                new ConnectorConfig
                {
                    NamePrefix = $"Test Connector {DateTime.UtcNow.ToUnixTimeMilliseconds()}",
                    AddMachineNameSuffix = false,
                    StatusInterval = 2
                },
                new List<SimulatorConfig>
                {
                    config
                },
                logger,
                remoteConfigManager,
                remoteSink)
        {
            _pipeline = pipeline;
            _config = config;
        }

        public override string GetConnectorVersion()
        {
            return "v0.0.1";
        }

        public override string GetSimulatorVersion(string simulator)
        {
            return "1.2.3";
        }

        public override async Task Init(CancellationToken token)
        {
            await InitRemoteSimulatorIntegrations(token).ConfigureAwait(false);
            await UpdateRemoteSimulatorIntegrations(
                true,
                token).ConfigureAwait(false);
            await _pipeline.Init(_config, token).ConfigureAwait(false);
        }

        public override async Task Run(CancellationToken token)
        {
            try
            {
                var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                var linkedToken = linkedTokenSource.Token;
                var taskList = new List<Task> 
                { 
                    HeartbeatLoop(linkedToken),
                    _pipeline.PipelineUpdate(linkedToken)
                };
                await taskList.RunAll(linkedTokenSource).ConfigureAwait(false);
                linkedTokenSource.Dispose();
            }
            catch (OperationCanceledException)
            {
                // Heartbeat will run until the token is canceled. Exit without throwing in this case
                return;
            }
        }
    }
}