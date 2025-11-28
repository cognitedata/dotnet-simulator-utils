using Cognite.Simulator.Utils;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

using static SampleConnectorNamespace.SampleConnector;

public class WeatherSimulatorClient :
        ISimulatorClient<WeatherModelFilestate, SimulatorRoutineRevision>
{


    private readonly ILogger<WeatherSimulatorClient> _logger;

    public WeatherSimulatorClient(ILogger<WeatherSimulatorClient> logger, DefaultConfig<CustomAutomationConfig> config)
    {
        _logger = logger;
    }

    public async Task ExtractModelInformation(WeatherModelFilestate state, CancellationToken _token)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }
        _logger.LogInformation($"model file path : {state.FilePath}");

        try
        {
            // Read the file content
            if (!File.Exists(state.FilePath))
            {
                _logger.LogError($"Model file not found at path: {state.FilePath}");
                state.CanRead = false;
                state.Processed = true;
                state.ParsingInfo.SetFailure();
                return;
            }

            var fileContent = await File.ReadAllTextAsync(state.FilePath, _token).ConfigureAwait(false);
            _logger.LogInformation($"File content read successfully, length: {fileContent.Length}");

            // Parse JSON and extract location
            var jsonDocument = System.Text.Json.JsonDocument.Parse(fileContent);
            if (jsonDocument.RootElement.TryGetProperty("location", out var locationElement))
            {
                var location = locationElement.GetString();
                _logger.LogInformation($"Extracted location from model file: {location}");
                
                // Store location in state for later use
                state.ParsingInfo.SetSuccess();
                state.CanRead = true;
            }
            else
            {
                _logger.LogWarning("Location key not found in model file");
                state.ParsingInfo.SetFailure();
                state.CanRead = false;
            }

            state.Processed = true;
            _logger.LogInformation("ExtractModelInformation completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting model information");
            state.CanRead = false;
            state.Processed = true;
            state.ParsingInfo.SetFailure();
        }
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
        _logger.LogInformation("WeatherClient Running a simulation");
        try
        {
            Dictionary<string, SimulatorValueItem> result = new Dictionary<string, SimulatorValueItem>();
            var routine = new WeatherRoutine(routineRevision, inputData, _logger);
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

internal class WeatherRoutine : RoutineImplementationBase
{
    public WeatherRoutine(SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger)
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