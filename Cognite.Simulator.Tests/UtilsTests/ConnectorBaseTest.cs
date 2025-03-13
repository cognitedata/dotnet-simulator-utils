using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk;
using CogniteSdk.Alpha;

using Microsoft.Extensions.DependencyInjection;

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
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddSingleton(SeedData.SimulatorCreate);
            services.AddSingleton<RemoteConfigManager<Utils.BaseConfig>>(provider => null!);
            services.AddSingleton<Utils.BaseConfig>();
            services.AddTransient<TestConnector>();
            services.AddSingleton<ExtractionPipeline>();
            services.AddSingleton<DefaultConfig<AutomationConfig>>();
            services.AddSingleton<ScopedRemoteApiSink>();
            var pipeConfig = new PipelineNotificationConfig();
            services.AddSingleton(pipeConfig);
            using var provider = services.BuildServiceProvider();
            using var source = new CancellationTokenSource();

            var connector = provider.GetRequiredService<TestConnector>();
            var cdf = provider.GetRequiredService<Client>();
            var cdfConfig = provider.GetRequiredService<CogniteConfig>();

            await SeedData.GetOrCreateSimulator(cdf, SeedData.SimulatorCreate);

            try
            {
                await connector
                    .Init(source.Token)
;

                var integrationsRes = await cdf.Alpha.Simulators.ListSimulatorIntegrationsAsync(
                    new SimulatorIntegrationQuery(),
                    source.Token);
                var integration = integrationsRes.Items.FirstOrDefault(i => i.SimulatorExternalId == SeedData.TestSimulatorExternalId);

                Assert.NotNull(integration);
                Assert.Equal(SeedData.TestSimulatorExternalId, integration.SimulatorExternalId);
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
                await connector.Run(linkedToken);

                var pipelines = await cdf.ExtPipes.RetrieveAsync(
                    new List<string> { cdfConfig.ExtractionPipeline.PipelineId },
                    true,
                    source.Token);
                Assert.Contains(pipelines, p => p.ExternalId == cdfConfig.ExtractionPipeline.PipelineId);
                Assert.Contains(pipelines, p => p.LastSeen >= timestamp);
            }
            finally
            {
                await SeedData.DeleteSimulator(cdf, SeedData.TestSimulatorExternalId);
                try
                {
                    await cdf.ExtPipes
                        .DeleteAsync(new[] { cdfConfig.ExtractionPipeline?.PipelineId }, CancellationToken.None);
                }
                catch (Exception) { }
            }
        }
    }


    /// <summary>
    /// Implements a simple mock connector that only reports
    /// status back to CDF (Heartbeat)
    /// </summary>
    internal class TestConnector : ConnectorBase<Utils.BaseConfig>
    {
        static ConnectorConfig TestConnectorConfig = new ConnectorConfig()
        {
            NamePrefix = $"Test Connector {DateTime.UtcNow.ToUnixTimeMilliseconds()}",
            AddMachineNameSuffix = false,
            StatusInterval = 2,
            DataSetId = SeedData.TestDataSetId,
        };

        private readonly ExtractionPipeline _pipeline;

        public TestConnector(
            CogniteDestination cdf,
            ExtractionPipeline pipeline,
            SimulatorCreate simulatorDefinition,
            Microsoft.Extensions.Logging.ILogger<TestConnector> logger,
            RemoteConfigManager<Utils.BaseConfig> remoteConfigManager,
            ScopedRemoteApiSink remoteSink) :
            base(
                cdf,
                TestConnectorConfig,
                simulatorDefinition,
                logger,
                remoteConfigManager,
                remoteSink)
        {
            _pipeline = pipeline;
        }

        public override string GetConnectorVersion(CancellationToken _token)
        {
            return "v0.0.1";
        }

        public override string GetSimulatorVersion(string simulator, CancellationToken _token)
        {
            return "1.2.3";
        }

        public override async Task Init(CancellationToken token)
        {
            await InitRemoteSimulatorIntegration(token).ConfigureAwait(false);
            await UpdateRemoteSimulatorIntegration(true, token).ConfigureAwait(false);
            await _pipeline.Init(TestConnectorConfig, token).ConfigureAwait(false);
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