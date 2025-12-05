# Understanding Simulator Integration

This guide explains how simulators can be integrated with Cognite Data Fusion (CDF) and provides an overview of different integration patterns supported by the SDK.

For comprehensive information about CDF's simulator integration capabilities, see the [official CDF Simulators documentation](https://docs.cognite.com/cdf/integration/guides/simulators/).

## Integration approaches overview

Simulators can be integrated with CDF using various communication methods, depending on how the simulator exposes its functionality. The Simulator Integration SDK is designed to support multiple integration approaches while providing a consistent interface to CDF.

### Common integration types

**1. COM Automation (Component Object Model)**
- **When to use**: Windows-based simulators that expose COM interfaces
- **Examples**: Petro-SIM, Symmetry, HYSYS, UniSim Design, DWSIM
- **SDK Support**: `AutomationClient` helper class available

**2. TCP/Socket Communication**
- **When to use**: Simulators that run as separate processes with network interfaces
- **Examples**: Simulators which expose some form of socket APIs
- **SDK Support**: Direct `ISimulatorClient` implementation

**3. REST/HTTP APIs**
- **When to use**: Simulators that expose web service interfaces
- **Examples**: Cloud-based simulators, modern simulators with HTTP APIs
- **SDK Support**: Direct `ISimulatorClient` implementation with HttpClient

**4. Native Library Integration (DLL/Shared Object)**
- **When to use**: Simulators that provide native libraries
- **Examples**: Simulators with native SDKs
- **SDK Support**: Direct `ISimulatorClient` implementation with P/Invoke

**5. File-Based Integration**
- **When to use**: Batch simulators or legacy systems without APIs
- **Examples**: Simulators that read input files and write output files
- **SDK Support**: Direct `ISimulatorClient` implementation

## About this tutorial

### Tutorial strategy

This tutorial uses Excel as the integration example

**The patterns and principles demonstrated in this tutorial apply broadly to simulator integration**, regardless of the specific communication method your simulator uses.

### What you'll learn  
You'll learn **Core SDK Concepts** (interfaces, runtime, configuration), **Connection Management** (establishing and maintaining simulator connections), **Model Parsing** (extracting model structure and properties), and **Routine Implementation** (mapping CDF routine steps to simulator operations).

## SDK architecture overview

The Simulator Integration SDK provides a framework that handles CDF integration while you focus on simulator-specific logic.

### What the SDK provides

**CDF integration** (you don't need to implement):
- Communication with CDF APIs
- Model and routine synchronization
- Simulation scheduling and execution
- Result storage
- State management

**Your responsibilities** (you implement):
- Connecting to your simulator
- Validating models and extracting model information
- Executing simulation steps
- Reading and writing simulator data

Learn more about [how connectors work](https://docs.cognite.com/cdf/integration/guides/simulators/connectors/).

### Core interfaces

You'll implement three main components:

**1. ISimulatorClient**: Manages connection to the simulator, reports simulator and connector versions, extracts model information, and executes simulations.


**2. RoutineImplementationBase:** Sets input values on the simulator, gets output values from the simulator, and runs commands on the simulator.


**3. Configuration:** Defines simulator connection settings and inherits from `AutomationConfig` (or creates a custom config).


## Universal integration patterns

While connection mechanisms differ, the **integration patterns remain consistent**:

### Pattern: three-method routine
All connectors implement the same three methods:
- `SetInput()` - Write values to simulator
- `GetOutput()` - Read values from simulator
- `RunCommand()` - Execute simulator operations

### Pattern: model representation
All connectors parse models into the same structure:
- Nodes (streams, equipment)
- Edges (connections)
- Properties with units
- Thermodynamic data

## Platform considerations

Different integration approaches have different platform requirements.

**For this tutorial**: We use Excel (COM), which requires Windows. The SDK itself supports cross-platform deployment when using appropriate integration methods.

The project would also need a Runtime Identifier (RID), this specifies the platform and architecture where your application runs.

**Common RIDs:** `win-x64` (Windows 64-bit), `linux-x64` (Linux 64-bit), `osx-arm64` (macOS ARM 64-bit, Apple Silicon).

**Specifying RID in your project:**

Add the RID to your `.csproj` file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>
</Project>
```

**Next:** Continue to [Prerequisites & Setup](prerequisites.md) to set up your development environment.