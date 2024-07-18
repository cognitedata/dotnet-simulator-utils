using System.Diagnostics;
using Cognite.Extractor.StateStorage;
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
            services.AddScoped<ISimulatorClient<CalculatorModelFilestate, SimulatorRoutineRevision>, CalculatorSimulatorAutomationClient>();
        }
        public class CustomAutomationConfig : AutomationConfig { }


        public static async Task Run()
        {
            // var currentProcess = Process.GetCurrentProcess();
            // Console.WriteLine($"ProcessId: {currentProcess.Id} . Launch the debugger...");
            // Thread.Sleep(6000);
            DefaultConnectorRuntime<CustomAutomationConfig, CalculatorModelFilestate, CalculatorFileStatePoco>.SimulatorDefinition = SimulatorDefinition.Get();
            DefaultConnectorRuntime<CustomAutomationConfig, CalculatorModelFilestate, CalculatorFileStatePoco>.ConfigureServices = ConfigureServices;
            DefaultConnectorRuntime<CustomAutomationConfig, CalculatorModelFilestate, CalculatorFileStatePoco>.ConnectorName = "Calculator";

            await DefaultConnectorRuntime<CustomAutomationConfig, CalculatorModelFilestate, CalculatorFileStatePoco>.RunStandalone().ConfigureAwait(false);
        }
    }
}

