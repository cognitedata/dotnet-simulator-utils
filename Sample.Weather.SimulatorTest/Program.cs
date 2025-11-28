using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk.Alpha;

using Microsoft.Extensions.DependencyInjection;

using SampleConnector;

await SampleConnectorNamespace.SampleConnector.Run().ConfigureAwait(false);

namespace SampleConnectorNamespace
{
    public static class SampleConnector
    {
        static void ConfigureServices(IServiceCollection services)
        {
            // services.AddScoped<DefaultModelFilestate>();
            services.AddScoped<ISimulatorClient<WeatherModelFilestate, SimulatorRoutineRevision>, WeatherSimulatorClient>();
        }
        public class CustomAutomationConfig : AutomationConfig { }


        public static async Task Run()
        {
            // var currentProcess = Process.GetCurrentProcess();
            // Console.WriteLine($"ProcessId: {currentProcess.Id} . Launch the debugger...");
            // Thread.Sleep(6000);
            DefaultConnectorRuntime<CustomAutomationConfig, WeatherModelFilestate, WeatherFileStatePoco>.SimulatorDefinition = SimulatorDefinition.Get();
            DefaultConnectorRuntime<CustomAutomationConfig, WeatherModelFilestate, WeatherFileStatePoco>.ConfigureServices = ConfigureServices;
            DefaultConnectorRuntime<CustomAutomationConfig, WeatherModelFilestate, WeatherFileStatePoco>.ConnectorName = "weather-vikram-2211";

            await DefaultConnectorRuntime<CustomAutomationConfig, WeatherModelFilestate, WeatherFileStatePoco>.RunStandalone().ConfigureAwait(false);
        }
    }
}

