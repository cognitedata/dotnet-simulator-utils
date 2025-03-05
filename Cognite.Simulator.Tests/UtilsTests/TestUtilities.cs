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

                connector:
                    status-interval: 3
                    name-prefix: {SeedData.TestIntegrationExternalId}
                    add-machine-name-suffix: false
                    data-set-id: {datasetId}
                    model-library:
                        library-update-interval: 1";

            // Write the content to the file
            File.WriteAllText(filePath, yamlContent);
        }

        private static HttpResponseMessage GoneResponse =
            CreateResponse(HttpStatusCode.Gone, "{\"error\": {\"code\": 410,\"message\": \"Gone\"}}");

        /// <summary>
        /// Mocks the HttpClientFactory to return the mocked responses
        /// Example format for endpointMappings:
        ///     { uri => uri.Contains("/extpipes"), (MockExtPipesEndpoint, 0, 2) },
        ///     2 is the number of times the response function will be called before returning a 410 Gone
        ///     if maxCalls is null, the response function will return the same response indefinitely
        /// </summary>
        /// <param name="endpointMappings">Dictionary of URL matchers and response functions</param>
        public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> mockRequestsAsync(IDictionary<Func<string, bool>, (Func<int, HttpResponseMessage> responseFunc, int callCount, int? maxCalls)> endpointMappings)
        {
            
            return async (HttpRequestMessage message, CancellationToken token) =>
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
                        var (responseFunc, callCount, maxCalls) = mapping.Value;
                        if (maxCalls.HasValue && callCount >= maxCalls)
                        {
                            return GoneResponse;
                        }
                        endpointMappings[mapping.Key] = (responseFunc, callCount + 1, maxCalls);
                        return responseFunc(callCount);
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") };
            };
        }

        public static HttpResponseMessage MockAzureAADTokenEndpoint(int n)
        {
            return CreateResponse(HttpStatusCode.OK, "{\"access_token\": \"test_token\", \"expires_in\": 3600, \"token_type\": \"Bearer\"}");
        }

        public static HttpResponseMessage MockExtPipesEndpoint(int n)
        {
            var item = $@"{{
                ""externalId"": ""{SeedData.TestExtPipelineId}"",
                ""name"": ""Test connector extraction pipeline"",
                ""dataSetId"": 123,
                ""schedule"": ""Continuous"",
                ""source"": ""Test"",
                ""createdBy"": ""unknown"",
                ""id"": 123,
                ""lastMessage"": ""Connector available""
            }}";
            return OkItemsResponse(item);
        }

        public static HttpResponseMessage MockSimulatorsEndpoint(int n)
        {
            var item = $@"{{
                ""externalId"": ""{SeedData.TestSimulatorExternalId}"",
                ""name"": ""{SeedData.TestSimulatorExternalId}"",
                ""fileExtensionTypes"": [""csv""]
            }}";
            return OkItemsResponse(item);
        }

        public static HttpResponseMessage MockSimulatorsIntegrationsEndpoint(int n)
        {
            var item = $@"{{
                ""externalId"": ""{SeedData.TestIntegrationExternalId}"",
                ""name"": ""Test connector integration"",
                ""dataSetId"": 123
            }}";
            return OkItemsResponse(item);
        }

        public static HttpResponseMessage MockSimulatorRoutineRevEndpoint(int n)
        {
            var item = $@"{{
                ""externalId"": ""{SeedData.TestIntegrationExternalId}"",
                ""name"": ""Test connector integration"",
                ""dataSetId"": 123,
                ""createdTime"": 1234567890000
            }}";
            return OkItemsResponse(item);
        }

        public static HttpResponseMessage OkItemsResponse(string item)
        {
            return CreateResponse(HttpStatusCode.OK, $"{{\"items\":[{item}]}}");
        }

        public static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string content)
        {
            return new HttpResponseMessage(statusCode) { Content = new StringContent(content) };
        }
    }
}
