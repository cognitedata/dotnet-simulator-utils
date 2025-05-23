using Cognite.Simulator.Utils;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

using static SampleConnectorNamespace.SampleConnector;

public class CalculatorSimulatorClient :
        ISimulatorClient<CalculatorModelFilestate, SimulatorRoutineRevision>
{


    private readonly ILogger<CalculatorSimulatorClient> _logger;

    public CalculatorSimulatorClient(ILogger<CalculatorSimulatorClient> logger, DefaultConfig<CustomAutomationConfig> config)
    {
        _logger = logger;
    }

    public Task ExtractModelInformation(CalculatorModelFilestate state, CancellationToken _token)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }
        _logger.LogInformation("ExtractModelInformation ");
        state.CanRead = false;
        state.Processed = true;
        state.ParsingInfo.SetFailure();
        return Task.CompletedTask;
    }

    public string GetConnectorVersion(CancellationToken token)
    {
        return CommonUtils.GetAssemblyVersion();
    }

    public string GetSimulatorVersion(CancellationToken token)
    {
        return "2.0.1";
    }

    public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(
        CalculatorModelFilestate modelState,
        SimulatorRoutineRevision routineRevision,
        Dictionary<string, SimulatorValueItem> inputData,
        CancellationToken token
    )
    {
        _logger.LogInformation("CalculatorClient Running a simulation");
        try
        {
            Dictionary<string, SimulatorValueItem> result = new Dictionary<string, SimulatorValueItem>();
            var routine = new CalculatorRoutine(routineRevision, inputData, _logger);
            result = routine.PerformSimulation(token);
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

    public Task TestConnection(CancellationToken token)
    {
        return Task.CompletedTask;
    }
}

internal class CalculatorRoutine : RoutineImplementationBase
{
    public CalculatorRoutine(SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger)
    {
    }

    public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments, CancellationToken token)
    {
        Console.WriteLine("Handling outputs");
        var resultItem = new SimulatorValueItem()
        {
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

    public override void RunCommand(Dictionary<string, string> arguments, CancellationToken token)
    {
        Console.WriteLine("Handling run command");
    }

    public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments, CancellationToken token)
    {
        Console.WriteLine("Handling inputs");
    }
}