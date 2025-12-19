using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

namespace Sample.PythonConnector;

/// <summary>
/// Client for executing Python-based simulations
/// </summary>
public class PythonSimulatorClient : ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<PythonSimulatorClient> _logger;
    private readonly PythonConfig _config;
    private string _pythonVersion = "Unknown";

    public PythonSimulatorClient(
        ILogger<PythonSimulatorClient> logger,
        DefaultConfig<PythonConfig> config)
    {
        _logger = logger;
        _config = config?.Automation ?? new PythonConfig();
        
        // Get Python version at initialization
        _pythonVersion = GetPythonVersionInternal();
        _logger.LogInformation("Python Simulator Client initialized with Python version: {Version}", _pythonVersion);
    }

    public Task TestConnection(CancellationToken token)
    {
        try
        {
            var version = GetPythonVersionInternal();
            _logger.LogInformation("Python connection test successful. Version: {Version}", version);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Python connection test failed");
            throw new SimulatorConnectionException("Failed to connect to Python", ex);
        }
    }

    public string GetConnectorVersion(CancellationToken token)
    {
        return "1.0.0";
    }

    public string GetSimulatorVersion(CancellationToken token)
    {
        return _pythonVersion;
    }

    private string GetPythonVersionInternal()
    {
        try
        {
            var result = ExecutePythonCommand("--version", TimeSpan.FromSeconds(5));
            return result.Output.Trim();
        }
        catch
        {
            return "Unknown";
        }
    }

    public async Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(state);
        
        await _semaphore.WaitAsync(token).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Extracting model information from {FilePath}", state.FilePath);

            // Validate the Python script exists
            if (!File.Exists(state.FilePath))
            {
                _logger.LogError("Model file not found: {FilePath}", state.FilePath);
                state.ParsingInfo.SetFailure("Model file not found");
                return;
            }

            // Optionally validate the script syntax
            var validationArgs = $"-m py_compile \"{state.FilePath}\"";
            var result = ExecutePythonCommand(validationArgs, TimeSpan.FromSeconds(30));

            if (result.ExitCode == 0)
            {
                _logger.LogInformation("Model validation successful");
                state.ParsingInfo.SetSuccess();
            }
            else
            {
                _logger.LogError("Model validation failed: {Error}", result.Error);
                state.ParsingInfo.SetFailure($"Syntax error: {result.Error}");
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Dictionary<string, SimulatorValueItem>> RunSimulation(
        DefaultModelFilestate modelState,
        SimulatorRoutineRevision routineRevision,
        Dictionary<string, SimulatorValueItem> inputData,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(routineRevision);

        await _semaphore.WaitAsync(token).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Running simulation for model: {ModelPath}", modelState.FilePath);

            var routine = new PythonSimulatorRoutine(
                modelState.FilePath,
                routineRevision,
                inputData,
                _logger,
                _config);

            return routine.PerformSimulation(token);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Execute a Python command and return the result
    /// </summary>
    private ProcessExecutionResult ExecutePythonCommand(string arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _config.PythonExecutable,
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
                outputBuilder.AppendLine(e.Data);
        };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill();
            throw new TimeoutException($"Python process timed out after {timeout.TotalSeconds} seconds");
        }

        return new ProcessExecutionResult
        {
            ExitCode = process.ExitCode,
            Output = outputBuilder.ToString(),
            Error = errorBuilder.ToString()
        };
    }
}

/// <summary>
/// Result of a process execution
/// </summary>
public class ProcessExecutionResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
