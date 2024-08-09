# Creating your first simulator connector

#### Prerequisites
  - .NET LTS version
  - Cognite Data Fusion project 

#### Create a new simulator connector project

The Cognite Simulator Utils can be downloaded from [NuGet](https://www.nuget.org/packages/Cognite.Simulator.Utils/).

To create a console application and add the latest version of the library:

Using .NET CLI:
```sh
dotnet new console -o NewSimConnector
cd NewSimConnector
dotnet add package Cognite.Simulator.Utils --prerelease
```
NB: The `--prerelease` flag is required to install the latest version of the library (alpha).

#### Create a configuration file

Create a `config.yml` file containing the simulator configuration

```yaml
version: 1

logger:
    console:
        level: "debug"

cognite:
    project: ${COGNITE_PROJECT}
    # This is for Microsoft Entra as an IdP, to use a different provider,
    # set implementation: Basic, and use token-url instead of tenant.
    # See the example config for the full list of options.
    idp-authentication:
        # Directory tenant
        tenant: ${COGNITE_TENANT_ID}
        # Application Id
        client-id: ${COGNITE_CLIENT_ID}
        # Client secret
        secret: ${COGNITE_CLIENT_SECRET}
        # List of resource scopes, ex:
        # scopes:
        #   - https://api.cognitedata.com/.default
        scopes:
          - ${COGNITE_SCOPE}


simulator:
  name: "NewSim"
  # Data set id to keep all the simulator resources
  data-set-id: ${COGNITE_DATA_SET_ID}
    
connector:
  name-prefix: "new-sim-connector@"
```

This file contains the configuration needed to connect to the Cognite Data Fusion project and the simulator.

Make sure to populate the environment variables with the correct values. Alternatively, you can hardcode the values in the configuration file for the development environment.


### Create a simulator definition

Now we to have to define a contract between the simulator and the Cognite Data Fusion platform. This contract is defined in the API as a `Simulator` object.

Create a new file called `SimulatorDefinition.cs`.
The copy the following code into it and replace the values with your own:

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

`UnitQuantities`, `ModelTypes`, and `StepFields` are used to define the simulator units, models, and fields that the simulator can handle.

`StepFields` are used to define how simulator object fields can be accessed in order to both send values into the simulator and read results of a simulation.
Steps can be of type `get`, `set`, or `command`.

`UnitQuantities` are used to define the units of measurement that the simulator can handle.

`ModelTypes` can be used to define different types of models that the simulator can handle.

We may fill these fields with the actual values later, but for now, we can use placeholders.

### Implement a simulator client
Create a class that implements `ISimulatorClient`.

NewSimClient.cs:
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
We will implement the methods in the `NewSimClient` class later.


#### Create a ConnectorRuntime class
We need to configure the services via Dependency Injection and boilerplate code to run the connector.
Create a class using `DefaultConnectorRuntime` helper class.

ConnectorRuntime.cs:
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

The only thing left is to run the connector in the `Main` method of the console application.
Replace the contents of the `Program.cs` file with the following code:

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

Once you run the application, you should see the connector in the Fusion GUI.
The connector can't do much yet, but it reports its "heartbeat" to the Cognite Data Fusion platform.

![Heartbeat in Fusion](../images/screenshot-heartbeat.png)