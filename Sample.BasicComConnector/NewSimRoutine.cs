using Microsoft.Extensions.Logging;
using CogniteSdk.Alpha;
using Cognite.Simulator.Utils;
using System.Text.Json;

public class NewSimRoutine : RoutineImplementationBase
{
    private readonly dynamic _workbook;
    private dynamic? _placeitComObject;
    private readonly Dictionary<string, object> _simulationParameters = new();
    private readonly ILogger _logger;

    public NewSimRoutine(dynamic workbook, SimulatorRoutineRevision routineRevision, Dictionary<string, SimulatorValueItem> inputData, ILogger logger) : base(routineRevision, inputData, logger)
    {
        _workbook = workbook;
        _logger = logger;
    }

    public override void SetInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments, CancellationToken _token)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(arguments);

        // Get the reference ID to identify the parameter
        var referenceId = inputConfig.ReferenceId ?? "";

        // Determine what kind of input this is based on reference ID
        // All PlaceiT simulation parameters are recognized by their reference ID
        if (IsPlaceiTParameter(referenceId))
        {
            SetPlaceiTSimulationParameter(inputConfig, input, arguments);
        }
        else
        {
            _logger.LogWarning($"Unexpected input configuration: referenceId={referenceId}");
        }

        var simulationObjectRef = new Dictionary<string, string> {
            { "referenceId", referenceId },
            { "parameterName", inputConfig.Name ?? "" }
        };
        input.SimulatorObjectReference = simulationObjectRef;
    }

    private bool IsPlaceiTParameter(string referenceId)
    {
        // List of all known PlaceiT parameter reference IDs
        var placeitParameters = new HashSet<string>
        {
            "porosity", "zonePressure", "zoneLength", "isothermOption", "isothermValues",
            "adsorptionCapOption", "adsorptionCapValue", "enabledStages", "stagesVol",
            "stagesConc", "injectionRate", "timestep", "productionTime", "productionRate", "medRange"
        };
        return placeitParameters.Contains(referenceId);
    }

    private void SetPlaceiTSimulationParameter(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments)
    {
        // Use reference ID as the parameter key
        var parameterName = inputConfig.ReferenceId ?? inputConfig.Name ?? "";
        
        object parameterValue = input.ValueType switch
        {
            SimulatorValueType.DOUBLE => (input.Value as SimulatorValue.Double)?.Value ?? 0.0,
            SimulatorValueType.STRING => (input.Value as SimulatorValue.String)?.Value ?? "",
            SimulatorValueType.DOUBLE_ARRAY => (input.Value as SimulatorValue.DoubleArray)?.Value?.ToArray() ?? Array.Empty<double>(),
            SimulatorValueType.STRING_ARRAY => (input.Value as SimulatorValue.StringArray)?.Value?.ToArray() ?? Array.Empty<string>(),
            _ => throw new NotImplementedException($"Value type '{input.ValueType}' not implemented for PlaceiT simulation")
        };

        _simulationParameters[parameterName] = parameterValue;
        _logger.LogDebug($"Set PlaceiT parameter '{parameterName}' = {JsonSerializer.Serialize(parameterValue)}");
    }

    private void HandleComControlInput(SimulatorRoutineRevisionInput inputConfig, SimulatorValueItem input, Dictionary<string, string> arguments)
    {
        var action = (input.Value as SimulatorValue.String)?.Value ?? "";
        
        switch (action)
        {
            case "Initialize":
                InitializePlaceiTComObject();
                break;
            case "Release":
                ReleasePlaceiTComObject();
                break;
            case "Reset":
                ResetSimulation();
                break;
            default:
                throw new NotImplementedException($"COM control action '{action}' not implemented");
        }
    }

    private void InitializePlaceiTComObject()
    {
        try
        {
            if (_placeitComObject == null)
            {
                // PlaceiT is implemented as VBA functions in the workbook
                // The PlaceiTCOMEntryPoint function can be called directly on the workbook
                _placeitComObject = _workbook;
                _logger.LogInformation("PlaceiT COM object initialized (using workbook)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize PlaceiT COM object");
            throw new InvalidOperationException("Could not initialize PlaceiT COM object", ex);
        }
    }

    private void ReleasePlaceiTComObject()
    {
        if (_placeitComObject != null)
        {
            try
            {
                // Release COM object references
                System.Runtime.InteropServices.Marshal.ReleaseComObject(_placeitComObject);
                _placeitComObject = null;
                _logger.LogInformation("PlaceiT COM object released");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing PlaceiT COM object");
            }
        }
    }

    private void ResetSimulation()
    {
        _simulationParameters.Clear();
        _logger.LogInformation("Simulation parameters reset");
    }

    public override SimulatorValueItem GetOutput(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments, CancellationToken _token)
    {
        ArgumentNullException.ThrowIfNull(outputConfig);
        ArgumentNullException.ThrowIfNull(arguments);

        // Get the reference ID to determine what output to retrieve
        var referenceId = outputConfig.ReferenceId ?? "";

        // Check if this is a PlaceiT simulation result request
        if (referenceId == "placeit-result")
        {
            return GetPlaceiTSimulationResult(outputConfig, arguments, _token);
        }
        else
        {
            throw new NotImplementedException($"Output reference ID '{referenceId}' not implemented");
        }
    }

    private SimulatorValueItem GetPlaceiTSimulationResult(SimulatorRoutineRevisionOutput outputConfig, Dictionary<string, string> arguments, CancellationToken token)
    {
        // Ensure we have all required parameters
        ValidateSimulationParameters();

        // Call the PlaceiT COM function
        dynamic result = CallPlaceiTCOMEntryPoint(token);

        SimulatorValue value;
        if (outputConfig.ValueType == SimulatorValueType.DOUBLE)
        {
            // If result is a single double value
            var doubleResult = Convert.ToDouble(result);
            value = new SimulatorValue.Double(doubleResult);
        }
        else if (outputConfig.ValueType == SimulatorValueType.STRING)
        {
            // If result is a string representation
            var stringResult = result?.ToString() ?? "";
            value = new SimulatorValue.String(stringResult);
        }
        else if (outputConfig.ValueType == SimulatorValueType.DOUBLE_ARRAY)
        {
            // If result is an array of doubles (variant array from COM)
            double[] doubleArray;
            if (result is Array array)
            {
                doubleArray = new double[array.Length];
                for (int i = 0; i < array.Length; i++)
                {
                    doubleArray[i] = Convert.ToDouble(array.GetValue(i));
                }
            }
            else
            {
                // Single value as array
                doubleArray = new double[] { Convert.ToDouble(result) };
            }
            value = new SimulatorValue.DoubleArray(doubleArray);
        }
        else
        {
            throw new NotImplementedException($"Output value type '{outputConfig.ValueType}' not implemented for PlaceiT simulation");
        }

        var simulationObjectRef = new Dictionary<string, string> {
            { "stepType", "placeit-simulation" },
            { "functionName", "PlaceiTCOMEntryPoint" }
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

    private dynamic CallPlaceiTCOMEntryPoint(CancellationToken token)
    {
        // Auto-initialize PlaceiT COM object if not already initialized
        if (_placeitComObject == null)
        {
            _logger.LogInformation("Auto-initializing PlaceiT COM object");
            InitializePlaceiTComObject();
        }

        try
        {
            _logger.LogInformation("Calling PlaceiTCOMEntryPoint function");

            // Extract parameters from the dictionary
            // Note: integers are stored as doubles since API only supports DOUBLE type
            var porosity = GetParameter<double>("porosity");
            var zonePressure = GetParameter<double>("zonePressure");
            var zoneLength = GetParameter<double>("zoneLength");
            var isothermOption = Convert.ToInt16(GetParameter<double>("isothermOption"));
            var isothermValues = GetParameter<double[]>("isothermValues");
            var adsorptionCapOption = Convert.ToInt16(GetParameter<double>("adsorptionCapOption"));
            var adsorptionCapValue = GetParameter<double>("adsorptionCapValue");
            
            // Convert double arrays to int arrays for enabled stages
            var enabledStagesDouble = GetParameter<double[]>("enabledStages");
            var enabledStages = enabledStagesDouble.Select(d => Convert.ToInt16(d)).ToArray();
            
            var stagesVol = GetParameter<double[]>("stagesVol");
            var stagesConc = GetParameter<double[]>("stagesConc");
            var injectionRate = GetParameter<double>("injectionRate");
            var timestep = Convert.ToInt16(GetParameter<double>("timestep"));
            var productionTime = GetParameter<double>("productionTime");
            var productionRate = GetParameter<double>("productionRate");
            var medRange = GetParameter<double[]>("medRange");

            _logger.LogDebug($"Parameters: porosity={porosity}, zonePressure={zonePressure}, zoneLength={zoneLength}");
            _logger.LogDebug($"Isotherm: option={isothermOption}, values=[{string.Join(",", isothermValues)}]");
            _logger.LogDebug($"Adsorption: option={adsorptionCapOption}, value={adsorptionCapValue}");
            _logger.LogDebug($"Enabled stages: [{string.Join(",", enabledStages)}]");

            // Call the VBA function using Application.Run
            // VBA functions in Excel must be invoked this way
            dynamic result = _workbook.Application.Run(
                "PlaceiTCOMEntryPoint",
                porosity,
                zonePressure,
                zoneLength,
                isothermOption,
                isothermValues,
                adsorptionCapOption,
                adsorptionCapValue,
                enabledStages,
                stagesVol,
                stagesConc,
                injectionRate,
                timestep,
                productionTime,
                productionRate,
                medRange
            );

            _logger.LogInformation("PlaceiTCOMEntryPoint function completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling PlaceiTCOMEntryPoint function");
            throw new InvalidOperationException("PlaceiT simulation failed", ex);
        }
    }

    private T GetParameter<T>(string parameterName)
    {
        if (!_simulationParameters.TryGetValue(parameterName, out var value))
        {
            throw new ArgumentException($"Required parameter '{parameterName}' not found");
        }

        try
        {
            return (T)value;
        }
        catch (InvalidCastException)
        {
            throw new ArgumentException($"Parameter '{parameterName}' cannot be cast to type {typeof(T).Name}");
        }
    }

    private void ValidateSimulationParameters()
    {
        var requiredParameters = new[]
        {
            "porosity", "zonePressure", "zoneLength", "isothermOption", "isothermValues",
            "adsorptionCapOption", "adsorptionCapValue", "enabledStages", "stagesVol",
            "stagesConc", "injectionRate", "timestep", "productionTime", "productionRate", "medRange"
        };

        var missingParameters = requiredParameters.Where(param => !_simulationParameters.ContainsKey(param)).ToList();
        
        if (missingParameters.Any())
        {
            throw new ArgumentException($"Missing required parameters: {string.Join(", ", missingParameters)}");
        }

        _logger.LogDebug($"All {requiredParameters.Length} required parameters are present");
    }

    public override void RunCommand(Dictionary<string, string> arguments, CancellationToken _token)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var command = arguments["action"] ?? arguments["command"] ?? "";

        switch (command)
        {
            case "Initialize":
                {
                    InitializePlaceiTComObject();
                    break;
                }
            case "Release":
                {
                    ReleasePlaceiTComObject();
                    break;
                }
            case "Reset":
                {
                    ResetSimulation();
                    break;
                }
            // Keep backward compatibility with old Excel commands if needed
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
