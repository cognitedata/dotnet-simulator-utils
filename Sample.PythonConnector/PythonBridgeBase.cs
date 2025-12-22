using System.IO;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using Microsoft.Extensions.Logging;
using Python.Runtime;

namespace Sample.PythonConnector;

public abstract class PythonBridgeBase
{
    protected ILogger Logger { get; }
    
    private static volatile bool _pythonInitialized;
    private static readonly object _initLock = new();

    protected PythonBridgeBase(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Logger = logger;
    }

    protected static void InitializePythonEngine(PythonConfig config, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        if (_pythonInitialized) return;

        lock (_initLock)
        {
            if (_pythonInitialized) return;

            try
            {
                if (!string.IsNullOrEmpty(config.PythonDLL))
                    Runtime.PythonDLL = config.PythonDLL;

                if (!string.IsNullOrEmpty(config.PythonHome))
                    PythonEngine.PythonHome = config.PythonHome;

                PythonEngine.Initialize();
                
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    var paths = new[] { Path.GetFullPath(config.PythonDirectory) }
                        .Concat(config.PythonPaths.Select(Path.GetFullPath));
                    
                    foreach (var path in paths.Where(p => !((IEnumerable<dynamic>)sys.path).Any(sp => sp.ToString() == p)))
                    {
                        sys.path.append(path);
                    }

                    string version = sys.version.ToString();
                    logger.LogInformation("Python engine initialized. Version: {Version}", version);
                }

                _pythonInitialized = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize Python engine");
                throw new SimulatorConnectionException("Failed to initialize Python engine", ex);
            }
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
                return arg == null ? pyClass() : pyClass(arg);
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
