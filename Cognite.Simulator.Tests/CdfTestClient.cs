﻿using System;
using System.Net.Http;
using System.Threading;

using Cognite.Extensions;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;

using CogniteSdk;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Xunit;

namespace Cognite.Simulator.Tests
{
    [CollectionDefinition(nameof(SequentialTestCollection), DisableParallelization = true)]
    public class SequentialTestCollection
    {

    }

    internal static class CdfTestClient
    {
        private static int _configIdx;
        private static string? _statePath;

        public static IServiceCollection AddCogniteTestClient(this IServiceCollection services)
        {
            var host = Environment.GetEnvironmentVariable("COGNITE_HOST");
            var project = Environment.GetEnvironmentVariable("COGNITE_PROJECT");
            var tenant = Environment.GetEnvironmentVariable("AZURE_TENANT");
            var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            var secret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
            if (host == null || project == null || tenant == null || clientId == null || secret == null)
            {
                throw new NullReferenceException("Environment variables needed by tests cannot be read");
            }

            var index = Interlocked.Increment(ref _configIdx);
            _statePath = $"test-state-{index}";

            var authConfig = new AuthenticatorConfig
            {
                Tenant = tenant,
                ClientId = clientId,
                Secret = secret,
                Scopes = new Common.ListOrSpaceSeparated($"{host}/.default")
            };

            var cogConfig = new CogniteConfig
            {
                Host = host,
                Project = project,
                IdpAuthentication = authConfig,
                ExtractionPipeline = new ExtractionRunConfig
                {
                    PipelineId = "utils-tests-pipeline",
                    Frequency = 1
                }
            };

            var loggerConfig = new LoggerConfig
            {
                Console = new Extractor.Logging.ConsoleConfig
                {
                    Level = "debug"
                }
            };

            var stateStoreConfig = new StateStoreConfig
            {
                Database = StateStoreConfig.StorageType.LiteDb,
                Location = _statePath
            };

            // Configure logging
            services.AddSingleton(loggerConfig);
            services.AddLogger();

            // Configure OIDC auth
            services.AddHttpClient("AuthClient");
            services.AddSingleton<IAuthenticator>(p =>
            {
                var factory = p.GetRequiredService<IHttpClientFactory>();
                var logger = p.GetRequiredService<ILogger<IAuthenticator>>();
                return new MsalAuthenticator(authConfig, logger, factory, "AuthClient");
            });

            // Configure CDF Client
            services.AddHttpClient<Client.Builder>()
                .AddPolicyHandler((provider, message) =>
                {
                    return CogniteExtensions.GetRetryPolicy(null, 10, 10000);
                });

            services.AddSingleton(p =>
            {
                var auth = p.GetRequiredService<IAuthenticator>();
                var builder = p.GetRequiredService<Client.Builder>();
                var client = builder
                    .SetBaseUrl(new Uri(host))
                    .SetProject(project)
                    .SetAppId("Simulator-Utils-Tests")
                    .SetUserAgent($"Simulator-Utils-Tests/1.0.0")
                    .SetTokenProvider(auth.GetToken)
                    .Build();
                return client;
            });

            //Configure state
            services.AddSingleton(stateStoreConfig);
            services.AddStateStore();

            // Configure CDF destination
            services.AddSingleton(cogConfig);
            services.AddSingleton(p =>
            {
                var client = p.GetRequiredService<Client>();
                var logger = p.GetRequiredService<ILogger<CogniteDestination>>();
                var config = p.GetRequiredService<CogniteConfig>();
                return new CogniteDestination(client, logger, config);
            });

            return services;
        }
    }
}
