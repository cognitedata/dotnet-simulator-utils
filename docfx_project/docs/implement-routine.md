# Implement RoutineImplementationBase

A routine is an entity that contains the input and output parameter configuration required for simulation.
It also contains a list of instructions for the connector to pass into the simulation model.

In this tutorial, you'll use the COM interface to connect the connector to a simulator.

Create a class that inherits from `RoutineImplementationBase`.

`NewSimRoutine.cs`:
```csharp
using Cognite.Simulator.Utils;
using CogniteSdk.Alpha;

public class NewSimRoutine : RoutineImplementationBase
{
    private readonly dynamic _workbook;


    public NewSimRoutine(dynamic workbook, SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger)
    {
        _workbook = workbook;
    }

    public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments, CancellationToken _token)
    {
        var rowStr = arguments["row"];
        var colStr = arguments["col"];
        var row = int.Parse(rowStr);
        var col = int.Parse(colStr);

        dynamic worksheet = _workbook.ActiveSheet;

        if (input.ValueType == SimulatorValueType.DOUBLE)
        {
            var rawValue = (input.Value as SimulatorValue.Double)?.Value ?? 0;
            worksheet.Cells[row, col].Value = rawValue;
        } else if (input.ValueType == SimulatorValueType.STRING)
        {
            var rawValue = (input.Value as SimulatorValue.String)?.Value;
            worksheet.Cells[row, col].Formula = rawValue;
        } else {
            throw new NotImplementedException($"{input.ValueType} not implemented");
        }

        var simulationObjectRef = new Dictionary<string, string> { { "row", rowStr }, { "col", colStr } };
        input.SimulatorObjectReference = simulationObjectRef;
    }

    public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments, CancellationToken _token)
    {
        var rowStr = arguments["row"];
        var colStr = arguments["col"];
        var row = int.Parse(rowStr);
        var col = int.Parse(colStr);

        dynamic worksheet = _workbook.ActiveSheet;
        var cell = worksheet.Cells[row, col];

        if (outputConfig.ValueType != SimulatorValueType.DOUBLE)
        {
            throw new NotImplementedException($"{outputConfig.ValueType} value type not implemented");
        }

        var rawValue = (double) cell.Value;
        SimulatorValue value = new SimulatorValue.Double(rawValue);

        var simulationObjectRef = new Dictionary<string, string> { { "row", rowStr }, { "col", colStr } };

        return new SimulatorValueItem
        {
            ValueType = SimulatorValueType.DOUBLE,
            Value = value,
            ReferenceId = outputConfig.ReferenceId,
            SimulatorObjectReference = simulationObjectRef,
            TimeseriesExternalId = outputConfig.SaveTimeseriesExternalId,
        };
    }

    public override void RunCommand(Dictionary<string, string> arguments, CancellationToken _token)
    {
        // No implementation is required for this simulator.
    }
}
```
The newly created class will perform the simulation.

- The `SetInput` method sets the input values for the simulation. 
- The `GetOutput` method gets the output values from the simulation. 
- The `RunCommand` method runs commands in the simulation. You don't need this method for the simulator because the results are calculated right away on the worksheet.

#### Implement `RunSimulation` method in `NewSimClient`

Call the `PerformSimulation` method in the `NewSimRoutine` class. The `PerformSimulation` method will run through the instructions in the routine revision and return the results of the simulation.
Use a semaphore to ensure only one connection to the simulator is made at a time.

```csharp
public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(DefaultModelFilestate modelState, SimulatorRoutineRevision routineRev, Dictionary<string, SimulatorValueItem> inputData, CancellationToken token)
    {
        await semaphore.WaitAsync().ConfigureAwait(false);
        dynamic? workbook = null;
        try
        {
            Initialize();
            workbook = OpenBook(modelState.FilePath);

            var routine = new NewSimRoutine(workbook, routineRev, inputData, logger);
            return routine.PerformSimulation(token);
        }
        finally
        {
            if (workbook != null)
            {
                workbook.Close(false);
            }
            Shutdown();
            semaphore.Release();
        }
    }
```

Now, call the API and create a new routine and a routine revision. When done, you're ready for the first simulation.

Routine:

```json
POST {{baseUrl}}/api/v1/projects/{{project}}/simulators/routines
{
  "items": [{
        "externalId": "simple-computations",
        "modelExternalId": "empty_book",
        "simulatorIntegrationExternalId": "new-test-connector@computer",
        "name": "Simple computations"
    }]
}
```

In the following example, create a routine revision for the routine that you've already created.

The script contains the instructions for the simulation. In this case, set the value of the cell `A1` to `10` and the value of the cell `B1` to the formula `=A1 * 2`, which should result in `20`.

Routine revision:

```json
POST {{baseUrl}}/api/v1/projects/{{project}}/simulators/routines/revisions

{
    "items": [{
        "externalId": "simple-computations-1",
        "routineExternalId": "simple-computations",
        "configuration": {
            "schedule": {
                "enabled": false
            },
            "dataSampling": {
                "enabled": false
            },
            "logicalCheck": [],
            "steadyStateDetection": [],
            "inputs": [
                {
                    "name": "Number",
                    "referenceId": "I1",
                    "value": 10.0,
                    "valueType": "DOUBLE"
                },
                {
                    "name": "Formula",
                    "referenceId": "F1",
                    "value": "=A1 * 2",
                    "valueType": "STRING"
                }
            ],
            "outputs": [
                {
                    "name": "Formula Result",
                    "referenceId": "FR1",
                    "valueType": "DOUBLE"
                }
            ]
        },
        "script": [
            {
                "order": 1,
                "description": "Set Inputs",
                "steps": [
                    {
                        "order": 1,
                        "stepType": "Set",
                        "arguments": {
                            "referenceId": "I1",
                            "row": "1",
                            "col": "1"
                        }
                    },
                    {
                        "order": 2,
                        "stepType": "Set",
                        "arguments": {
                            "referenceId": "F1",
                            "row": "1",
                            "col": "2"
                        }
                    }
                ]
            },
            {
                "order": 3,
                "description": "Set outputs",
                "steps": [
                    {
                        "order": 1,
                        "stepType": "Get",
                        "arguments": {
                            "referenceId": "FR1",
                            "row": "1",
                            "col": "2"
                        }
                    }
                ]
            }
        ]
    }]
}
```

Now, run the simulation and view the results. Select the routine and then select `Run now`.

![Running simulation](../images/running-simulation.png)

When the simulation is completed, you can view the details in the `Run browser` tab.

![Simulation details](../images/simulation-details.png)

Select `View data` to view the simulation results.

![Simulation results](../images/simulation-data.png)
