using System;
using CogniteSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Configuration for HTTP client policies.
    /// </summary>
    public static class HttpClientPolicyConfiguration
    {
        /// <summary>
        /// Configures a custom HTTP client with a retry policy for transient HTTP errors.
        /// </summary>
        /// <param name="services">The service collection to add the HTTP client to.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection ConfigureCustomHttpClientWithRetryPolicy(this IServiceCollection services)
        {
            services.AddHttpClient<Client.Builder>()
                .AddPolicyHandler((serviceProvider, _) =>
                {
                    return HttpPolicyExtensions
                        .HandleTransientHttpError()
                        .WaitAndRetryAsync(
                            retryCount: 11,
                            sleepDurationProvider: retryAttempt =>
                                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                            onRetry: (exception, timeSpan, retryCount, context) =>
                            {
                                try
                                {
                                    var logger = serviceProvider.GetService<ILogger<Client.Builder>>();
                                    if (logger != null)
                                    {
                                        logger.LogWarning(
                                            "Retry {RetryCount} of {MaxRetries} after {Delay}s. Error: {Error}",
                                            retryCount,
                                            11,
                                            timeSpan.TotalSeconds,
                                            exception.ToString());
                                    }
                                }
                                catch (ObjectDisposedException)
                                {
                                    // Ignore if the logger has been disposed
                                }
                            });
                });

            return services;
        }
    }



}