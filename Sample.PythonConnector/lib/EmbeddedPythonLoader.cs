using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Python.Runtime;

namespace Sample.PythonConnector.Lib;

public sealed class EmbeddedPythonLoader
{
    private static EmbeddedPythonLoader? _instance;
    private static readonly object _lock = new();
    
    private readonly string _extractionDirectory;
    private readonly ILogger? _logger;
    private bool _extracted;
    private bool _pythonInitialized;

    private static readonly string[] RequiredPythonFiles = 
    {
        "definition.py",
        "client.py", 
        "routine.py"
    };

    public string PythonDirectory => _extractionDirectory;
    public bool IsPythonInitialized => _pythonInitialized;

    private EmbeddedPythonLoader(ILogger? logger = null)
    {
        _logger = logger;
        
        var assemblyHash = ComputeAssemblyHash();
        var dirName = $"PythonConnector_{assemblyHash[..8]}";
        _extractionDirectory = Path.Combine(Path.GetTempPath(), dirName);
    }

    public static EmbeddedPythonLoader GetInstance(ILogger? logger = null)
    {
        if (_instance != null) return _instance;
        
        lock (_lock)
        {
#pragma warning disable CA1508 // False positive: double-check locking pattern
            _instance ??= new EmbeddedPythonLoader(logger);
#pragma warning restore CA1508
        }
        return _instance;
    }

    public void ExtractPythonFiles()
    {
        if (_extracted) return;

        lock (_lock)
        {
            if (_extracted) return;

            if (!Directory.Exists(_extractionDirectory))
            {
                Directory.CreateDirectory(_extractionDirectory);
                _logger?.LogInformation("Created Python extraction directory: {Directory}", _extractionDirectory);
            }

            var assembly = Assembly.GetExecutingAssembly();

            foreach (var pythonFile in RequiredPythonFiles)
            {
                ExtractResource(assembly, pythonFile);
            }

            _extracted = true;
            _logger?.LogInformation("Extracted {Count} Python files to {Directory}", 
                RequiredPythonFiles.Length, _extractionDirectory);
        }
    }

    public string? FindPythonLibrary()
    {
        var envPath = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            _logger?.LogDebug("Using Python library from PYTHONNET_PYDLL: {Path}", envPath);
            return envPath;
        }

        try
        {
            var pythonCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";
            
            var psi = new ProcessStartInfo
            {
                FileName = pythonCmd,
                Arguments = "-c \"from find_libpython import find_libpython; print(find_libpython())\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (process.ExitCode == 0 && File.Exists(output))
            {
                _logger?.LogInformation("Found Python library via find-libpython: {Path}", output);
                return output;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to auto-detect Python library path");
        }

        return null;
    }

    public void InitializePython(string? pythonDll = null, string? pythonHome = null, IEnumerable<string>? additionalPaths = null)
    {
        if (_pythonInitialized)
        {
            if (additionalPaths != null)
            {
                AddPathsToSys(additionalPaths);
            }
            return;
        }

        lock (_lock)
        {
            if (_pythonInitialized) return;

            if (!_extracted)
            {
                throw new InvalidOperationException(
                    "Python files must be extracted before initializing. Call ExtractPythonFiles() first.");
            }

            if (!PythonEngine.IsInitialized)
            {
                var effectivePythonDll = pythonDll ?? FindPythonLibrary();

                if (!string.IsNullOrEmpty(effectivePythonDll))
                {
                    Runtime.PythonDLL = effectivePythonDll;
                    _logger?.LogDebug("Set Python DLL: {DLL}", effectivePythonDll);
                }
                else
                {
                    _logger?.LogWarning(
                        "Could not auto-detect Python library. " +
                        "Install find-libpython or set PYTHONNET_PYDLL environment variable.");
                }

                if (!string.IsNullOrEmpty(pythonHome))
                {
                    PythonEngine.PythonHome = pythonHome;
                }

                PythonEngine.Initialize();
                PythonEngine.BeginAllowThreads();
                
                _logger?.LogInformation("Python engine initialized");
            }

            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                
                var fullExtractionPath = Path.GetFullPath(_extractionDirectory);
                if (!PathExistsInSysPath(sys, fullExtractionPath))
                {
                    sys.path.append(fullExtractionPath);
                }

                if (additionalPaths != null)
                {
                    foreach (var path in additionalPaths)
                    {
                        var fullPath = Path.GetFullPath(path);
                        if (!PathExistsInSysPath(sys, fullPath))
                        {
                            sys.path.append(fullPath);
                        }
                    }
                }

                string version = sys.version.ToString();
                _logger?.LogInformation("Python {Version} configured, scripts at {Path}", 
                    version.Split(' ')[0], _extractionDirectory);
            }

            _pythonInitialized = true;
        }
    }

    private void AddPathsToSys(IEnumerable<string> paths)
    {
        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            foreach (var path in paths)
            {
                var fullPath = Path.GetFullPath(path);
                if (!PathExistsInSysPath(sys, fullPath))
                {
                    sys.path.append(fullPath);
                    _logger?.LogDebug("Added Python path: {Path}", fullPath);
                }
            }
        }
    }

    private static bool PathExistsInSysPath(dynamic sys, string path)
    {
        foreach (var existingPath in sys.path)
        {
            if (existingPath.ToString() == path)
                return true;
        }
        return false;
    }

    private void ExtractResource(Assembly assembly, string fileName)
    {
        using var stream = assembly.GetManifestResourceStream(fileName) 
            ?? throw new InvalidOperationException(
                $"Embedded Python file '{fileName}' not found. " +
                $"Ensure it exists in python/ folder and is marked as EmbeddedResource.");

        var targetPath = Path.Combine(_extractionDirectory, fileName);
        
        if (File.Exists(targetPath))
        {
            _logger?.LogDebug("Python file already exists: {File}", fileName);
            return;
        }

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        
        var tempPath = targetPath + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, memoryStream.ToArray());
            File.Move(tempPath, targetPath, overwrite: true);
            File.SetAttributes(targetPath, FileAttributes.ReadOnly);
            
            _logger?.LogDebug("Extracted: {File}", fileName);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private static string ComputeAssemblyHash()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var location = assembly.Location;
        
        if (string.IsNullOrEmpty(location))
        {
            var name = assembly.GetName();
            var input = $"{name.Name}_{name.Version}";
            return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input)));
        }
        
        using var stream = File.OpenRead(location);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
