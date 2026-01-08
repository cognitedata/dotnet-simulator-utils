# MuJoCo Python Connector

A sample connector using pythonnet to integrate [MuJoCo](https://mujoco.org/) physics simulator with CDF. Python files are embedded in the executable at build time.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    .NET Connector Runtime                   │
├─────────────────────────────────────────────────────────────┤
│                    lib/ (C# Bridge Layer)                   │
├─────────────────────────────────────────────────────────────┤
│  EmbeddedPythonLoader   │  Extracts .py files, auto-detect  │
│  PythonBridgeBase       │  GIL handling, large-stack exec   │
│  PythonBridgeClient     │  ISimulatorClient implementation  │
│  PythonBridgeRoutine    │  IRoutineImplementation impl      │
├─────────────────────────────────────────────────────────────┤
│                  python/ (Embedded at build)                │
├─────────────────────────────────────────────────────────────┤
│  definition.py          │  Simulator definition (MuJoCo)    │
│  client.py              │  MuJoCo model loading/validation  │
│  routine.py             │  Simulation execution engine      │
└─────────────────────────────────────────────────────────────┘
```

## Prerequisites

- .NET 8.0 SDK
- Python 3.9+ with: `pip install mujoco find-libpython`

## Production Build

```bash
# macOS Apple Silicon
dotnet publish -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -o ./publish

# Linux x64
dotnet publish -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -o ./publish

# Windows x64
dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -o ./publish
```

Produces a single ~85MB executable with embedded Python files.
