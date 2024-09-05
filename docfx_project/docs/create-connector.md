# Create your first simulator connector

## Prerequisites

  - .NET LTS version
  - You require a Cognite Data Fusion (CDF) project.

## Create a new simulator connector project

Download the Cognite Simulator Utils from [NuGet](https://www.nuget.org/packages/Cognite.Simulator.Utils/).

To create a console application and add the latest version of the library, open the terminal and run the commands below:

```sh
dotnet new console -o NewSimConnector
cd NewSimConnector
dotnet add package Cognite.Simulator.Utils --prerelease
```
> Note: The `--prerelease` flag is required to install the latest version of the library (alpha).

### Create a configuration file

To create a `config.yml` file containing the simulator configuration, use the YAML code below:

```yaml
version: 1

logger:
    console:
        level: "debug"

cognite:
    project: ${COGNITE_PROJECT}
    # This is for Microsoft Entra as an IdP. To use a different provider:
    # set implementation: Basic, and use token-url instead of tenant.
    # See the example config for the full list of options.
    idp-authentication:
        # Directory tenant
        tenant: ${COGNITE_TENANT_ID}
        # Application ID
        client-id: ${COGNITE_CLIENT_ID}
        # Client secret
        secret: ${COGNITE_CLIENT_SECRET}
        # List of resource scopes. Example:
        # scopes:
        #   - https://api.cognitedata.com/.default
        scopes:
          - ${COGNITE_SCOPE}


simulator:
  name: "NewSim"
  # Data set ID to keep all the simulator resources
  data-set-id: ${COGNITE_DATA_SET_ID}
    
connector:
  name-prefix: "new-sim-connector@"
```

This file contains the configuration required to connect to the CDF project, define the target data set ID, and set the connector name.

:::info tip
Make sure you populate the environment variables with the correct values. You can also hardcode the values in the configuration file for the development environment.
:::

### Create a simulator definition

Now, define a contract between the simulator and CDF. This contract is defined in the API as a `Simulator` object.

Create a new file called `SimulatorDefinition.cs` and copy the code into it. You can adjust the values later.

```csharp
using CogniteSdk.Alpha;

static class SimulatorDefinition {
    public static SimulatorCreate Get() {
        return new SimulatorCreate()
            {
                ExternalId = "NewSim",
                Name = "NewSim",
                FileExtensionTypes = new List<string> { "xlsx" },
                ModelTypes = new List<SimulatorModelType> {
                    new SimulatorModelType {
                        Name = "Steady State",
                        Key = "SteadyState",
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
                                Label = "Command",
                                Info = "Enter the command to run",
                            },
                        },
                    },
                },
                UnitQuantities = new List<SimulatorUnitQuantity>() {
                    new SimulatorUnitQuantity {
                        Name = "Temperature",
                        Label = "Temperature",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Name = "C",
                                Label = "Celsius",
                            },
                        },
                    },
                },
            };
    }
}
```

`UnitQuantities`, `ModelTypes`, and `StepFields` define the simulator units, models, and fields that the simulator can handle.

`StepFields` defines how to access simulator object fields, send values into the simulator, and read the simulation results.

Steps can be of type `get`, `set`, or `command`.

`UnitQuantities` defines the measurement units that the simulator can handle.

`ModelTypes` defines the different types of models that the simulator can handle.

Now, we've used placeholders for the fields. Fill in the actual values when you use them.

### Implement a simulator client

Create a class that implements `ISimulatorClient`.

`NewSimClient.cs`:
```csharp
using Cognite.Simulator.Utils;
using CogniteSdk.Alpha;

public class NewSimClient : ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
    public Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken _token)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }
}
```
<!--We will implement the methods in the `NewSimClient` class later.-->

#### Create a ConnectorRuntime class

Configure the services via `Dependency Injection` and boilerplate code to run the connector.

Create a class using `DefaultConnectorRuntime` helper class.

`ConnectorRuntime.cs`:
```csharp
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;

public static class ConnectorRuntime {

    public static void Init() {
        DefaultConnectorRuntime<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConfigureServices = ConfigureServices;
        DefaultConnectorRuntime<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.ConnectorName = "NewSim";
        DefaultConnectorRuntime<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.SimulatorDefinition = SimulatorDefinition.Get();
    }

    static void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>, NewSimClient>();
    }
    
    public static async Task RunStandalone() {
        Init();
        await DefaultConnectorRuntime<AutomationConfig, DefaultModelFilestate, DefaultModelFileStatePoco>.RunStandalone().ConfigureAwait(false);
    }
}
```

#### Program entry point

The final step is to run the connector in the `Main` method of the console application.
Replace the contents of the `Program.cs` file with the code below:

```csharp
public class Program
{
    public static int Main(string[] args)
    {
        RunStandalone();
        return 0;
    }

    private static void RunStandalone()
    {
        ConnectorRuntime.RunStandalone().Wait();
    }
}
```

Once you run the application, you'll see the connector in CDF.
<!--Change this line The connector can't do much yet, but it reports its "heartbeat" to the Cognite Data Fusion platform.-->

![Heartbeat in Fusion](../images/screenshot-heartbeat.png)