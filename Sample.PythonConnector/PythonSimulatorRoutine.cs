using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cognite.Simulator.Utils;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

namespace Sample.PythonConnector;

/// <summary>
/// Routine implementation for Python-based simulations
/// </summary>
public class PythonSimulatorRoutine : RoutineImplementationBase
{
    private readonly string _modelPath;
    private readonly PythonConfig _config;
    private readonly Dictionary<string, object> _variables = new();
    private readonly string _tempDir;
    private readonly ILogger _logger;

    public PythonSimulatorRoutine(
        string modelPath,
        SimulatorRoutineRevision routineRevision,
        Dictionary<string, SimulatorValueItem> inputData,
        ILogger logger,
        PythonConfig config)
        : base(routineRevision, inputData, logger)
    {
        _modelPath = modelPath;
        _config = config;
        _logger = logger;
        _tempDir = Path.Combine(Path.GetTempPath(), $"sim_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public override void SetInput(
        SimulatorRoutineRevisionInput inputConfig,
        SimulatorValueItem input,
        Dictionary<string, string> arguments,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(arguments);

        var variableName = arguments["variable"];

        object value = input.ValueType switch
        {
            SimulatorValueType.DOUBLE => (input.Value as SimulatorValue.Double)?.Value ?? 0.0,
            SimulatorValueType.STRING => (input.Value as SimulatorValue.String)?.Value ?? "",
            _ => throw new NotImplementedException($"{input.ValueType} not implemented")
        };

        _variables[variableName] = value;
        
        _logger.LogDebug("Set variable {Variable} = {Value}", variableName, value);

        input.SimulatorObjectReference = new Dictionary<string, string>
        {
            { "variable", variableName }
        };
    }

    public override SimulatorValueItem GetOutput(
        SimulatorRoutineRevisionOutput outputConfig,
        Dictionary<string, string> arguments,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(outputConfig);
        ArgumentNullException.ThrowIfNull(arguments);

        var variableName = arguments["variable"];

        if (!_variables.TryGetValue(variableName, out var rawValue))
        {
            throw new InvalidOperationException($"Variable '{variableName}' not found in simulation results");
        }

        SimulatorValue value = outputConfig.ValueType switch
        {
            SimulatorValueType.DOUBLE => new SimulatorValue.Double(Convert.ToDouble(rawValue)),
            SimulatorValueType.STRING => new SimulatorValue.String(rawValue?.ToString() ?? ""),
            _ => throw new NotImplementedException($"{outputConfig.ValueType} not implemented")
        };

        _logger.LogDebug("Get variable {Variable} = {Value}", variableName, rawValue);

        return new SimulatorValueItem
        {
            ValueType = outputConfig.ValueType,
            Value = value,
            ReferenceId = outputConfig.ReferenceId,
            SimulatorObjectReference = new Dictionary<string, string>
            {
                { "variable", variableName }
            },
            TimeseriesExternalId = outputConfig.SaveTimeseriesExternalId,
        };
    }

    public override void RunCommand(Dictionary<string, string> arguments, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        _logger.LogInformation("Executing Python simulation");

        try
        {
            // Write input variables to JSON file
            var inputFile = Path.Combine(_tempDir, "input.json");
            var inputJson = JsonSerializer.Serialize(_variables, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(inputFile, inputJson);

            // Prepare output file path
            var outputFile = Path.Combine(_tempDir, "output.json");

            // Execute Python script
            var result = ExecutePythonScript(_modelPath, inputFile, outputFile, token);

            if (result.ExitCode != 0)
            {
                _logger.LogError("Python script failed with exit code {ExitCode}: {Error}", 
                    result.ExitCode, result.Error);
                throw new Exception($"Simulation failed: {result.Error}");
            }

            // Read output variables from JSON file
            if (File.Exists(outputFile))
            {
                var outputJson = File.ReadAllText(outputFile);
                var outputVariables = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(outputJson);

                if (outputVariables != null)
                {
                    foreach (var kvp in outputVariables)
                    {
                        // Handle JsonElement type from deserialization
                        var element = kvp.Value;
                        _variables[kvp.Key] = element.ValueKind switch
                        {
                            JsonValueKind.Number => element.GetDouble(),
                            JsonValueKind.String => element.GetString() ?? "",
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => element.ToString()
                        };
                    }
                }
            }

            _logger.LogInformation("Python simulation completed successfully");
            _logger.LogDebug("Output: {Output}", result.Output);
        }
        finally
        {
            // Clean up temporary files
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temporary directory: {TempDir}", _tempDir);
            }
        }
    }

    private ProcessExecutionResult ExecutePythonScript(
        string scriptPath,
        string inputFile,
        string outputFile,
        CancellationToken token)
    {
        var arguments = $"\"{scriptPath}\" \"{inputFile}\" \"{outputFile}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = _config.PythonExecutable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _config.WorkingDirectory
        };

        // Add custom environment variables
        foreach (var kvp in _config.EnvironmentVariables)
        {
            startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
        }

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                _logger.LogDebug("Python stdout: {Line}", e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                _logger.LogWarning("Python stderr: {Line}", e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait with cancellation support
        while (!process.WaitForExit(1000))
        {
            if (token.IsCancellationRequested)
            {
                process.Kill();
                throw new OperationCanceledException("Python script execution was cancelled");
            }
        }

        return new ProcessExecutionResult
        {
            ExitCode = process.ExitCode,
            Output = outputBuilder.ToString(),
            Error = errorBuilder.ToString()
        };
    }
}
