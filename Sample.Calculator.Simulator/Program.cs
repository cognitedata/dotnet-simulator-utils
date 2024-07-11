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
            base.FillProperties<FileStatePoco, CalculatorFileStatePoco >(poco, newObj);
            newObj.ModelType = "UZYXXZ";
            return newObj;
        }

        public override void Init(FileStatePoco poco)
        {
            base.Init(poco);
            if (poco is CalculatorFileStatePoco mPoco)
            {
            }
        }

        public override bool IsExtracted => false;

        public string ModelType ;
    }

    public class CalculatorFileStatePoco : ModelStateBasePoco
    {
        [StateStoreProperty("extra-model-type")]
        public string ModelType { get; internal set; }
    }
}

