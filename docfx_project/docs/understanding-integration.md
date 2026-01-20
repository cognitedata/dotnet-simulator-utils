# Understanding Simulator Integration

This guide explains how to integrate simulators with Cognite Data Fusion (CDF) using the SDK, covering various integration patterns. For more details, see the [official CDF Simulators documentation](https://docs.cognite.com/cdf/integration/guides/simulators/).

## Integration Approaches Overview

The SDK supports multiple communication methods for integrating simulators with CDF.

### Common Integration Types

- **COM Automation**: For Windows-based simulators with COM interfaces (e.g., Petro-SIM, HYSYS). The SDK provides an `AutomationClient` helper.
- **TCP/Socket**: For simulators with network interfaces. Requires a direct `ISimulatorClient` implementation.
- **REST/HTTP APIs**: For simulators with web service interfaces. Implemented with `ISimulatorClient` and `HttpClient`.
- **Native Library (DLL/Shared Object)**: For simulators with native libraries. Implemented with `ISimulatorClient` and P/Invoke.
- **File-Based**: For batch simulators without APIs. Implemented with `ISimulatorClient`.

## About this Tutorial

This tutorial uses Excel as an example, but the concepts apply to all integration types. You'll learn about core SDK concepts, connection management, model parsing, and routine implementation.

## SDK Architecture Overview

The SDK handles CDF integration, allowing you to focus on simulator-specific logic.

### SDK Responsibilities

- **CDF Integration**: Handles API communication, model/routine synchronization, scheduling, and result storage.
- **Your Responsibilities**: Connect to the simulator, validate models, execute simulation steps, and read/write data.

Learn more about [how connectors work](https://docs.cognite.com/cdf/integration/guides/simulators/connectors/).

### Core Interfaces

You'll implement three main components:
1.  **ISimulatorClient**: Manages the simulator connection, reports versions, extracts model info, and executes simulations.
2.  **RoutineImplementationBase**: Sets/gets values and runs commands on the simulator.
3.  **Configuration**: Defines connection settings.

## Universal Integration Patterns

Integration patterns are consistent across all connection types.

### Three-Method Routine

All connectors implement three methods: `SetInput()`, `GetOutput()`, and `RunCommand()`.

### Model Representation

All connectors parse models into a standard structure of nodes, edges, properties, and thermodynamic data.

## Platform Considerations

This tutorial uses Excel (COM), which requires Windows. The SDK supports cross-platform deployment for other integration types. Your project will need a Runtime Identifier (RID) to specify the target platform (e.g., `win-x64`, `linux-x64`).

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