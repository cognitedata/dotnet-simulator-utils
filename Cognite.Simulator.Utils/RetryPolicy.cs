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
        /// Gets or sets the action to execute when a transient HTTP error occurs.
        /// </summary>
        public static Action<Exception, TimeSpan, int, Context, IServiceProvider> OnRetry { get; set; } = (exception, timeSpan, retryCount, context, serviceProvider) => { 
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
        };

        /// <summary>
        /// Gets or sets the function that provides the sleep duration for each retry attempt.
        /// </summary>
        public static Func<int, TimeSpan> SleepDurationProvider { get; set; } = retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));

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
                            sleepDurationProvider: SleepDurationProvider,
                            onRetry: (exception, timeSpan, retryCount, context) =>
                            {
                                OnRetry(exception.Exception, timeSpan, retryCount, context, serviceProvider);
                                
                            });
                });

            return services;
        }
    }



}