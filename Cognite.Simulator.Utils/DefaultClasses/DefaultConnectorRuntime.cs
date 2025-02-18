using System;
using System.Collections.Generic;
using System.Linq;
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
using CogniteSdk;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

/// <summary>
/// Default implementation of the runtime for a connector.
/// In general cases, this class should not be overridden.
/// </summary>
public class DefaultConnectorRuntime<TAutomationConfig, TModelState, TModelStateBasePoco>
 where TAutomationConfig : AutomationConfig, new()
 where TModelState: ModelStateBase, new()
 where TModelStateBasePoco: ModelStateBasePoco
{
    /// <summary>
    /// Delegate to configure services. This will be called before the services are built.
    /// </summary>
    public delegate void ServiceConfiguratorDelegate(IServiceCollection services);

    /// <summary>
    /// Delegate to configure services. This will be called before the services are built.
    /// </summary>
    public static ServiceConfiguratorDelegate ConfigureServices;

    /// <summary>
    /// The simulator definition. This will be used to create a simulator if the simulator definition
    /// is not found on CDF.
    /// </summary>
    public static SimulatorCreate SimulatorDefinition;

    public static string ConnectorName = "Default";
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
                catch (Exception e)
                {
                    logger.LogError(e, "Unhandled exception: {message}", e.Message);
                }
            }
        }
    }


    /// <summary>
    /// Prepares the simulator object for update
    /// </summary>
    /// <param name="existingSimulatorDefinition"></param>
    /// <param name="newSimulatorDefinition"></param>
    /// <param name="logger"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static SimulatorUpdateItem PrepareUpdateSimulatorObject(Simulator existingSimulatorDefinition, 
    SimulatorCreate newSimulatorDefinition, ILogger<DefaultConnectorRuntime<TAutomationConfig, TModelState, TModelStateBasePoco>> logger )
    {
        if (existingSimulatorDefinition == null) {
            throw new Exception("Simulator definition from remote is null");
        }

        if (newSimulatorDefinition == null) {
            throw new Exception("New simulator definition is null");
        }

        var update = new SimulatorUpdate {
            FileExtensionTypes = new Update<IEnumerable<string>> { Set = newSimulatorDefinition.FileExtensionTypes },
            ModelTypes = new Update<IEnumerable<SimulatorModelType>> { Set = newSimulatorDefinition.ModelTypes },
            StepFields = new Update<IEnumerable<SimulatorStepField>> { Set = newSimulatorDefinition.StepFields },
        };

        // Optional fields
        if (newSimulatorDefinition.UnitQuantities != null) {
            update.UnitQuantities = new Update<IEnumerable<SimulatorUnitQuantity>> { Set = newSimulatorDefinition.UnitQuantities };
        }

        // Create and return the update item if there are changes
        return new SimulatorUpdateItem(existingSimulatorDefinition.Id) { Update = update };
    }

    private static async Task GetOrCreateSimulator( Client cdfClient, DefaultConfig<TAutomationConfig> config, 
    ILogger<DefaultConnectorRuntime<TAutomationConfig, TModelState, TModelStateBasePoco>> logger, CancellationToken token) {
        var definition = SimulatorDefinition;
        var existingSimulators = await cdfClient.Alpha.Simulators.ListAsync(new SimulatorQuery(), token).ConfigureAwait(false);
        var existingSimulator = existingSimulators.Items.FirstOrDefault(s => s.ExternalId == definition.ExternalId);
        if (definition == null && existingSimulator == null) {
            throw new Exception("Simulator definition not found in either the remote API or locally.");
        }
        if (definition != null) {
            logger.LogDebug("Simulator definition found locally");
            if (existingSimulator == null) {
                logger.LogDebug("Simulator definition not found on CDF, will create one remotely");
                var res = await cdfClient.Alpha.Simulators.CreateAsync(new List<SimulatorCreate> { definition }, token).ConfigureAwait(false);
            } else {
                var updateItem = PrepareUpdateSimulatorObject(existingSimulator, definition, logger);
                logger.LogDebug("Updating simulator definition");
                var simulatorsToUpdate = new List<SimulatorUpdateItem> { updateItem };
                await cdfClient.Alpha.Simulators.UpdateAsync(simulatorsToUpdate, token).ConfigureAwait(false);                
            }
        } 
    }

    public static async Task Run(ILogger defaultLogger, CancellationToken token)
    {
        var assembly = Assembly.GetEntryAssembly();
        var services = new ServiceCollection();
        services.AddCogniteClient($"{ConnectorName}Connector", $"{ConnectorName}Connector (Cognite)", true);

        DefaultConfig<TAutomationConfig> config;
        try
        {
            config = await services.AddConfiguration<DefaultConfig<TAutomationConfig>>(
                path: "./config.yml",
                types: new Type[] { typeof(DefaultConnectorConfig), typeof(LoggerConfig), typeof(Cognite.Simulator.Utils.BaseConfig) },
                appId: $"{ConnectorName}Connector",
                token: token
            ).ConfigureAwait(false);
        }
        catch (ConfigurationException e)
        {
            defaultLogger.LogError("Failed to load configuration file: {Message}", e.Message);
            return;
        }
        services.AddLogger();
        services.AddStateStore();
        services.AddHttpClient<FileStorageClient>();
        services.AddScoped<TAutomationConfig>();
        services.AddSingleton(SimulatorDefinition);
        services.AddScoped<DefaultConnector<TAutomationConfig,TModelState,TModelStateBasePoco>>();
        services.AddScoped<DefaultModelLibrary<TAutomationConfig,TModelState,TModelStateBasePoco>>();
        services.AddScoped<DefaultRoutineLibrary<TAutomationConfig>>();
        services.AddScoped<DefaultSimulationRunner<TAutomationConfig,TModelState,TModelStateBasePoco>>();
        services.AddScoped<DefaultSimulationScheduler<TAutomationConfig>>();

        defaultLogger.LogDebug("Injecting services");
        ConfigureServices?.Invoke(services);

        // This part allows connectors to inject their own SimulatorClients to 
        // the service stack

        services.AddExtractionPipeline(config.Connector);

        var provider = services.BuildServiceProvider();


        var logger = provider.GetRequiredService<ILogger<DefaultConnectorRuntime<TAutomationConfig,TModelState,TModelStateBasePoco>>>();

        logger.LogInformation("Starting the connector...");

        var destination = provider.GetRequiredService<CogniteDestination>();
        var cdfClient = provider.GetRequiredService<Client>();

        await destination.TestCogniteConfig(token).ConfigureAwait(false);
        logger.LogInformation("Connector can reach CDF!");

        await GetOrCreateSimulator(cdfClient, config, logger, token).ConfigureAwait(false);

        while (!token.IsCancellationRequested)
        {
            using (var scope = provider.CreateScope())
            {
                var pipeline = scope.ServiceProvider.GetRequiredService<ExtractionPipeline>();
                
                try
                {
                    var connector = scope.ServiceProvider.GetRequiredService<DefaultConnector<TAutomationConfig,TModelState,TModelStateBasePoco>>();

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
                        var delay = TimeSpan.FromSeconds(10);
                        logger.LogWarning("Restarting connector in {time} seconds", delay.TotalSeconds);
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                }
            }
        }
        
        logger.LogInformation("Connector exiting");

    }
}