using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

namespace Sample.PythonConnector.Lib;

public class PythonBridgeClient : PythonBridgeBase, ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly PythonConfig _config;
    private dynamic? _clientInstance;
    private bool _disposed;

    public PythonBridgeClient(
        ILogger<PythonBridgeClient> logger,
        DefaultConfig<PythonConfig> config)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(config);
        
        _config = config.Automation ?? throw new ArgumentException("Automation configuration is required", nameof(config));
        
        _config.Validate();
        InitializePythonEngine(_config, Logger);
        LoadClientModule();
    }

    private void LoadClientModule()
    {
        _clientInstance = LoadPythonModule(
            _config.ClientPyPath,
            "SimulatorClient");
    }

    public async Task TestConnection(CancellationToken token)
    {
        if (_clientInstance == null)
            throw new SimulatorConnectionException("Python client not initialized. LoadClientModule may have failed during construction.");

        await Task.Run(() => 
        {
            token.ThrowIfCancellationRequested();
            RunPythonWithLargeStack(() => _clientInstance.test_connection(), "connection test");
        }, token).ConfigureAwait(false);
    }

    public string GetConnectorVersion(CancellationToken token)
    {
        if (_clientInstance == null)
            throw new SimulatorConnectionException("Python client not initialized. LoadClientModule may have failed during construction.");

        try
        {
            return RunPython(() => _clientInstance.get_connector_version().ToString(), "get connector version");
        }
        catch (SimulatorConnectionException ex)
        {
            Logger.LogWarning(ex, "Failed to get connector version from Python client");
            return "Unknown";
        }
    }

    public string GetSimulatorVersion(CancellationToken token)
    {
        if (_clientInstance == null)
            throw new SimulatorConnectionException("Python client not initialized. LoadClientModule may have failed during construction.");

        try
        {
            return RunPythonWithLargeStack(() => _clientInstance.get_simulator_version().ToString(), "get simulator version");
        }
        catch (SimulatorConnectionException ex)
        {
            Logger.LogWarning(ex, "Failed to get simulator version from Python client");
            return "Unknown";
        }
    }

    public async Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_clientInstance == null)
            throw new SimulatorConnectionException("Python client not initialized. LoadClientModule may have failed during construction.");

        await _semaphore.WaitAsync(token).ConfigureAwait(false);
        try
        {
            RunPythonWithLargeStack(() =>
            {
                dynamic result = _clientInstance.open_model(state.FilePath);
                bool success = (bool)result["success"];
                if (success)
                {
                    state.ParsingInfo.SetSuccess();
                }
                else
                {
                    string error = result["error"]?.ToString() ?? "Unknown error";
                    state.ParsingInfo.SetFailure(error);
                }
            }, "open model");
        }
        catch (SimulatorConnectionException ex)
        {
            state.ParsingInfo.SetFailure($"Python error: {ex.Message}");
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
            var routine = new PythonBridgeRoutine(
                modelState.FilePath,
                routineRevision,
                inputData,
                Logger,
                _config);

            return routine.PerformSimulation(token);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _semaphore?.Dispose();
            }
            _disposed = true;
        }
    }
}
