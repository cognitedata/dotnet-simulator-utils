﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class ExtPipelineUnitTest
    {

        private static readonly List<SimpleRequestMocker> endpointMappings = new List<SimpleRequestMocker>
        {
            new SimpleRequestMocker(uri => uri.EndsWith("/token"), MockAzureAADTokenEndpoint).ShouldBeCalled(Times.Once()),
            new SimpleRequestMocker(uri => uri.EndsWith("/extpipes/byids"), MockBadRequest, 1),
            new SimpleRequestMocker(uri => uri.EndsWith("/extpipes/byids"), () => OkItemsResponse(""), 2),
            new SimpleRequestMocker(uri => uri.EndsWith("/extpipes"), MockBadRequest, 1),
            new SimpleRequestMocker(uri => uri.EndsWith("/extpipes"), MockExtPipesEndpoint, 1).ShouldBeCalled(Times.Once()),
            new SimpleRequestMocker(uri => uri.EndsWith("/extpipes/runs"), MockExtPipesEndpoint),
        };
        private static readonly ConnectorConfig ConnectorConfig = new ConnectorConfig
        {
            NamePrefix = SeedData.TestIntegrationExternalId,
            DataSetId = SeedData.TestDataSetId,
            PipelineNotification = new PipelineNotificationConfig(),
        };

        [Fact]
        /// <summary>
        /// Test the late initialization of the extraction pipeline.
        /// 1. On the first attempt it tries to get the pipeline, the /extpipes/byids fails with a 400.
        /// 2. Next try, the /extpipes/byids returns an empty response. So the connector will try to create the pipeline. Create fails with a 400.
        /// 3. Next try, the /extpipes/byids returns an empty response. The connector will try to create the pipeline again. Create succeeds.
        /// 4. The connector will then try to notify the pipeline, which should succeed.
        /// </summary>
        public async Task TestExtPipelineRetryInitRemote()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(SeedData.SimulatorCreate);

            var httpMock = GetMockedHttpClientFactory(MockRequestsAsync(endpointMappings));
            var mockFactory = httpMock.factory;
            services.AddSingleton(mockFactory.Object);
            services.AddCogniteTestClient();

            var mockedLogger = new Mock<ILogger<ExtractionPipeline>>();
            services.AddSingleton(mockedLogger.Object);

            services.AddExtractionPipeline(ConnectorConfig);

            using var provider = services.BuildServiceProvider();

            var extPipeline = provider.GetRequiredService<ExtractionPipeline>();

            // Act
            await extPipeline.Init(ConnectorConfig, CancellationToken.None);

            using var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));

            await Assert.ThrowsAsync<TaskCanceledException>(() => extPipeline.PipelineUpdate(tokenSource.Token));

            // Assert
            VerifyLog(mockedLogger, LogLevel.Warning, "Could not find an extraction pipeline with id utils-tests-pipeline, attempting to create one", Times.AtLeast(1), true);
            VerifyLog(mockedLogger, LogLevel.Warning, "Could not retrieve or create extraction pipeline from CDF: CogniteSdk.ResponseException: Bad Request", Times.AtLeast(1), true);
            VerifyLog(mockedLogger, LogLevel.Debug, "Pipeline utils-tests-pipeline created successfully", Times.Once());
            VerifyLog(mockedLogger, LogLevel.Debug, "Notifying extraction pipeline, status: seen", Times.AtLeastOnce());

            foreach (var mocker in endpointMappings)
            {
                mocker.AssertCallCount();
            }
        }

        private static readonly List<SimpleRequestMocker> endpointMappingsDisabled = new List<SimpleRequestMocker>
        {
            new SimpleRequestMocker(uri => uri.Contains("/extpipes"), MockExtPipesEndpoint).ShouldBeCalled(Times.Never()),
        };

        [Fact]
        /// <summary>
        /// Test extraction pipeline doesn't produce an error when disabled.
        /// </summary>
        public async Task TestExtPipelineDisabledNoError()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(SeedData.SimulatorCreate);

            var httpMock = GetMockedHttpClientFactory(MockRequestsAsync(endpointMappingsDisabled));
            var mockFactory = httpMock.factory;
            services.AddSingleton(mockFactory.Object);
            services.AddCogniteTestClient();

            var mockedLogger = new Mock<ILogger<ExtractionPipeline>>();
            services.AddSingleton(mockedLogger.Object);

            services.AddExtractionPipeline(ConnectorConfig);

            using var provider = services.BuildServiceProvider();

            CogniteConfig cdfConfig = provider.GetRequiredService<CogniteConfig>();
            cdfConfig.ExtractionPipeline = null; // disabling the extraction pipeline

            var extPipeline = provider.GetRequiredService<ExtractionPipeline>();

            // Act
            await extPipeline.Init(ConnectorConfig, CancellationToken.None);

            using var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));

            await extPipeline.PipelineUpdate(tokenSource.Token);

            // Assert
            VerifyLog(mockedLogger, LogLevel.Debug, "Extraction pipeline is not configured", Times.Once());

            foreach (var mocker in endpointMappingsDisabled)
            {
                mocker.AssertCallCount();
            }
        }
    }
}
