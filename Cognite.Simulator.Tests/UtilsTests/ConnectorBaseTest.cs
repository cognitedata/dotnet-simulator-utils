using Cognite.Extractor.Common;
using Cognite.Extractor.Logging;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils;
using CogniteSdk;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddLogger();
            services.AddSingleton<RemoteConfigManager<BaseConfig>>(provider => null!);
            services.AddSingleton<BaseConfig>();
            services.AddTransient<TestConnector>();
            services.AddSingleton<ExtractionPipeline>();
            var simConfig = new SimulatorConfig
            {
                Name = "TestSim",
                DataSetId = CdfTestClient.TestDataset
            };
            services.AddSingleton(simConfig);
            var pipeConfig = new PipelineNotificationConfig();
            services.AddSingleton(pipeConfig);
            using var provider = services.BuildServiceProvider();
            using var source = new CancellationTokenSource();

            var connector = provider.GetRequiredService<TestConnector>();
            var cdf = provider.GetRequiredService<Client>();
            var cdfConfig = provider.GetRequiredService<CogniteConfig>();

            try
            {
                var timestamp = DateTime.UtcNow.ToUnixTimeMilliseconds();
                await connector
                    .Init(source.Token)
                    .ConfigureAwait(false);

                var integrationsRes = await cdf.Alpha.Simulators.ListSimulatorIntegrationsAsync(
                    new SimulatorIntegrationQuery(),
                    source.Token).ConfigureAwait(false);
                var integration = integrationsRes.Items.FirstOrDefault(i => i.SimulatorExternalId == "TestSim");

                Assert.NotNull(integration);
                Assert.Equal("TestSim", integration.SimulatorExternalId);
                Assert.Equal("1.2.3", integration.SimulatorVersion);
                Assert.Equal(CdfTestClient.TestDataset, integration.DataSetId);
                Assert.Equal("v0.0.1", integration.ConnectorVersion);
                Assert.Equal("Test Connector", integration.ExternalId);
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
                // print cdfConfig.ExtractionPipeline.PipelineId
                // Console.WriteLine("--------------------- " + cdfConfig.ExtractionPipeline?.PipelineId);
                await cdf.ExtPipes
                    .DeleteAsync(new []{ cdfConfig.ExtractionPipeline?.PipelineId }, CancellationToken.None).ConfigureAwait(false); 
            }
        }
    }


    /// <summary>
    /// Implements a simple mock connector that only reports
    /// status back to CDF (Heartbeat)
    /// </summary>
    internal class TestConnector : ConnectorBase<BaseConfig>
    {
        private readonly ExtractionPipeline _pipeline;
        private readonly RemoteConfigManager<BaseConfig> _remoteConfigManager;
        private readonly SimulatorConfig _config;


        public TestConnector(
            CogniteDestination cdf,
            ExtractionPipeline pipeline,
            SimulatorConfig config,
            ILogger<TestConnector> logger,
            RemoteConfigManager<BaseConfig> remoteConfigManager) :
            base(
                cdf,
                new ConnectorConfig
                {
                    NamePrefix = "Test Connector",
                    AddMachineNameSuffix = false
                },
                new List<SimulatorConfig>
                {
                    config
                },
                logger,
                remoteConfigManager)
        {
            _pipeline = pipeline;
            _config = config;
            _remoteConfigManager = remoteConfigManager;
        }

        public override string GetConnectorVersion()
        {
            return "v0.0.1";
        }

        public override TimeSpan GetHeartbeatInterval()
        {
            return TimeSpan.FromSeconds(2);
        }

        public override string GetSimulatorVersion(string simulator)
        {
            return "1.2.3";
        }

        public override async Task Init(CancellationToken token)
        {
            await EnsureSimulatorIntegrationsExists(token).ConfigureAwait(false);
            await UpdateIntegrationRows(
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
                    Heartbeat(linkedToken),
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