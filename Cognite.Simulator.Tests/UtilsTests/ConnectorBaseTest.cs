using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils;
using CogniteSdk;
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
        [Fact]
        public async Task TestConnectorBase()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddTransient<TestConnector>();
            services.AddSingleton<ExtractionPipeline>();
            //services.AddSingleton<RemoteConfigManager<RemoteConfig>>();
            //services.AddLogging();
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

            string? externalIdToDelete = null;
            try
            {
                var timestamp = DateTime.UtcNow.ToUnixTimeMilliseconds();
                await connector
                    .Init(source.Token)
                    .ConfigureAwait(false);

                externalIdToDelete = connector.GetSimulatorIntegartionExternalId("TestSim");
                Assert.NotNull(externalIdToDelete);

                var rowQuery = new SequenceRowQuery
                {
                    ExternalId = externalIdToDelete,
                };

                var rowsResult = await cdf.Sequences.ListRowsAsync(
                    rowQuery,
                    source.Token).ConfigureAwait(false);
                Assert.NotNull(rowsResult);
                Assert.NotEmpty(rowsResult.Rows);

                IDictionary<string, string> resultDict = rowsResult.Rows.ToDictionary(
                    r => r.GetStringValues()[0], r => r.GetStringValues()[1]);
                var heartbeat = Assert.Contains(SimulatorIntegrationSequenceRows.Heartbeat, resultDict);
                var connVersion = Assert.Contains(SimulatorIntegrationSequenceRows.ConnectorVersion, resultDict);
                var simDataset = Assert.Contains(SimulatorIntegrationSequenceRows.DataSetId, resultDict);
                var simVersion = Assert.Contains(SimulatorIntegrationSequenceRows.SimulatorVersion, resultDict);
                Assert.True(long.Parse(heartbeat) > timestamp);
                Assert.Equal(connector.GetConnectorVersion(), connVersion);
                Assert.Equal(CdfTestClient.TestDataset, long.Parse(simDataset));
                Assert.Equal("1.2.3", simVersion);

                // Start the connector loop and cancel it after 5 seconds. Should be enough time
                // to report a heartbeat back to CDF at least once.
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5));
                await connector.Run(linkedToken).ConfigureAwait(false);

                rowsResult = await cdf.Sequences.ListRowsAsync(
                    rowQuery,
                    source.Token).ConfigureAwait(false);

                resultDict = rowsResult.Rows.ToDictionary(
                    r => r.GetStringValues()[0], r => r.GetStringValues()[1]);
                var lastHeartbeat = Assert.Contains(SimulatorIntegrationSequenceRows.Heartbeat, resultDict);
                Assert.True(long.Parse(lastHeartbeat) > long.Parse(heartbeat));

                var pipelines = await cdf.ExtPipes.RetrieveAsync(
                    new List<string> { cdfConfig.ExtractionPipeline.PipelineId },
                    true,
                    source.Token).ConfigureAwait(false);
                //var configTest = cdf.ExtPipes.GetCurrentConfigAsync("symmetry-extraction-pipeline-kenneths", source.Token).ConfigureAwait(false);
                //Assert.NotNull(configTest);
                Assert.Contains(pipelines, p => p.ExternalId == cdfConfig.ExtractionPipeline.PipelineId);
                Assert.Contains(pipelines, p => p.LastSeen >= timestamp);
            }
            finally
            {
                if (externalIdToDelete != null)
                {
                    await cdf.Sequences
                        .DeleteAsync(new List<string> { externalIdToDelete }, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                await cdf.ExtPipes
                    .DeleteAsync(new []{ cdfConfig.ExtractionPipeline.PipelineId }, CancellationToken.None).ConfigureAwait(false); 
            }
        }
    }

    /// <summary>
    /// Implements a simple mock connector that only reports
    /// status back to CDF (Heartbeat)
    /// </summary>
    internal class TestConnector : ConnectorBase
    {
        private readonly ExtractionPipeline _pipeline;
        private readonly SimulatorConfig _config;
   
        public TestConnector(
            CogniteDestination cdf,
            ExtractionPipeline pipeline,
            SimulatorConfig config,
            ILogger<ConnectorBase> logger) : 
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
                logger)
        {
            _pipeline = pipeline;
            _config = config;
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
            await EnsureSimulatorIntegrationsSequencesExists(token).ConfigureAwait(false);
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
                var taskList = new List<Task> { Heartbeat(linkedToken), _pipeline.PipelineUpdate(linkedToken) };
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