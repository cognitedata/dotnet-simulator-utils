# Implement model parsing

To be able to run simulations using the Cognite simulator integration, we need to be able to open the model file and extract the necessary information from it first.
In this example, we will use Excel a simple "simulator".

### Create a NewSimAutomationConfig class that inherits from AutomationConfig:

This is needed to set the COM program ID, so the connector knows which program to connect to.
For the sake of this example, we will use Excel.

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

Adjust the ConnectorRuntime class to use the NewSimAutomationConfig class.
Replace every instance of `AutomationConfig` with `NewSimAutomationConfig`.

```csharp
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

    public static async Task Run(ILogger defaultLogger, CancellationToken token) {
        Init();
        await DefaultConnectorRuntime<NewSimAutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.Run(defaultLogger, token).ConfigureAwait(false);
    }
}
```

### Extend the NewSimClient class to inherit from AutomationClient

This is needed to access the COM object.
We need to add the `using Cognite.Simulator.Utils.Automation;` namespace to the NewSimClient class.
Also, extend the class to inherit from `AutomationClient` and implement a new constructor that calls the base constructor with the AutomationConfig type.
Simply replace these top lines in the NewSimClient class:

```csharp
using Cognite.Simulator.Utils;
using CogniteSdk.Alpha;

public class NewSimClient : ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
// rest of the class
```

With these lines:

```csharp
using Microsoft.Extensions.Logging;

using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;

public class NewSimClient : AutomationClient, ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
    private readonly object simulatorLock = new object();

    public NewSimClient(ILogger<NewSimClient> logger, DefaultConfig<NewSimAutomationConfig> config)
            : base(logger, config.Automation)
    {
    }
    // rest of the class
```

### Implement COM call to open the Excel file:

Simply copy the following method to the NewSimClient class.

```csharp
public dynamic OpenBook(string path)
{
    dynamic workbooks = Server.Workbooks;
    return workbooks.Open(path);
}
```

### Implement the ExtractModelInformation method in the NewSimClient class:

This method is used to extract the model information from the simulator.
For the sake of this example we will only open the Excel .xlsx file. If 

```csharp
private readonly object simulatorLock = new object();

public Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken _token)
    {
        lock (simulatorLock)
        {
            try
            {
                Initialize();
                dynamic workbook = OpenBook(state.FilePath);
                if (workbook != null)
                {
                    state.ParsingInfo.SetSuccess();
                }
                else
                {
                    state.ParsingInfo.SetFailure();
                }
            }
            finally
            {
                Shutdown();
            }
        }
        return Task.CompletedTask;
    }
```

Updated NewSimClient class:

```csharp
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

public class NewSimClient : AutomationClient, ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
    private readonly object simulatorLock = new object();

    public NewSimClient(ILogger<NewSimClient> logger, DefaultConfig<NewSimAutomationConfig> config)
            : base(logger, config.Automation)
    {
    }

    public dynamic OpenBook(string path)
    {
        dynamic workbooks = Server.Workbooks;
        return workbooks.Open(path);
    }

    public Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken _token)
    {
        lock (simulatorLock)
        {
            try
            {
                Initialize();
                dynamic workbook = OpenBook(state.FilePath);
                if (workbook != null)
                {
                    state.ParsingInfo.SetSuccess();
                }
                else
                {
                    state.ParsingInfo.SetFailure();
                }
            }
            finally
            {
                Shutdown();
            }
        }
        return Task.CompletedTask;
    }

    public string GetConnectorVersion()
    {
        return "N/A";
    }

    public string GetSimulatorVersion()
    {
        return "N/A";
    }

    public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(DefaultModelFilestate modelState, SimulatorRoutineRevision simulationConfiguration, Dictionary<string, SimulatorValueItem> inputData)
    {
        var routine = new NewSimRoutine(simulationConfiguration, inputData);
        return Task.FromResult(routine.PerformSimulation());
    }

    protected override void PreShutdown()
    {
    }
}
```

Now, using the Fusion interface we can upload an empty Excel file and observe its status.

Upload the file:
![Model upload](../images/model-upload.png)

Model has been successfully parsed:
![Model parsing](../images/model-parsing.png)
