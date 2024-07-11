using System.Diagnostics;
using Cognite.Extractor.StateStorage;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;


await SampleConnector.Run();
public static class SampleConnector {
    static void ConfigureServices(IServiceCollection services)
    {
        // services.AddScoped<DefaultModelFilestate>();
        services.AddScoped<ISimulatorClient<CalculatorModelFilestate, SimulatorRoutineRevision>, CalculatorSimulatorAutomationClient>();
    }
    public class CustomAutomationConfig : AutomationConfig { }
    public static async Task Run() {
        
        //var currentProcess = Process.GetCurrentProcess();
        //Console.WriteLine($"ProcessId: {currentProcess.Id} . Launch the debugger...");
        //Thread.Sleep(6000);
        DefaultConnectorRuntime<CustomAutomationConfig,CalculatorModelFilestate, CalculatorFileStatePoco>.ConfigureServices = ConfigureServices;
        DefaultConnectorRuntime<CustomAutomationConfig,CalculatorModelFilestate, CalculatorFileStatePoco>.ConnectorName = "Calculator";
        await DefaultConnectorRuntime<CustomAutomationConfig,CalculatorModelFilestate, CalculatorFileStatePoco>.RunStandalone().ConfigureAwait(false);
    }

    public class CalculatorModelFilestate : ModelStateBase
    {
        public CalculatorModelFilestate() : base()
        {
        }

        public override CalculatorFileStatePoco GetPoco() {
            var poco = base.GetPoco();
            var newObj = new CalculatorFileStatePoco{};
            FillProperties(poco, newObj);
            newObj.ModelType = ModelType;
            return newObj;
        }

        public override void Init(FileStatePoco poco)
        {
            base.Init(poco);
            if (poco is CalculatorFileStatePoco mPoco)
            {
                ModelType = mPoco.ModelType ;
            }
        }

        public bool Processed {get; set; }

        public override bool IsExtracted => Processed;

        public string ModelType {get; set; }
    }

    public class CalculatorFileStatePoco : ModelStateBasePoco
    {
        [StateStoreProperty("extra-model-type")]
        public string ModelType { get; internal set; }
    }
}

