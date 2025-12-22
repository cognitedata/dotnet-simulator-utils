using Cognite.Simulator.Utils.Automation;

namespace Sample.PythonConnector;

public class PythonConfig : AutomationConfig
{
    public string? PythonDLL { get; set; }
    public string? PythonHome { get; set; }
    public string PythonDirectory { get; set; } = "./python";
    public string DefinitionPyPath { get; set; } = "definition.py";
    public string ClientPyPath { get; set; } = "client.py";
    public string RoutinePyPath { get; set; } = "routine.py";
    public List<string> PythonPaths { get; set; } = new();

    public void Validate()
    {
        if (!Directory.Exists(PythonDirectory))
            throw new InvalidOperationException($"Python directory not found: {PythonDirectory}");

        ValidateFileExists(ClientPyPath, "Client module");
        ValidateFileExists(RoutinePyPath, "Routine module");
        ValidateFileExists(DefinitionPyPath, "Definition module");
    }

    private void ValidateFileExists(string relativePath, string description)
    {
        var fullPath = Path.Combine(PythonDirectory, relativePath);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"{description} not found: {fullPath}");
    }
}
