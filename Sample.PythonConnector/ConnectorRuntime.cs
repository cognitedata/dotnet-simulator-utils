using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.PythonConnector;

public static class ConnectorRuntime
{
    public static void Init()
    {
        DefaultConnectorRuntime<PythonConfig, DefaultModelFilestate, DefaultModelFileStatePoco>
            .ConfigureServices = ConfigureServices;
        DefaultConnectorRuntime<PythonConfig, DefaultModelFilestate, DefaultModelFileStatePoco>
            .ConnectorName = "PythonSim";
        DefaultConnectorRuntime<PythonConfig, DefaultModelFilestate, DefaultModelFileStatePoco>
            .SimulatorDefinition = SimulatorDefinition.Get();
    }

    static void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, 
            PythonBridgeClient>();
    }

    public static async Task RunStandalone()
    {
        Init();
        await DefaultConnectorRuntime<PythonConfig, DefaultModelFilestate, DefaultModelFileStatePoco>
            .RunStandalone().ConfigureAwait(false);
    }
}
