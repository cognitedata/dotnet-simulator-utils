using Microsoft.Extensions.Logging;

using CogniteSdk.Alpha;

using Cognite.Simulator.Utils;


public class NewSimRoutine : RoutineImplementationBase
{
    private readonly dynamic _workbook;

    public NewSimRoutine(dynamic workbook, SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger)
    {
        _workbook = workbook;
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
        }
        else if (input.ValueType == SimulatorValueType.STRING)
        {
            var rawValue = (input.Value as SimulatorValue.String)?.Value;
            cell.Formula = rawValue;
        }
        else if (input.ValueType == SimulatorValueType.DOUBLE_ARRAY)
        {
            var rawValues = (input.Value as SimulatorValue.DoubleArray)?.Value?.ToArray();
            if (rawValues == null || rawValues.Length == 0)
            {
                throw new ArgumentException("Double array value cannot be null or empty");
            }

            dynamic range = worksheet.Range(cellReference);
            int count = range.Cells.Count;

            if (count != rawValues.Length)
            {
                throw new ArgumentException($"Expected {count} values but got {rawValues.Length}");
            }

            for (int i = 1; i <= count; i++)
            {
                range.Cells(1, i).Value = rawValues[i - 1];
            }
        }
        else if (input.ValueType == SimulatorValueType.STRING_ARRAY)
        {
            var rawValues = (input.Value as SimulatorValue.StringArray)?.Value?.ToArray();
            if (rawValues == null || rawValues.Length == 0)
            {
                throw new ArgumentException("String array value cannot be null or empty");
            }

            dynamic range = worksheet.Range(cellReference);
            int count = range.Cells.Count;

            if (count != rawValues.Length)
            {
                throw new ArgumentException($"Expected {count} values but got {rawValues.Length}");
            }

            for (int i = 1; i <= count; i++)
            {
                range.Cells(1, i).Formula = rawValues[i - 1];
            }
        }
        else
        {
            throw new NotImplementedException($"{input.ValueType} not implemented");
        }

        var simulationObjectRef = new Dictionary<string, string> {
            { "sheet", sheetName },
            { "cell", cellReference }
        };
        input.SimulatorObjectReference = simulationObjectRef;
    }

    public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments, CancellationToken _token)
    {
        ArgumentNullException.ThrowIfNull(outputConfig);
        ArgumentNullException.ThrowIfNull(arguments);

        var sheetName = arguments["sheet"];
        var cellReference = arguments["cell"];

        dynamic worksheet = _workbook.Worksheets(sheetName);
        dynamic cell = worksheet.Range(cellReference);

        // Wait for calculation to complete with timeout
        var timeout = TimeSpan.FromSeconds(120);
        var startTime = DateTime.Now;
        var lastValue = cell.Value;
        var stableCount = 0;
        var valueReady = false;

        while (!valueReady && DateTime.Now - startTime <= timeout)
        {
            if (_token.IsCancellationRequested)
            {
                throw new OperationCanceledException("Operation was cancelled");
            }

            try
            {
                // Try to read the value regardless of calculation state
                var currentValue = cell.Value;

                // Check if we have a valid value
                if (currentValue != null)
                {
                    // If calculation is complete, we're done
                    if (_workbook.Application.CalculationState == -4105)
                    {
                        valueReady = true;
                        break;
                    }

                    // If value has stabilized, we can proceed
                    if (currentValue == lastValue)
                    {
                        stableCount++;
                        if (stableCount >= 3) // Value has remained stable for 3 consecutive checks
                        {
                            valueReady = true;
                            break;
                        }
                    }
                    else
                    {
                        stableCount = 0;
                        lastValue = currentValue;
                    }
                }
            }
            catch (Exception)
            {
                // If we can't read the value yet, just continue waiting
                stableCount = 0;
            }

            Thread.Sleep(100);
        }

        if (!valueReady)
        {
            throw new TimeoutException("Excel calculation timed out or value could not be read after 2 minutes");
        }

        SimulatorValue value;
        if (outputConfig.ValueType == SimulatorValueType.DOUBLE)
        {
            var rawValue = (double)cell.Value;
            value = new SimulatorValue.Double(rawValue);
        }
        else if (outputConfig.ValueType == SimulatorValueType.STRING)
        {
            var rawValue = (string)cell.Text;
            value = new SimulatorValue.String(rawValue);
        }
        else if (outputConfig.ValueType == SimulatorValueType.DOUBLE_ARRAY)
        {
            // Handle 1D array of doubles
            dynamic range = worksheet.Range(cellReference);
            int count = range.Cells.Count;
            double[] rawValues = new double[count];

            for (int i = 1; i <= count; i++)
            {
                rawValues[i - 1] = (double)range.Cells(1, i).Value;
            }

            value = new SimulatorValue.DoubleArray(rawValues);
        }
        else if (outputConfig.ValueType == SimulatorValueType.STRING_ARRAY)
        {
            // Handle 1D array of strings
            dynamic range = worksheet.Range(cellReference);
            int count = range.Cells.Count;
            string[] rawValues = new string[count];

            for (int i = 1; i <= count; i++)
            {
                rawValues[i - 1] = (string)range.Cells(1, i).Text;
            }

            value = new SimulatorValue.StringArray(rawValues);
        }
        else
        {
            throw new NotImplementedException($"{outputConfig.ValueType} value type not implemented");
        }

        var simulationObjectRef = new Dictionary<string, string> {
            { "sheet", sheetName },
            { "cell", cellReference }
        };

        return new SimulatorValueItem
        {
            ValueType = outputConfig.ValueType,
            Value = value,
            ReferenceId = outputConfig.ReferenceId,
            SimulatorObjectReference = simulationObjectRef,
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
                    _workbook.Application.Calculation = -4135; // xlCalculationManual = -4135
                    break;
                }
            case "Calculate":
                {
                    _workbook.Application.Calculate();
                    break;
                }
            default:
                {
                    throw new NotImplementedException($"Unsupported command: '{command}'");
                }
        }
    }
}
