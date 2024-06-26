using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;
using static SampleConnector;

public class CalculatorSimulatorAutomationClient : 
        AutomationClient,
        ISimulatorClient<ModelStateBase, SimulatorRoutineRevision> {

    
    private readonly ILogger<CalculatorSimulatorAutomationClient> _logger;

    public CalculatorSimulatorAutomationClient(
        ILogger<CalculatorSimulatorAutomationClient> logger, 
        DefaultConfig<CustomAutomationConfig> config): base(logger, config.Automation) {
        _logger = logger;
    }

    public void ExtractModelInformation(ModelStateBase state, CancellationToken _token)
    {
        _logger.LogCritical("ExtractModelInformation WAS CALLED IN SIMULATORAUTOMATION");
        state.CanRead = false;
        state.ParsingInfo.SetFailure();
    }

    public string GetConnectorVersion()
    {
        return CommonUtils.GetAssemblyVersion();
    }

    public string GetSimulatorVersion()
    {
        return "2.0.1";
    }

    public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(
        ModelStateBase modelState, 
        SimulatorRoutineRevision routineRevision, 
        Dictionary<string, SimulatorValueItem> inputData
    ) {
        _logger.LogInformation("CalculatorClient Running a simulation");
        try
        {
            Dictionary<string, SimulatorValueItem> result = new Dictionary<string, SimulatorValueItem>();
            var routine = new CalculatorRoutine(routineRevision, inputData);
            result = routine.PerformSimulation();
            foreach (var kvp in result)
            {
                Console.WriteLine($"Key: {kvp.Key}, Value: {kvp.Value}");
            }

            return Task.FromResult(result);
        }
        finally
        {
        }
        
    }

    protected override void PreShutdown()
    {
        throw new NotImplementedException();
    }
}

internal class CalculatorRoutineAutomation : RoutineImplementationBase
{
    public CalculatorRoutineAutomation(SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData) : base(routineRevision, inputData)
    {
    }

    public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments)
    {
        Console.WriteLine("Handling outputs");
        var resultItem = new SimulatorValueItem() {
            SimulatorObjectReference = new Dictionary<string, string> {
                { "objectName", "a" },
                { "objectProperty", "b" },
            },
            TimeseriesExternalId = outputConfig.SaveTimeseriesExternalId,
            ReferenceId = outputConfig.ReferenceId,
            ValueType = outputConfig.ValueType,
            Value = SimulatorValue.Create("1.0"),
        };
        return resultItem;
    }

    public override void RunCommand(Dictionary<string, string> arguments)
    {
        Console.WriteLine("Handling run command");
    }

    public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments)
    {
        Console.WriteLine("Handling inputs");
    }
}