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

    private class TestDelegatingHandler : DelegatingHandler
    {
        private readonly Action<TimeSpan> _onRetry;

        public TestDelegatingHandler(Action<TimeSpan> onRetry)
        {
            _onRetry = onRetry;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Always return a transient error to trigger retries
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    [Fact]
    public async Task Run_WithRetryPolicy_ShouldBeConfiguredCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockLogger = new Mock<ILogger>();
        _retryDelays.Clear();

        // Configure services
        services.AddSingleton(mockLogger.Object);

        // Override the retry configuration for testing
        var originalSleepDuration = HttpClientPolicyConfiguration.SleepDurationProvider;
        var originalOnRetry = HttpClientPolicyConfiguration.OnRetry;
        
        HttpClientPolicyConfiguration.SleepDurationProvider = _ => TimeSpan.FromMilliseconds(1); // Use minimal delay for testing
        HttpClientPolicyConfiguration.OnRetry = (exception, timeSpan, retryCount, context, serviceProvider) =>
        {
            _retryDelays.Add(timeSpan);
        };

        // Configure the retry policy and test handler
        services.ConfigureCustomHttpClientWithRetryPolicy();
        services.AddHttpClient<Client.Builder>()
            .ConfigurePrimaryHttpMessageHandler(() => new TestDelegatingHandler(delay => { }));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var client = serviceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(Client.Builder));
        client.BaseAddress = new Uri(TestHost);

        try
        {
            // This should trigger the policy's retries
            await client.GetAsync("/api/test");
        }
        catch (HttpRequestException)
        {
            // Expected to fail after all retries
        }

        // Assert
        Assert.Equal(11, _retryDelays.Count);

        // Verify all delays are minimal (1ms)
        foreach (var delay in _retryDelays)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(1), delay);
        }

        // Restore original configurations
        HttpClientPolicyConfiguration.SleepDurationProvider = originalSleepDuration;
        HttpClientPolicyConfiguration.OnRetry = originalOnRetry;
    }
}
}
