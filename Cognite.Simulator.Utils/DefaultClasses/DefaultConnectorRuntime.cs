using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cognite.Extractor.Common;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

/// <summary>
/// The default connector runtime, all connectors should use this
/// </summary>
/// <typeparam name="TAutomationConfig"></typeparam>
public class DefaultConnectorRuntime<TAutomationConfig>
 where TAutomationConfig : AutomationConfig, new()
{

    /// <summary>
    /// The delegate for injecting services
    /// </summary>
    /// <param name="services"></param>
    public delegate void ServiceConfiguratorDelegate(IServiceCollection services);

    /// <summary>
    /// The function to inject the Simulator client into the service stack
    /// </summary>
    public static ServiceConfiguratorDelegate ConfigureServices;

    /// <summary>
    /// The default connector name to be used in calls to the API
    /// </summary>
    public static string ConnectorName = "Default";

    /// <summary>
    /// The entry point function to run the connector
    /// </summary>
    /// <returns></returns>
    public static async Task RunStandalone()
    {
        var logger = SimulatorLoggingUtils.GetDefault();
        using (var tokenSource = new CancellationTokenSource())
        {
            Console.CancelKeyPress += (sender, eArgs) =>
            {
                logger.LogWarning("Ctrl-C pressed: Cancelling tasks");
                eArgs.Cancel = true;
                tokenSource.Cancel();
            };

            CancellationToken token = tokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Run(logger, token).ConfigureAwait(false);
                }
                catch (NewConfigDetected newConfigException) // If NewConfigDetected, restart connector
                {
                    logger.LogInformation($"New remote config detected, restarting... {newConfigException}");
                    continue;
                }
            }
        }

    }

    public static async Task Run(ILogger defaultLogger, CancellationToken token)
    {
        var assembly = Assembly.GetEntryAssembly();
        var services = new ServiceCollection();
        services.AddLogger();
        services.AddCogniteClient($"{ConnectorName}Connector", $"{ConnectorName}Connector (Cognite)", true);

        DefaultConfig<TAutomationConfig> config;
        try
        {
            config = await services.AddConfiguration<DefaultConfig<TAutomationConfig>>(
                path: "./config.yml",
                types: new Type[] { typeof(DefaultConnectorConfig), typeof(SimulatorConfig) },
                appId: $"{ConnectorName}Connector",
                token: token
            ).ConfigureAwait(false);
        }
        catch (ConfigurationException e)
        {
            defaultLogger.LogError("Failed to load configuration file: {Message}", e.Message);
            return;
        }

        services.AddStateStore();
        services.AddHttpClient<FileStorageClient>();
        services.AddScoped<TAutomationConfig>();
        
        services.AddScoped<DefaultConnector<TAutomationConfig>>();
        services.AddScoped<DefaultModelLibrary<TAutomationConfig>>();
        services.AddScoped<DefaultRoutineLibrary<TAutomationConfig>>();
        services.AddScoped<DefaultSimulationRunner<TAutomationConfig>>();
        services.AddScoped<DefaultSimulationScheduler<TAutomationConfig>>();

        // This part allows connectors to inject their own SimulatorClients to 
        // the service stack
        ConfigureServices?.Invoke(services);
        

        services.AddExtractionPipeline(config.Connector);

        var provider = services.BuildServiceProvider();


        var logger = provider.GetRequiredService<ILogger<DefaultConnectorRuntime<TAutomationConfig>>>();

        logger.LogInformation("Starting the connector...");

        var destination = provider.GetRequiredService<CogniteDestination>();
        
        try
        {
            await destination.TestCogniteConfig(token).ConfigureAwait(false);
            logger.LogInformation("Connector can reach CDF!");
        }
        catch (Exception e)
        {
            // NewConfigDetected needs to propagate all the way up
            if (!(e is NewConfigDetected))
            {
                logger.LogError(e, "Error testing connection to CDF: {Message}", e.Message);
                return;
            }
        }

        while (!token.IsCancellationRequested)
        {
            using (var scope = provider.CreateScope())
            {
                var pipeline = provider.GetRequiredService<ExtractionPipeline>();
                
                try
                {
                    var connector = scope.ServiceProvider.GetRequiredService<DefaultConnector<TAutomationConfig>>();
                    var simulatorClient = scope.ServiceProvider.GetRequiredService<ISimulatorClient<ModelStateBase, SimulatorRoutineRevision>>();

                    await connector.Init(token).ConfigureAwait(false);
                   
                    await connector.Run(token).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (token.IsCancellationRequested)
                {
                    logger.LogWarning("Connector manually interrupted. Exiting...");
                    break;
                }
                catch (Exception e)
                {
                    await pipeline.NotifyError(e, token).ConfigureAwait(false);
                    if (e is ConnectorException ce)
                    {
                        logger.LogError("Connector error: {Message}", ce.Message);
                        if (ce.InnerException != null)
                        {
                            logger.LogError("- {Message}", ce.InnerException.Message);
                        }
                        foreach (var err in ce.Errors)
                        {
                            logger.LogError("- {Message}", ce.Message);
                        }
                    }
                    else if (e is CogniteSdk.ResponseException re)
                    {
                        // CDF request failed but request id and status code are available.
                        logger.LogError("Request to CDF failed with code {Code}: {Message}. Request id: {RequestId}", re.Code, re.Message, re.RequestId);
                    }
                    else if (e is HttpRequestException he)
                    {
                        // Did not get response from server (server down or other networking errors).
                        logger.LogError("The HTTP client failed to contact CDF: {Message}", he.Message);
                    }
                    else if (e is JsonException je)
                    {
                        // Json decode exception produced by the SDK. Unlikely to happen.
                        logger.LogError("Response from CDF cannot be parsed: {Message}", je.Message);
                    }
                    else if (e is TimeoutRejectedException)
                    {
                        // Timeout from the http client policy.
                        logger.LogError("Request to CDF timeout after retry");
                    }
                    else if (e is SimulatorConnectionException sue)
                    {
                        // If the simulator is not available, log to Windows events and exit
                        defaultLogger.LogError(sue.Message);
                        logger.LogError(sue, sue.Message);
                        break;
                    }
                    else
                    {
                        logger.LogError(e, "Unhandled exception: {message}", e.Message);
                    }
                    // Most errors may be intermittent. Wait and restart.
                    if (!token.IsCancellationRequested)
                    {
                        var delay = TimeSpan.FromSeconds(5);
                        logger.LogWarning("Restarting connector in {time} seconds", delay.TotalSeconds);
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                }
            }
        }
        
        logger.LogInformation("Connector exiting");

    }
}