using Cognite.Extensions;
using Cognite.Extractor.Logging;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using CogniteSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Tests
{
    internal static class CdfTestClient
    {
        internal const long TestDataset = 7900866844615420;
        private static int _configIdx;
        internal static string? _statePath;

        public static void AddCogniteTestClient(this IServiceCollection services)
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
                Scopes = new[]
                {
                    $"{host}/.default"
                }
            };

            var cogConfig = new CogniteConfig
            {
                Host = host,
                Project = project,
                IdpAuthentication = authConfig,
            };

            var loggerConfig = new LoggerConfig
            {
                Console = new ConsoleConfig
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
            services.AddSingleton<IAuthenticator>(p => {
                var factory = p.GetRequiredService<IHttpClientFactory>();
                var logger = p.GetRequiredService<ILogger<IAuthenticator>>();
                return new MsalAuthenticator(authConfig, logger, factory, "AuthClient");
            });

            // Configure CDF Client
            services.AddHttpClient<Client.Builder>();
            services.AddSingleton(p => {
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

            // Configure state store
        }
    }
}
