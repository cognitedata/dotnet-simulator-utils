# COM Connection Deep Dive

This guide explores COM automation in detail, showing you how to work effectively with Excel and other COM-based simulators using the SDK's `AutomationClient` helper class.

## Prerequisites

You should have completed:
- [Prerequisites & Setup](prerequisites.md) - Development environment ready
- [Create Your First Connector](create-connector.md) - Basic connector working

**What you'll learn:**
- How COM automation works with late binding
- Using the `AutomationClient` base class
- Thread safety and locking for COM
- Discovery techniques for COM APIs

## Understanding COM Automation

**COM (Component Object Model)** is a Microsoft technology that allows applications to expose their functionality to other programs.

### Why COM for Simulators?

Many industrial simulators use COM because:
- **Established standard** - Been around since the 1990s
- **Language-independent** - Works from VBA, Python, C#, etc.
- **No network required** - In-process or local-only communication
- **Mature tooling** - Well-supported by Windows

### Simulators Using COM

Common COM-based simulators include:
- **Microsoft Excel** - Office automation
- **KBC Petro-SIM** - Process simulation
- **AspenTech HYSYS** - Process simulation
- **Honeywell UniSim Design** - Process simulation
- **SLB Symmetry** - Process simulation
- **DWSIM** - Open-source process simulator

For a production example, see the [DWSIM Connector](https://github.com/cognitedata/dwsim-connector-dotnet) which uses these same patterns.

## The AutomationClient Base Class

The SDK provides `AutomationClient` as a base class that handles common COM automation tasks.

### What AutomationClient Provides

```csharp
public abstract class AutomationClient
{
    // The COM server instance (dynamic)
    protected dynamic Server { get; }

    // Configuration (Program ID, timeouts, etc.)
    protected AutomationConfig Config { get; }

    // Initialize COM connection
    protected void Initialize();

    // Shutdown COM connection
    protected void Shutdown();

    // Hook for cleanup before shutdown
    protected virtual void PreShutdown();

    // Locking mechanism for thread safety
    protected object Lock { get; }
}
```

### Basic Usage Pattern

```csharp
public class NewSimClient : AutomationClient, ISimulatorClient<...>
{
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    public NewSimClient(
        ILogger<NewSimClient> logger,
        DefaultConfig<NewSimAutomationConfig> config)
        : base(logger, config.Automation)
    {
        // Acquire semaphore for thread safety
        semaphore.Wait();
        try
        {
            // Connect to COM server
            Initialize();

            // Use Server to access COM object
            var version = Server.Version;

            // Disconnect
            Shutdown();
        }
        finally
        {
            semaphore.Release();
        }
    }
}
```

## Configuration: AutomationConfig

Create a configuration class that specifies how to connect to the COM server.

### Basic Configuration

```csharp
using Cognite.Simulator.Utils.Automation;

public class NewSimAutomationConfig : AutomationConfig
{
    public NewSimAutomationConfig()
    {
        ProgramId = "Excel.Application";
    }
}
```

### Advanced Configuration Options

```csharp
public class SimulatorAutomationConfig : AutomationConfig
{
    public SimulatorAutomationConfig()
    {
        // COM Program ID
        ProgramId = "YourSimulator.Application";

        // Connection timeout (milliseconds)
        ConnectionTimeout = 60000;  // 60 seconds

        // Retry attempts for COM calls
        RetryAttempts = 3;

        // Delay between retries (milliseconds)
        RetryDelay = 1000;  // 1 second
    }
}
```

## Late Binding with Dynamic

The `Server` property in `AutomationClient` is `dynamic`, meaning method and property names are resolved at runtime.

### Accessing Properties and Methods

```csharp
// Initialize connection
Initialize();

// Server is 'dynamic' - no compile-time checking
dynamic workbooks = Server.Workbooks;
int count = workbooks.Count;

// Open a workbook
dynamic workbook = workbooks.Open(@"C:\model.xlsx");

// Access worksheet
dynamic worksheet = workbook.ActiveSheet;

// Access cells (1-based indexing)
dynamic cell = worksheet.Cells[1, 1];
cell.Value = 42.5;

// Read value back
double value = (double)cell.Value;

// Close workbook
workbook.Close(false);  // false = don't save changes

// Shutdown connection
Shutdown();
```

### Type Conversions

When reading from `dynamic`, explicit casting is required:

```csharp
// Reading values
double doubleValue = (double)cell.Value;
string stringValue = (string)cell.Text;
int intValue = (int)cell.Value;

// Be careful with null/missing values
object rawValue = cell.Value;
if (rawValue != null)
{
    double value = Convert.ToDouble(rawValue);
}
```

### Method Parameters

COM methods often have many optional parameters. In C#, you can use named arguments:

```csharp
// Excel Workbooks.Open has many optional parameters
dynamic workbook = workbooks.Open(
    Filename: @"C:\model.xlsx",
    ReadOnly: false,
    Password: Type.Missing,  // Optional parameter omitted
    WriteResPassword: Type.Missing
);

// Or just pass required parameters
dynamic workbook = workbooks.Open(@"C:\model.xlsx");
```

## Thread Safety and Locking

**Critical:** COM objects are **not thread-safe**. You must ensure only one thread accesses the COM server at a time.

### Using Semaphores

```csharp
public class ExcelClient : AutomationClient, ISimulatorClient<...>
{
    // One permit = only one thread at a time
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public async Task SomeOperation(CancellationToken token)
    {
        // Acquire lock
        await _semaphore.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Initialize();

            // Safe to use Server here
            var result = Server.SomeMethod();

            Shutdown();
        }
        finally
        {
            // Always release, even if exception thrown
            _semaphore.Release();
        }
    }
}
```

**Don't use `lock` statement:** The `lock` keyword doesn't support async and can cause deadlocks with COM.

## Discovery: Learning the COM API

Since late binding lacks IntelliSense, use **Vendor Documentation** (manuals, SDKs, sample scripts) as your primary source for methods and properties. Alternatively, if the library is registered, use **Visual Studio's Object Browser** (Ctrl+Alt+J) to inspect available types. These tools bridge the gap when dynamic exploration isn't possible.

## Lifecycle Management

```csharp
// Initialize
public NewSimClient(ILogger<NewSimClient> logger, DefaultConfig<NewSimAutomationConfig> config)
    : base(logger, config.Automation)
{
    semaphore.Wait();
    try
    {
        Initialize();
        _version = Server.Version;
        Shutdown();
    }
    finally
    {
        semaphore.Release();
    }
}

// Operation
public async Task SomeOperation(CancellationToken token)
{
    await semaphore.WaitAsync(token).ConfigureAwait(false);
    try
    {
        Initialize();
        dynamic result = Server.DoSomething();
        Shutdown();
        return result;
    }
    finally
    {
        semaphore.Release();
    }
}

// Cleanup
protected override void PreShutdown()
{
    try
    {
        Server.Quit();
        _logger.LogInformation("COM server quit successfully");
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Error quitting COM server");
    }
}

**Next:** Continue to [Implement Routines](implement-routine.md) to learn how to execute simulations.