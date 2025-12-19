using Cognite.Simulator.Utils;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;


public class NewSimRoutine : RoutineImplementationBase
{
    private readonly dynamic _workbook;
    private readonly ILogger _logger;
    private const int XlCalculationManual = -4135;

    public NewSimRoutine(dynamic workbook, SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger)
    {
        _workbook = workbook;
        _logger = logger;
    }

    public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments, CancellationToken _token)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(arguments);

        var sheetName = arguments["sheet"];
        var cellReference = arguments["cell"];
        dynamic worksheet = _workbook.Worksheets(sheetName);
        dynamic cell = worksheet.Range(cellReference);

        if (input.ValueType == SimulatorValueType.DOUBLE)
        {
            var rawValue = (input.Value as SimulatorValue.Double)?.Value ?? 0;
            cell.Value = rawValue;
            _logger.LogDebug($"Set {sheetName}!{cellReference} = {rawValue}");
        }
        else if (input.ValueType == SimulatorValueType.STRING)
        {
            var rawValue = (input.Value as SimulatorValue.String)?.Value;
            cell.Formula = rawValue;
            _logger.LogDebug($"Set {sheetName}!{cellReference} = '{rawValue}'");
        }
        else
        {
            throw new NotImplementedException($"{input.ValueType} not supported");
        }

        // Store reference for later use
        var simulatorObjectRef = new Dictionary<string, string> { { "sheet", sheetName }, { "cell", cellReference } };
        input.SimulatorObjectReference = simulatorObjectRef;
    }

    public override SimulatorValueItem GetOutput(
    SimulatorRoutineRevisionOutput outputConfig,
    Dictionary<string, string> arguments,
    CancellationToken _token)
    {
        ArgumentNullException.ThrowIfNull(outputConfig);
        ArgumentNullException.ThrowIfNull(arguments);

        var sheetName = arguments["sheet"];
        var cellReference = arguments["cell"];

        dynamic worksheet = _workbook.Worksheets(sheetName);
        dynamic cell = worksheet.Range(cellReference);

        SimulatorValue value;

        if (outputConfig.ValueType == SimulatorValueType.DOUBLE)
        {
            var rawValue = cell.Value;

            if (rawValue == null)
            {
                _logger.LogWarning($"Cell {sheetName}!{cellReference} is empty, using default");
                rawValue = 0.0;
            }
            var doubleValue = Convert.ToDouble(rawValue);
            value = new SimulatorValue.Double(doubleValue);
            _logger.LogDebug($"Read {sheetName}!{cellReference} = {doubleValue}");
        }
        else if (outputConfig.ValueType == SimulatorValueType.STRING)
        {
            var stringValue = (string)cell.Text;
            value = new SimulatorValue.String(stringValue);
            _logger.LogDebug($"Read {sheetName}!{cellReference} = '{stringValue}'");
        }
        else
        {
            throw new NotImplementedException($"{outputConfig.ValueType} not supported");
        }

        var simulatorObjectRef = new Dictionary<string, string> { { "sheet", sheetName }, { "cell", cellReference } };

        // Return the output item
        return new SimulatorValueItem
        {
            ValueType = outputConfig.ValueType,
            Value = value,
            ReferenceId = outputConfig.ReferenceId,
            SimulatorObjectReference = simulatorObjectRef,
            TimeseriesExternalId = outputConfig.SaveTimeseriesExternalId,
        };
    }

    public override void RunCommand(Dictionary<string, string> arguments, CancellationToken _token)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var command = arguments["command"];

        switch (command)
        {
            case "Pause":
                {
                    _workbook.Application.Calculation = XlCalculationManual;
                    _logger.LogInformation("Calculation mode set to manual");
                    break;
                }
            case "Calculate":
                {
                    _workbook.Application.Calculate();
                    _logger.LogInformation("Calculation completed");
                    break;
                }
            default:
                {
                    throw new NotImplementedException($"Unsupported command: '{command}'");
                }
        }
    }
}
