using Cognite.Simulator.Utils;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

public class NewSimRoutine : RoutineImplementationBase
{
    private readonly dynamic _workbook;

    public NewSimRoutine(dynamic workbook, SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger)
    {
        _workbook = workbook;
    }

    public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments)
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
        }
        else if (input.ValueType == SimulatorValueType.STRING)
        {
            var rawValue = (input.Value as SimulatorValue.String)?.Value;
            worksheet.Cells[row, col].Formula = rawValue;
        }
        else
        {
            throw new NotImplementedException($"{input.ValueType} not implemented");
        }

        var simulationObjectRef = new Dictionary<string, string> { { "row", rowStr }, { "col", colStr } };
        input.SimulatorObjectReference = simulationObjectRef;
    }

    public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments)
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

        var rawValue = (double)cell.Value;
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

    public override void RunCommand(Dictionary<string, string> arguments)
    {
        // No implementation needed for this simulator
    }
}
