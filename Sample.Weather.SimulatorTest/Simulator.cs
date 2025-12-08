using Cognite.Simulator.Utils;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;
using System.Text.Json;

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
    Dictionary<string, string> _inputData = new Dictionary<string, string>();
    
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
            // Value = SimulatorValue.Create(_inputData["humidity"]),
            Value = SimulatorValue.Create(_inputData["temperature"]),
        };
        return resultItem;
    }

    public override void RunCommand(Dictionary<string, string> arguments, CancellationToken token)
    {
        Console.WriteLine("Handling run command");
        double output = getWeatherHumidityAsync(arguments, token).Result;
        // _inputData.Add("humidity", output.ToString());
        _inputData.Add("temperature", output.ToString());
    }
    public async Task<double> getWeatherHumidityAsync(Dictionary<string, string> arguments, CancellationToken token)
    {
        using (var httpClient = new HttpClient())
        {
            var response = await httpClient.GetAsync($"https://api.open-meteo.com/v1/forecast?latitude=52.52&longitude=13.41&current=temperature_2m,wind_speed_10m&hourly=temperature_2m,relative_humidity_2m,wind_speed_10m");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"API Response: {json}");
                var jsonDocument = JsonDocument.Parse(json);
                if (jsonDocument.RootElement.TryGetProperty("hourly", out var hourly))
                {
                    // if (hourly.TryGetProperty("relative_humidity_2m", out var humidityArray))
                    // {
                    //     return humidityArray[0].GetDouble();
                    // }

                    if (hourly.TryGetProperty("temperature_2m", out var temperatureArray))
                    {
                        return temperatureArray[0].GetDouble();
                    }
                }
            }
            else
            {
                Console.WriteLine($"API Error: {response.StatusCode}");
            }
        }
        return 0.0;
    }
    public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments, CancellationToken token)
    {
        Console.WriteLine("Handling inputs");
        var valueStr = arguments["location"];
        _inputData.Add("location", valueStr);
    }
}