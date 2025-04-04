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

using Xunit;

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
                        isContainsCheck ? (v.ToString() ?? string.Empty).Contains(expectedMessage) : v.ToString() == expectedMessage)
                    ,
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
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

        public static HttpResponseMessage GoneResponse =
            CreateResponse(HttpStatusCode.Gone, "{\"error\": {\"code\": 410,\"message\": \"Gone\"}}");

        /// <summary>
        /// Mocks the requests to the endpoints with the given templates.
        /// Goes through the list of templates in order and returns the response from the first template that matches the request.
        /// If no template matches, returns a 501 Not Implemented response.
        /// If a template has a max call count, it will only be used that many times. After that, it will be skipped.
        /// </summary>
        /// <param name="endpointMockTemplates"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> MockRequestsAsync(IList<SimpleRequestMocker> endpointMockTemplates)
        {

            return (HttpRequestMessage message, CancellationToken token) =>
            {
                var uri = message.RequestUri?.ToString();
                if (uri == null)
                {
                    throw new ArgumentNullException(nameof(uri));
                }

                foreach (var mockTemplate in endpointMockTemplates)
                {
                    if (mockTemplate.Matches(uri))
                    {
                        return Task.FromResult(mockTemplate.GetResponse());
                    }
                }
                return Task.FromResult(CreateResponse(HttpStatusCode.NotImplemented, "Not implemented"));
            };
        }

        /// <summary>
        /// A container for easier mocking of endpoints.
        /// </summary>
        public class SimpleRequestMocker
        {
            private readonly Func<string, bool> _uriMatcher;
            private readonly Func<HttpResponseMessage> _responseFunc;
            private int _callCount = 0;
            private readonly int? _maxCalls = null;
            private Times _expectedCalls = Times.AtLeastOnce();

            /// <summary>
            /// Creates a new SimpleRequestMocker.
            /// </summary>
            /// <param name="uriMatcher">Function to match the URI of the request.</param>
            /// <param name="responseFunc">Function to generate the response.</param>
            /// <param name="maxCalls">Maximum number of times this mocker can be called. If null, there is no limit.</param>
            public SimpleRequestMocker(Func<string, bool> uriMatcher, Func<HttpResponseMessage> responseFunc, int? maxCalls = null)
            {
                _uriMatcher = uriMatcher;
                _responseFunc = responseFunc;
                _maxCalls = maxCalls;
            }

            /// <summary>
            /// Sets the expected number of calls to this mocker.
            /// After the test, you can call AssertCallCount to check if the number of calls was as expected.
            /// Default is AtLeastOnce.
            /// </summary>
            /// <param name="expectedCalls"></param>
            public SimpleRequestMocker ShouldBeCalled(Times expectedCalls)
            {
                _expectedCalls = expectedCalls;
                return this;
            }

            private bool HasMoreCalls()
            {
                return !_maxCalls.HasValue || _callCount < _maxCalls;
            }

            public bool Matches(string uri)
            {
                return _uriMatcher(uri) && HasMoreCalls();
            }

            public void AssertCallCount()
            {
                Assert.True(_expectedCalls.Validate(_callCount), $"Unexpected number of calls to endpoint. Expected {_expectedCalls} but was {_callCount}.");
            }

            public HttpResponseMessage GetResponse()
            {
                if (!HasMoreCalls())
                {
                    throw new InvalidOperationException("Maximum number of calls reached.");
                }
                _callCount++;
                return _responseFunc();
            }
        }

        public static HttpResponseMessage MockAzureAADTokenEndpoint()
        {
            return CreateResponse(HttpStatusCode.OK, "{\"access_token\": \"test_token\", \"expires_in\": 3600, \"token_type\": \"Bearer\"}");
        }

        public static HttpResponseMessage MockBadRequest()
        {
            return CreateResponse(HttpStatusCode.BadRequest, "Bad Request");
        }

        public static HttpResponseMessage MockExtPipesEndpoint()
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

        public static HttpResponseMessage MockTokenInspectEndpoint()
        {
            var project = Environment.GetEnvironmentVariable("COGNITE_PROJECT");
            var item = $@"{{
                ""subject"": ""test"",
                ""projects"": [
                    {{
                        ""projectUrlName"": ""{project}"",
                        ""groups"": [3746490342025788]
                    }}
                ]
            }}";
            return CreateResponse(HttpStatusCode.OK, item);
        }

        public static HttpResponseMessage MockSimulatorsEndpoint()
        {
            var item = $@"{{
                ""externalId"": ""{SeedData.TestSimulatorExternalId}"",
                ""name"": ""{SeedData.TestSimulatorExternalId}"",
                ""fileExtensionTypes"": [""csv""]
            }}";
            return OkItemsResponse(item);
        }

        public static HttpResponseMessage MockSimulatorsIntegrationsEndpoint()
        {
            var item = $@"{{
                ""externalId"": ""{SeedData.TestIntegrationExternalId}"",
                ""name"": ""Test connector integration"",
                ""dataSetId"": 123
            }}";
            return OkItemsResponse(item);
        }

        public static HttpResponseMessage MockSimulatorRoutineRevEndpoint()
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
