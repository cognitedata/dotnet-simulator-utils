using Cognite.Extractor.StateStorage;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

using static SampleConnectorNamespace.SampleConnector;

public class WeatherModelFilestate : ModelStateBase
{
    public WeatherModelFilestate() : base() { }

    public override WeatherFileStatePoco GetPoco()
    {
        var basePoco = base.GetPoco();
        var extendedPoco = new WeatherFileStatePoco { };
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
        if (poco is WeatherFileStatePoco mPoco)
        {
            ModelType = mPoco.ModelType;
        }
    }

    public bool Processed { get; set; }

    public override bool IsExtracted => Processed;

    public string? ModelType { get; set; }
}

public class WeatherFileStatePoco : ModelStateBasePoco
{
    [StateStoreProperty("extra-model-type")]
    public string? ModelType { get; internal set; }
}

public class WeatherSimulatorAutomationClient :
        AutomationClient,
        ISimulatorClient<WeatherModelFilestate, SimulatorRoutineRevision>
{


    private readonly ILogger<WeatherSimulatorAutomationClient> _logger;

    public WeatherSimulatorAutomationClient(
        ILogger<WeatherSimulatorAutomationClient> logger,
        DefaultConfig<CustomAutomationConfig> config) : base(logger, config?.Automation)
    {
        _logger = logger;
    }

    public async Task ExtractModelInformation(WeatherModelFilestate state, CancellationToken _token)
    {
        _logger.LogInformation("Begin model information extraction");
        if (state == null)
        {
            throw new InvalidOperationException("State is not defined");
        }
        Random random = new Random();
        await Task.Run(() =>
        {
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
        WeatherModelFilestate modelState,
        SimulatorRoutineRevision routineRevision,
        Dictionary<string, SimulatorValueItem> inputData,
        CancellationToken token
    )
    {
        if (modelState == null)
        {
            throw new InvalidOperationException("Model state is not defined");
        }
        _logger.LogInformation("WeatherClient Running a simulation");
        _logger.LogInformation($"Model type : {modelState.ModelType}");
        try
        {
            var routine = new WeatherRoutine(routineRevision, inputData, _logger);
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

internal class WeatherRoutineAutomation : RoutineImplementationBase
{
    public WeatherRoutineAutomation(SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger)
    {
    }

    public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments, CancellationToken token)
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

    public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments, CancellationToken token)
    {
        Console.WriteLine("Handling inputs");
    }
}