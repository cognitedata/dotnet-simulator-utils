using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;
using Xunit;

using Cognite.Simulator.Utils;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class ExtPipelineUnitTest
    {

        public static HttpResponseMessage MockExtPipesEndpointWithErr(int n)
        {
            if (n == 0)
            {
                return CreateResponse(System.Net.HttpStatusCode.BadRequest, "Bad Request");
            }
            return MockExtPipesEndpoint(n);
        }

        public static HttpResponseMessage MockExtPipesEndpointWithEmptyRes(int n)
        {
            if (n == 0)
            {
                return CreateResponse(System.Net.HttpStatusCode.BadRequest, "Bad Request");
            }
            return OkItemsResponse("");
        }

        private static readonly IDictionary<Func<string, bool>, (Func<int, HttpResponseMessage> responseFunc, int callCount, int? maxCalls)> endpointMappings =
            new ConcurrentDictionary<Func<string, bool>, (Func<int, HttpResponseMessage>, int, int?)>(
                new Dictionary<Func<string, bool>, (Func<int, HttpResponseMessage>, int, int?)>
                {
                    { uri => uri.EndsWith("/token"), (MockAzureAADTokenEndpoint, 0, 1) },
                    { uri => uri.EndsWith("/extpipes/byids"), (MockExtPipesEndpointWithEmptyRes, 0, null) },
                    { uri => uri.EndsWith("/extpipes"), (MockExtPipesEndpointWithErr, 0, null) },
                    { uri => uri.EndsWith("/extpipes/runs"), (MockExtPipesEndpoint, 0, null) },
                }
            );

        [Fact]
        public async Task TestExtPipineRetryInitRemote()
        {
            var services = new ServiceCollection();
            services.AddSingleton(SeedData.SimulatorCreate);

            var httpMock = GetMockedHttpClientFactory(mockRequestsAsync(endpointMappings));
            var mockFactory = httpMock.factory;
            services.AddSingleton(mockFactory.Object);
            services.AddCogniteTestClient();

            var mockedLogger = new Mock<ILogger<ExtractionPipeline>>();
            services.AddSingleton(mockedLogger.Object);

            var connectorConfig = new ConnectorConfig
            {
                NamePrefix = SeedData.TestIntegrationExternalId,
                DataSetId = SeedData.TestDataSetId,
                PipelineNotification = new PipelineNotificationConfig(),
            };
            services.AddExtractionPipeline(connectorConfig);
            
            using var provider = services.BuildServiceProvider();

            var extPipeline = provider.GetRequiredService<ExtractionPipeline>();

            await extPipeline.Init(connectorConfig, CancellationToken.None);

            using var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                await extPipeline.PipelineUpdate(tokenSource.Token);
            } catch (OperationCanceledException) { }

            VerifyLog(mockedLogger, LogLevel.Warning, "Could not find an extraction pipeline with id utils-tests-pipeline, attempting to create one", Times.AtLeast(1), true);
            VerifyLog(mockedLogger, LogLevel.Warning, "Could not retrieve or create extraction pipeline from CDF: CogniteSdk.ResponseException: Bad Request", Times.AtLeast(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "Pipeline utils-tests-pipeline created successfully", Times.Once());
            VerifyLog(mockedLogger, LogLevel.Debug, "Notifying extraction pipeline, status: seen", Times.AtLeastOnce());
        }
    }
}
