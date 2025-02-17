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
using Moq;
using System.Net.Http;
using System.Net;
using Polly.Extensions.Http;
using Polly;
using Cognite.Extractor.Utils;

namespace Cognite.Simulator.Tests.UtilsTests
{
public class RetryPolicyTests
{
    private const string TestHost = "https://api.cognite.com";
    private static int _retryCount = 0;
    private static readonly List<TimeSpan> _retryDelays = new();

    [Fact]
    public void ConfigureCustomHttpClientWithRetryPolicy_ShouldRegisterHttpClient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.ConfigureCustomHttpClientWithRetryPolicy();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var clientFactory = serviceProvider.GetService<IHttpClientFactory>();
        Assert.NotNull(clientFactory);
    }

    [Fact]
    public async Task Run_WithRetryPolicy_ShouldBeConfiguredCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockLogger = new Mock<ILogger>();
        
        // Configure minimum required services
        services.ConfigureCustomHttpClientWithRetryPolicy();
        services.AddSingleton(mockLogger.Object);
        services.AddScoped<DefaultAutomationConfig>();
        // services.AddSingleton(SimulatorDefinition);
        services.AddCogniteClient("TestConnector");

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        using (var scope = serviceProvider.CreateScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<Client.Builder>();
            Assert.NotNull(client);

            // Verify that the HttpClient has been registered with the retry policy
            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            Assert.NotNull(httpClientFactory);
        }
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<int, bool> _shouldFail;
        private readonly Action<int, TimeSpan> _onRetry;
        private int _attempts = 0;

        public TestHttpMessageHandler(Func<int, bool> shouldFail, Action<int, TimeSpan> onRetry)
        {
            _shouldFail = shouldFail;
            _onRetry = onRetry;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _attempts++;

            if (_shouldFail(_attempts))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, _attempts - 1));
                _onRetry(_attempts, delay);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        }
    }
}
}