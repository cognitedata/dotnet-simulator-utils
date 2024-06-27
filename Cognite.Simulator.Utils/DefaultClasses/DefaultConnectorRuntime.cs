using System;
using System.Reflection;
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

public class DefaultConnectorRuntime<TAutomationConfig>
 where TAutomationConfig : AutomationConfig, new()
{

    public delegate void ServiceConfiguratorDelegate(IServiceCollection services);

    public static ServiceConfiguratorDelegate ConfigureServices;

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
        services.AddCogniteClient("CalculatorConnector", "CalculatorConnector/v0.0.1 (Cognite)", true);

        DefaultConfig<TAutomationConfig> config;
        try
        {
            config = await services.AddConfiguration<DefaultConfig<TAutomationConfig>>(
                path: "./config.yml",
                types: new Type[] { typeof(DefaultConnectorConfig), typeof(SimulatorConfig) },
                appId: "CalculatorConnector",
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
        ConfigureServices?.Invoke(services);
        // services.AddScoped<ISimulatorClient<ModelStateBase, SimulatorRoutineRevision>, CalculatorSimulatorAutomationClient>();

        services.AddScoped<DefaultConnector<TAutomationConfig>>();
        services.AddScoped<DefaultModelLibrary<TAutomationConfig>>();
        services.AddScoped<DefaultRoutineLibrary<TAutomationConfig>>();
        services.AddScoped<DefaultSimulationRunner<TAutomationConfig>>();
        services.AddScoped<DefaultSimulationScheduler<TAutomationConfig>>();
        

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
            // TODO: This needs to be fixed
            if (e is NewConfigDetected)
            {
                return;
            }
            else
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
                    logger.LogError(e, "An error occurred during connector execution");
                }
            }
        }
    }
}