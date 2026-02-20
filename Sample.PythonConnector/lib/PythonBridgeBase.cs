using System.IO;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using Microsoft.Extensions.Logging;
using Python.Runtime;

namespace Sample.PythonConnector.Lib;

public abstract class PythonBridgeBase
{
    protected ILogger Logger { get; }

    protected PythonBridgeBase(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Logger = logger;
    }

    /// <summary>
    /// Ensures Python engine is initialized and configured with paths.
    /// </summary>
    protected static void InitializePythonEngine(PythonConfig config, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        var loader = EmbeddedPythonLoader.GetInstance(logger);
        
        if (loader.IsPythonInitialized) return;

        try
        {
            loader.InitializePython(
                config.PythonDLL,
                config.PythonHome,
                config.ValidatedPythonPaths
            );
            
            logger.LogInformation("Python engine initialized with config. Directory: {Directory}", 
                config.PythonDirectory);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Python engine");
            throw new SimulatorConnectionException("Failed to initialize Python engine", ex);
        }
    }

    protected dynamic LoadPythonModule(string modulePath, string className, string? arg = null)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        ArgumentNullException.ThrowIfNull(className);

        try
        {
            using (Py.GIL())
            {
                string moduleName = Path.GetFileNameWithoutExtension(modulePath);
                dynamic module = Py.Import(moduleName);
                dynamic pyClass = module.GetAttr(className);
                var result = arg == null ? pyClass() : pyClass(arg);
                return result;
            }
        }
        catch (PythonException ex)
        {
            Logger.LogError(ex, "Failed to load Python module {Module}.{Class}", modulePath, className);
            throw new SimulatorConnectionException($"Failed to load module {modulePath}.{className}: {ex.Message}", ex);
        }
    }

    protected T RunPython<T>(Func<T> pythonCall, string operationName, bool isSimulation = false)
    {
        ArgumentNullException.ThrowIfNull(pythonCall);
        ArgumentNullException.ThrowIfNull(operationName);

        using (Py.GIL())
        {
            try
            {
                return pythonCall();
            }
            catch (PythonException ex)
            {
                Logger.LogError(ex, "Python error in {Operation}", operationName);
                if (isSimulation)
                    throw new SimulationException($"Failed to {operationName}: {ex.Message}", ex);
                else
                    throw new SimulatorConnectionException($"{operationName} failed: {ex.Message}", ex);
            }
        }
    }

    protected void RunPython(Action pythonCall, string operationName, bool isSimulation = false)
    {
        ArgumentNullException.ThrowIfNull(pythonCall);
        ArgumentNullException.ThrowIfNull(operationName);

        using (Py.GIL())
        {
            try
            {
                pythonCall();
            }
            catch (PythonException ex)
            {
                Logger.LogError(ex, "Python error in {Operation}", operationName);
                if (isSimulation)
                    throw new SimulationException($"Failed to {operationName}: {ex.Message}", ex);
                else
                    throw new SimulatorConnectionException($"{operationName} failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Runs a Python call on a dedicated thread with a large stack size.
    /// This is required for importing native libraries like MuJoCo that can
    /// exhaust the default ThreadPool stack size (1MB on macOS/Linux).
    /// </summary>
    protected T RunPythonWithLargeStack<T>(Func<T> pythonCall, string operationName, bool isSimulation = false)
    {
        ArgumentNullException.ThrowIfNull(pythonCall);
        ArgumentNullException.ThrowIfNull(operationName);

        T result = default!;
        Exception? caughtException = null;

        const int stackSize = 8 * 1024 * 1024;
        
        var thread = new Thread(() =>
        {
            try
            {
                result = RunPython(pythonCall, operationName, isSimulation);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
        }, stackSize);

        thread.Start();
        thread.Join();

        if (caughtException != null)
        {
            throw caughtException;
        }

        return result;
    }

    /// <summary>
    /// Runs a Python call on a dedicated thread with a large stack size.
    /// This is required for importing native libraries like MuJoCo that can
    /// exhaust the default ThreadPool stack size (1MB on macOS/Linux).
    /// </summary>
    protected void RunPythonWithLargeStack(Action pythonCall, string operationName, bool isSimulation = false)
    {
        RunPythonWithLargeStack<object?>(() => { pythonCall(); return null; }, operationName, isSimulation);
    }

    protected static PyDict ToPyDict(Dictionary<string, string> dict)
    {
        ArgumentNullException.ThrowIfNull(dict);

        var pyDict = new PyDict();
        foreach (var kvp in dict)
        {
            pyDict[kvp.Key.ToPython()] = kvp.Value.ToPython();
        }
        return pyDict;
    }
}
