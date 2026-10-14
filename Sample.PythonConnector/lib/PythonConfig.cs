using Cognite.Simulator.Utils.Automation;

namespace Sample.PythonConnector.Lib;

public class PythonConfig : AutomationConfig
{
    public string? PythonDLL { get; set; }
    
    public string? PythonHome { get; set; }
    
    public List<string> PythonPaths { get; set; } = new();

    internal string PythonDirectory { get; private set; } = "";
    internal string DefinitionPyPath => "definition.py";
    internal string ClientPyPath => "client.py";
    internal string RoutinePyPath => "routine.py";
    
    internal List<string> ValidatedPythonPaths { get; private set; } = new();

    public void Validate()
    {
        var loader = EmbeddedPythonLoader.GetInstance();
        loader.ExtractPythonFiles();
        
        PythonDirectory = loader.PythonDirectory;

        ValidateFileExists(DefinitionPyPath, "definition.py");
        ValidateFileExists(ClientPyPath, "client.py");
        ValidateFileExists(RoutinePyPath, "routine.py");
        
        ValidatePythonPaths();
    }

    private void ValidateFileExists(string fileName, string description)
    {
        var fullPath = Path.Combine(PythonDirectory, fileName);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException(
                $"Embedded Python file '{description}' was not extracted correctly.\n" +
                $"Expected location: {fullPath}\n" +
                $"This indicates a build configuration issue.");
    }
    
    private void ValidatePythonPaths()
    {
        ValidatedPythonPaths = new List<string>();
        
        foreach (var path in PythonPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
                
            var fullPath = Path.GetFullPath(path);
            
            if (!Path.IsPathFullyQualified(path) && !Path.IsPathRooted(path))
            {
                throw new InvalidOperationException(
                    $"Python path must be absolute: '{path}'. " +
                    "Relative paths are not allowed for security reasons.");
            }
            
            if (!Directory.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"Python path does not exist: '{fullPath}'. " +
                    "All configured Python paths must exist.");
            }
            
            var normalizedInput = Path.GetFullPath(path);
            if (normalizedInput != fullPath)
            {
                throw new InvalidOperationException(
                    $"Python path normalization mismatch: '{path}' -> '{fullPath}'. " +
                    "This may indicate a path traversal attempt.");
            }
            
            ValidatedPythonPaths.Add(fullPath);
        }
    }
}
