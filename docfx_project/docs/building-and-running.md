# Building and Running Your Connector

This guide explains how to build, run, and debug your simulator connector, including understanding entry points and using VS Code configuration.

## Prerequisites

You should have completed:
- [Create Your First Connector](create-connector.md) - Basic connector structure
- [Implement Model Parsing](model-parsing.md) - Model validation
- [Implement Routines](implement-routine.md) - Simulation execution

## Understanding the Entry Point

### Program.cs: The Application Entry Point

Your connector is a console application with a simple entry point in [Program.cs](../../Sample.BasicComConnector/Program.cs):

### What Gets Built

When you run `dotnet build`, the compiler:

1. Compiles all `.cs` files in your project
2. Resolves and downloads NuGet package dependencies
3. Produces an executable in `bin/Debug/net8.0/` or `bin/Release/net8.0/`
4. Creates a `.dll` (your code) and an `.exe` (launcher)

**Output structure:**

```
bin/Debug/net8.0/
├── YourConnector.exe          # Windows launcher
├── YourConnector.dll          # Your compiled code
├── YourConnector.deps.json    # Dependency manifest
├── YourConnector.runtimeconfig.json  # Runtime configuration
├── Cognite.Simulator.Utils.dll  # SDK dependency
└── [other dependencies...]     # Additional libraries
```

## Building Your Connector

### Command Line Build

**Debug build** (includes debug symbols, no optimization):

```bash
dotnet build
```

**Release build** (optimized, smaller binaries):

```bash
dotnet build --configuration Release
```

**Build for specific platform** (when targeting COM):

```bash
dotnet build --configuration Release --runtime win-x64
```

## Running Your Connector

### Command Line Execution

**Run directly**:

```bash
dotnet run
```

> Note: The connector looks for `config.yml` in the current working directory. Ensure you run the connector from the project root where `config.yml` exists.

## Debugging with VS Code

### Prerequisites

Install VS Code extensions:

1. **C# Dev Kit** - Microsoft's C# language support
2. **C#** - IntelliSense, debugging, and code navigation

### VS Code Configuration Files

The Sample.BasicComConnector includes debug configuration for VS Code in `.vscode/launch.json` and `.vscode/tasks.json`.

## Deployment

### Self-Contained Deployment

Create a deployment package with all dependencies:

```bash
dotnet publish --configuration Release --runtime win-x64 --self-contained
```

This produces a folder with everything needed to run without .NET SDK installed:

```
bin/Release/net8.0/win-x64/publish/
├── YourConnector.exe
├── YourConnector.dll
├── config.yml (copy manually)
└── [all dependencies]
```

### Framework-Dependent Deployment

Create a deployment that requires .NET Runtime on the target machine:

```bash
dotnet publish --configuration Release --runtime win-x64
```

Smaller deployment size, but requires .NET 8.0 Runtime installed on the server.

## Running as a Service

### Windows Service

Use `sc.exe` to install as a Windows Service:

```bash
sc create "MySimulatorConnector" binPath= "C:\path\to\YourConnector.exe"
sc start "MySimulatorConnector"
```

---

**Next:** Continue to [Testing Your Connector](testing.md) to learn testing strategies and best practices.
