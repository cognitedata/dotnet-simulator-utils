using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;

await SampleConnector.Run();
public static class SampleConnector {
    static void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ISimulatorClient<ModelStateBase, SimulatorRoutineRevision>, CalculatorSimulatorAutomationClient>();
    }
    public class CustomAutomationConfig : AutomationConfig { }
    public static async Task Run() {
        DefaultConnectorRuntime<CustomAutomationConfig>.ConfigureServices = ConfigureServices;
        DefaultConnectorRuntime<CustomAutomationConfig>.ConnectorName = "Calculator";
        await DefaultConnectorRuntime<CustomAutomationConfig>.RunStandalone().ConfigureAwait(false);
    }
}

