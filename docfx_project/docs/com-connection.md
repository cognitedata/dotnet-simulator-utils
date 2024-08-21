# Implement COM connection

To be able to run simulations using the Cognite simulator integration, we need to be able to connect to a simulator using `COM` interface.
In this example, we will use Excel as a simple "simulator" and will try to read its version number.

### Create a class that inherits from AutomationConfig

This is needed to set the `COM` program ID, so the connector knows which program to connect to.
For the sake of this example, we will use Excel.

`NewSimAutomationConfig.cs`:
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

Adjust the `ConnectorRuntime` class to use the `NewSimAutomationConfig` class.
Replace every instance of `AutomationConfig` with `NewSimAutomationConfig`.

```csharp
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;

public static class ConnectorRuntime {

    public static void Init() {
        DefaultConnectorRuntime<NewSimAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConfigureServices = ConfigureServices;
        DefaultConnectorRuntime<NewSimAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConnectorName = "NewSim";
        DefaultConnectorRuntime<NewSimAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.SimulatorDefinition = SimulatorDefinition.Get();
    }
    static void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, NewSimClient>();
    }
    
    public static async Task RunStandalone() {
        Init();
        await DefaultConnectorRuntime<NewSimAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.RunStandalone().ConfigureAwait(false);
    }
}
```

### Extend the NewSimClient class to inherit from the AutomationClient class

This is needed to access the `COM` object.
We need to add the `using Cognite.Simulator.Utils.Automation;` namespace to the `NewSimClient` class.
Also, extend the class to inherit from `AutomationClient` and implement a new constructor that calls the base constructor with the AutomationConfig type.

Simply replace these top lines in the `NewSimClient` class:

```csharp
using Cognite.Simulator.Utils;
using CogniteSdk.Alpha;

public class NewSimClient : ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
// rest of the class
```

With these lines:

```csharp
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

public class NewSimClient : AutomationClient, ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
    private readonly string _version = "N/A";

    public NewSimClient(ILogger<NewSimClient> logger, DefaultConfig<NewSimAutomationConfig> config)
            : base(logger, config.Automation)
    {
        semaphore.Wait();
        try
        {
            Initialize();
            _version = Server.Version;
        }
        finally
        {
            Shutdown();
            semaphore.Release();
        }
    }
    // rest of the class
```
In the constructor, we initialize the `COM` connection and get the version number of the Excel application. We also added a field `_version` to store the version number. While communicating with the simulator, we use a semaphore to avoid multiple threads accessing the `COM` object at the same time.


We also need to add a method that closes the Excel application when `COM` connection is closed.
```csharp
protected override void PreShutdown()
{
    Server.Quit();
}
```

And update the `GetSimulatorVersion` method to return the version number.
```csharp
public string GetSimulatorVersion()
{
    return _version;
}
```



Try to run the connector, you should see the version number in Fusion.

![Simulator version](../images/simulator-version.png)