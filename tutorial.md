
## Tutorial: Creating Your Own Simulator Connector

### Project Setup
Create a new C# project and add references to the necessary packages, including Cognite.Simulator.Utils [4].

### Create the Main Connector Class
Create a static class that will serve as the entry point for your connector:

```csharp
public static class YourConnector
{
    // We'll add methods here later
}
```

### Create a simulator definition

Use the following as a template:

```csharp
using CogniteSdk.Alpha;
using SimulatorCommon;

namespace GapConnector{
    static class SimulatorDefinition {
        public static SimulatorCreate Get() {
            return new SimulatorCreate()
                {
                    ExternalId = "GAP",
                    Name = "GAP",
                    FileExtensionTypes = new List<string> { "gar" },
                    ModelTypes = new List<SimulatorModelType> {
                        new SimulatorModelType {
                            Name = "Oil and Water Well",
                            Key = "OilWell",
                        }
                    },
                    StepFields = new List<SimulatorStepField> {
                        new SimulatorStepField {
                            StepType = "get/set",
                            Fields = new List<SimulatorStepFieldParam> {
                                new SimulatorStepFieldParam {
                                    Name = "address",
                                    Label = "Address",
                                    Info = "Enter the address to set",
                                },
                            },
                        },
                        new SimulatorStepField {
                            StepType = "command",
                            Fields = new List<SimulatorStepFieldParam> {
                                new SimulatorStepFieldParam {
                                    Name = "command",
                                    Label = "A Command",
                                    Info = "Enter the command to send to the simulator",
                                },
                            },
                        },
                    },
                    UnitQuantities = new List<SimulatorUnitQuantity> {
                        new SimulatorUnitQuantity {
                            Label= "Pressure",
                            Name = "Pressure",
                            Units = {
                                new SimulatorUnitEntry {}
                            }
                        }
                    }
                };
        }
    }
}
```
### Implement ISimulatorClient
Create a class that implements ISimulatorClient:

```csharp
public class YourSimulatorClient : ISimulatorClient<YourModelFilestate, SimulatorRoutineRevision>
{
    public async Task<SimulationResult> RunSimulation(YourModelFilestate modelFilestate, SimulatorRoutineRevision routine)
    {
        // Implement your simulation logic here
    }
}
```
Replace "YourSimulatorClient" and "YourModelFilestate" with appropriate names for your simulator.

### Create Custom Automation Config
If needed, create a custom automation config class:

```csharp


public class CustomAutomationConfig : AutomationConfig 
{
    // Add custom properties if needed
}
```

### Configure Services
In your main connector class, implement the ConfigureServices method:


```csharp
static void ConfigureServices(IServiceCollection services)
{
    services.AddScoped<ISimulatorClient<YourModelFilestate, SimulatorRoutineRevision>, YourSimulatorClient>();
}
```

This step is crucial for dependency injection [4].

### Set Up Connector Runtime
In the Run method of your main connector class, configure the DefaultConnectorRuntime:

```csharp
public static async Task Run()
{
    DefaultConnectorRuntime<CustomAutomationConfig, YourModelFilestate, YourFileStatePoco>.SimulatorDefinition = SimulatorDefinition.Get();
    DefaultConnectorRuntime<CustomAutomationConfig, YourModelFilestate, YourFileStatePoco>.ConfigureServices = ConfigureServices;
    DefaultConnectorRuntime<CustomAutomationConfig, YourModelFilestate, YourFileStatePoco>.ConnectorName = "YourConnectorName";

    await DefaultConnectorRuntime<CustomAutomationConfig, YourModelFilestate, YourFileStatePoco>.RunStandalone().ConfigureAwait(false);
}
```
Replace the generic types and connector name with your own [2][3].

### Implement RoutineImplementationBase
Create a class that inherits from RoutineImplementationBase:

```csharp
public class YourRoutineImplementation : RoutineImplementationBase
{
    private readonly YourSimulatorClient _client;

    public YourRoutineImplementation(YourSimulatorClient client)
    {
        _client = client;
    }

    public override async Task<Dictionary<string, object>> Run(Dictionary<string, object> inputs)
    {
        // Implement your routine logic here
    }

    public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments)
    {
        Console.WriteLine("Handling inputs");
        // Implement input handling
    }

    public override SimulatorValueItem CreateOutputItem(SimulatorRoutineRevisionOutput outputConfig)
    {
        // Implement output creation
        return new SimulatorValueItem
        {
            // Set properties as needed
        };
    }
}
```
This class will use the ISimulatorClient to run simulations and process inputs/outputs.

### Error Handling and Logging
Implement proper error handling and logging throughout your connector. Use the ILogger interface for consistent logging.

A logger is available in the ISimulatorClient and can be accessed as follows:
```csharp
_logger.LogDebug("Your message here");
```

### Testing
Create unit tests for your connector to ensure it works as expected. 


By following these steps, you should be able to create a custom connector for your simulator that integrates with the Cognite simulator integration framework. Remember to adjust the code examples to fit your specific simulator's requirements and to handle any simulator-specific operations in the RunSimulation method of your ISimulatorClient implementation.