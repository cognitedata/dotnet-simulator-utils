using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;

await SampleConnector.Run();
public static class SampleConnector {
    static void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<DefaultModelFilestate>();
        services.AddScoped<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, CalculatorSimulatorAutomationClient>();
    }
    public class CustomAutomationConfig : AutomationConfig { }
    public static async Task Run() {
        DefaultConnectorRuntime<CustomAutomationConfig,DefaultModelFilestate, DefaultModelFileStatePoco>.ConfigureServices = ConfigureServices;
        DefaultConnectorRuntime<CustomAutomationConfig,DefaultModelFilestate, DefaultModelFileStatePoco>.ConnectorName = "Calculator";
        await DefaultConnectorRuntime<CustomAutomationConfig,DefaultModelFilestate, DefaultModelFileStatePoco>.RunStandalone().ConfigureAwait(false);
    }
}

