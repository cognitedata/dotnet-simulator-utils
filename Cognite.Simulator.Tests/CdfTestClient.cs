using Cognite.Extensions;
using Cognite.Extractor.Logging;
using CogniteSdk;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Cognite.Simulator.Tests
{
    internal static class CdfTestClient
    {

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

            var loggerConfig = new LoggerConfig
            {
                Console = new ConsoleConfig
                {
                    Level = "debug"
                }
            };

            // Configure logging
            services.AddSingleton(loggerConfig);
            services.AddLogger();

            // Configure OIDC auth
            services.AddHttpClient("AuthClient");
            services.AddSingleton<IAuthenticator>(p => {
                var factory = p.GetRequiredService<IHttpClientFactory>();
                return new MsalAuthenticator(authConfig, null, factory, "AuthClient");
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
        }
    }
}
