using Cognite.Extractor.StateStorage;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;
using static SampleConnectorNamespace.SampleConnector;

public class CalculatorModelFilestate : ModelStateBase
{
    public CalculatorModelFilestate() : base() { }

    public override CalculatorFileStatePoco GetPoco()
    {
        var basePoco = base.GetPoco();
        var extendedPoco = new CalculatorFileStatePoco { };
        SyncProperties(basePoco, extendedPoco);
        extendedPoco.ModelType = ModelType;
        return extendedPoco;
    }

    public override bool ShouldProcess()
    {
        // Since the ModelType is an extended value it should be saved into the state.db after the 
        // first extraction and then read from there. If the value is null at this point this means  
        // that the last extraction state has been lost due to deletion of the state.db so we need 
        // to return true to reparse it.
        // if (ModelType == null)
        // {
        //     return true;
        // }
        return base.ShouldProcess();
    }

    public override void Init(FileStatePoco poco)
    {
        base.Init(poco);
        if (poco is CalculatorFileStatePoco mPoco)
        {
            ModelType = mPoco.ModelType;
        }
    }

    public bool Processed { get; set; }

    public override bool IsExtracted => Processed;

    public string ModelType { get; set; }
}

public class CalculatorFileStatePoco : ModelStateBasePoco
{
    [StateStoreProperty("extra-model-type")]
    public string? ModelType { get; internal set; }
}

public class CalculatorSimulatorAutomationClient :
        AutomationClient,
        ISimulatorClient<CalculatorModelFilestate, SimulatorRoutineRevision>
{


    private readonly ILogger<CalculatorSimulatorAutomationClient> _logger;

    public CalculatorSimulatorAutomationClient(
        ILogger<CalculatorSimulatorAutomationClient> logger,
        DefaultConfig<CustomAutomationConfig> config) : base(logger, config.Automation)
    {
        _logger = logger;
    }

    public async Task ExtractModelInformation(CalculatorModelFilestate state, CancellationToken _token)
    {
        _logger.LogInformation("Begin model information extraction");
        if (state == null)
        {
            throw new Exception("State is not defined");
        }
        Random random = new Random();
        await Task.Run(() => {
            Thread.Sleep(200);
        }, _token).ConfigureAwait(false);
        state.ModelType = "PARSED" + random.Next().ToString() + "abc";
        state.CanRead = true;
        state.Processed = true;
        _logger.LogInformation($"Model information type : {state.ModelType}");
        state.ParsingInfo.SetSuccess();
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
        if (modelState == null) {
            throw new Exception("Model state is not defined");
        }
        _logger.LogInformation("CalculatorClient Running a simulation");
        _logger.LogInformation($"Model type : {modelState.ModelType}");
        try
        {
            var routine = new CalculatorRoutine(routineRevision, inputData, _logger);
            var result = routine.PerformSimulation(token);
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

    protected override void PreShutdown()
    {
        throw new NotImplementedException();
    }
}

internal class CalculatorRoutineAutomation : RoutineImplementationBase
{
    public CalculatorRoutineAutomation(SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger)
    {
    }

    public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments)
    {
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

    public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments)
    {
        Console.WriteLine("Handling inputs");
    }
}