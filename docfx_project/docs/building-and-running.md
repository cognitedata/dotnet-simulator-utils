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

When you run `dotnet build`, the compiler compiles all `.cs` files, resolves NuGet dependencies (external libraries), and produces an executable with a `.dll` (your code) and `.exe` (launcher) in `bin/Debug/<target-framework>/` or `bin/Release/<target-framework>/`.

## Building & Running Your Connector

```
dotnet build                                      # Debug build
dotnet build -c Release -r win-x64                # Release build (add -r for specific platform)
dotnet run                                        # Build and run
```

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
dotnet publish -r win-x64 -c Release \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  /p:PublishTrimmed=false \
  /p:EnableCompressionInSingleFile=true
```

This produces a single file binary with everything needed to run in the `bin/Release/<target-framework>/win-x64/publish/`. This makes it easier to share your connector with others, as a single file packages everything needed to run the connector on another environment.

## Running as a Windows Service

Use `sc.exe` to install as a Windows Service:

```bash
sc create "MySimulatorConnector" binPath= "C:\path\to\YourConnector.exe"
sc start "MySimulatorConnector"
```

---

**Next:** Continue to [Testing Your Connector](testing-and-extras.md) to learn testing strategies and best practices.