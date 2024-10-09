using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public static class TestUtilities
    {
        private static int requestCount;

        public static (Mock<IHttpClientFactory> factory, Mock<HttpMessageHandler> handler) GetMockedHttpClientFactory(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> mockSendAsync)
        {
            var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                                                  ItExpr.IsAny<HttpRequestMessage>(),
                                                  ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(mockSendAsync);

            var client = new HttpClient(mockHttpMessageHandler.Object);
            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory.Setup(_ => _.CreateClient(It.IsAny<string>())).Returns(client);
            return (mockFactory, mockHttpMessageHandler);
        }

        public static void VerifyLog<TLogger>(Mock<ILogger<TLogger>> logger, LogLevel level, string expectedMessage, Times times, bool isContainsCheck = false)
        {
            logger.Verify(
                l => l.Log(
                    It.Is<LogLevel>(lvl => lvl == level),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        isContainsCheck ? v.ToString().Contains(expectedMessage) : v.ToString() == expectedMessage),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                times,
                $"Expected log message '{expectedMessage}' of level {level} to be logged {times} times"
            );
        }

        public static void WriteConfig()
        {
            var host = Environment.GetEnvironmentVariable("COGNITE_HOST");
            var project = Environment.GetEnvironmentVariable("COGNITE_PROJECT");
            var simulatorName = SeedData.TestSimulatorExternalId;
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
                    extraction-pipeline:
                        pipeline-id: {pipelineId}
                simulator:
                    name: {simulatorName}
                    data-set-id: {datasetId}

                connector:
                    status-interval: 3
                    name-prefix: {SeedData.TestIntegrationExternalId}
                    add-machine-name-suffix: false
                    model-library:
                        library-update-interval: 1";

            // Write the content to the file
            File.WriteAllText(filePath, yamlContent);
        }

        private static readonly Dictionary<Func<string, bool>, Func<HttpResponseMessage>> endpointMappings = new Dictionary<Func<string, bool>, Func<HttpResponseMessage>>
    {
        { uri => uri.Contains("/extpipes"), MockExtPipesEndpoint },
        { uri => uri.EndsWith("/simulators/list") || uri.EndsWith("/simulators"), MockSimulatorsEndpoint },
        { uri => uri.Contains("/simulators/integrations"), MockSimulatorsIntegrationsEndpoint }
    };

        public static async Task<HttpResponseMessage> mockSimintRequestsAsync(HttpRequestMessage message, CancellationToken token)
        {
            var uri = message.RequestUri?.ToString();
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            foreach (var mapping in endpointMappings)
            {
                if (mapping.Key(uri))
                {
                    return mapping.Value();
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") };
        }

        private static HttpResponseMessage MockExtPipesEndpoint()
        {
            var item = $@"{{
                ""externalId"": ""{SeedData.TestExtPipelineId}"",
                ""dataSetId"": 123,
                ""schedule"": ""Continuous"",
                ""source"": ""Test"",
                ""id"": 123,
            }}";
            return OkItemsResponse(item);
        }

        private static HttpResponseMessage MockSimulatorsEndpoint()
        {
            var item = $@"{{
                ""externalId"": ""{SeedData.TestSimulatorExternalId}"",
                ""name"": ""{SeedData.TestSimulatorExternalId}"",
                ""fileExtensionTypes"": [""csv""]
            }}";
            return OkItemsResponse(item);
        }

        private static HttpResponseMessage MockSimulatorsIntegrationsEndpoint()
        {
            if (requestCount > 0)
            {
                return CreateResponse(HttpStatusCode.Forbidden, "{\"error\": {\"code\": 403,\"message\": \"Forbidden\"}}");
            }
            requestCount++;
            var item = $@"{{
                ""externalId"": ""{SeedData.TestIntegrationExternalId}"",
                ""name"": ""Test connector integration"",
                ""dataSetId"": 123
            }}";
            return OkItemsResponse(item);
        }

        private static HttpResponseMessage OkItemsResponse(string item)
        {
            return CreateResponse(HttpStatusCode.OK, $"{{\"items\":[{item}]}}");
        }

        private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string content)
        {
            return new HttpResponseMessage(statusCode) { Content = new StringContent(content) };
        }
    }
}
