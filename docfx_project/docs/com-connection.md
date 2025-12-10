# COM Connection Deep Dive

This guide explores COM automation, showing how to work with Excel and other COM-based simulators using the SDK's `AutomationClient` helper class.

## Prerequisites

You should have completed:
- [Prerequisites & Setup](prerequisites.md) - Development environment ready
- [Create Your First Connector](create-connector.md) - Basic connector working

## Understanding COM Automation

**COM (Component Object Model)** is a Microsoft technology for inter-process communication. Many industrial simulators use it because it is a language-independent, established standard. Common examples include KBC Petro-SIM, Aspen HYSYS, and DWSIM. For a production example, see the [DWSIM Connector](https://github.com/cognitedata/dwsim-connector-dotnet).

## The AutomationClient Base Class

The SDK provides `AutomationClient` as a base class for common COM automation tasks.

### What AutomationClient Provides

```csharp
public abstract class AutomationClient
{
    // The COM server instance (dynamic)
    protected dynamic Server { get; }

    // Configuration (Program ID, timeouts, etc.)
    protected AutomationConfig Config { get; }

    // Initialize/Shutdown COM connection
    protected void Initialize();
    protected void Shutdown();

    // Hook for cleanup before shutdown
    protected virtual void PreShutdown();

    // Locking mechanism for thread safety
    protected object Lock { get; }
}
```

## Configuration: AutomationConfig

Create a configuration class to specify the COM server connection details.

### Basic Configuration

> The `ProgramId` should be available in the vendor documentation for the simulator. Alternatively, you can find the `ProgramId` for your COM application by looking in the Windows Registry under `HKEY_CLASSES_ROOT`. Look for the name of your application, and the `ProgID` will be listed there. For example, the `ProgID` for Excel is `Excel.Application`.

```csharp
using Cognite.Simulator.Utils.Automation;

public class NewSimAutomationConfig : AutomationConfig
{
    // any extra config options could be defined here
    public NewSimAutomationConfig()
    {
        ProgramId = "Excel.Application";
    }
}
```

## Late Binding with Dynamic

The `Server` property in `AutomationClient` is `dynamic`, so method and property names are resolved at runtime.

### Accessing Properties and Methods

```csharp
// Initialize connection
Initialize();

// Server is 'dynamic' - no compile-time checking
dynamic workbooks = Server.Workbooks;
int count = workbooks.Count;

// Close workbook
workbook.Close(false);  // false = don't save changes

// Shutdown connection
Shutdown();
```

### Type Conversions

Explicit casting is required when reading from `dynamic` objects.

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

## Thread Safety and Locking

**Critical:** COM objects are not thread-safe. You must ensure only one thread accesses the COM server at a time.

### Using Semaphores

```csharp

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

```

**Don't use `lock` statement:** The `lock` keyword doesn't support async and can cause deadlocks with COM.

## Discovery: Learning the COM API

Since late binding lacks IntelliSense, use vendor documentation or Visual Studio's Object Browser (Ctrl+Alt+J) to discover methods and properties.

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
```

**Next:** Continue to [Implement Routines](implement-routine.md) to learn how to execute simulations.