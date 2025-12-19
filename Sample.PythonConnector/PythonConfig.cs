using Cognite.Simulator.Utils.Automation;

namespace Sample.PythonConnector;

/// <summary>
/// Configuration for Python-based simulator
/// </summary>
public class PythonConfig : AutomationConfig
{
    /// <summary>
    /// Path to the Python executable (e.g., "python", "python3", or full path)
    /// </summary>
    public string PythonExecutable { get; set; } = "python3";

    /// <summary>
    /// Directory containing Python simulation scripts
    /// </summary>
    public string ScriptsDirectory { get; set; } = "./scripts";

    /// <summary>
    /// Timeout for Python script execution in milliseconds
    /// </summary>
    public int ExecutionTimeout { get; set; } = 300000; // 5 minutes

    /// <summary>
    /// Working directory for Python script execution
    /// </summary>
    public string WorkingDirectory { get; set; } = "./";

    /// <summary>
    /// Additional environment variables to pass to Python process
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
}
