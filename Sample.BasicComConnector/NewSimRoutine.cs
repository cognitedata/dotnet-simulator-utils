using Microsoft.Extensions.Logging;
using CogniteSdk.Alpha;
using Cognite.Simulator.Utils;
using System.Text.Json;

public class NewSimRoutine : RoutineImplementationBase
{
    private readonly dynamic _workbook;
    private readonly Dictionary<string, object> _simulationParameters = new();
    private readonly ILogger _logger;
    private object[,]? _cachedResultArray; // Cache the 2D result array

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

        // Check if this is one of the PlaceiT simulation result outputs
        if (referenceId == "time_days" || referenceId == "concentration_mg_L" || referenceId == "volume_m3")
        {
            return GetPlaceiTArrayOutput(referenceId, outputConfig, _token);
        }
        else
        {
            throw new NotImplementedException($"Output reference ID '{referenceId}' not implemented");
        }
    }

    private SimulatorValueItem GetPlaceiTArrayOutput(string referenceId, SimulatorRoutineRevisionOutput outputConfig, CancellationToken token)
    {
        // If we haven't run the simulation yet, run it and cache the results
        if (_cachedResultArray == null)
        {
            // Ensure we have all required parameters
            ValidateSimulationParameters();

            // Call the PlaceiT COM function
            dynamic result = CallPlaceiTCOMEntryPoint(token);

            // Parse PlaceiT result structure: result[0] = error code, result[1] = 2D array
            if (result is object[] wrapper && wrapper.Length == 2)
            {
                int errorCode = Convert.ToInt32(wrapper[0]);
                
                if (errorCode != 0)
                {
                    // Simulation failed - log details for troubleshooting
                    _logger.LogError($"PlaceiT simulation failed with error code {errorCode}");
                    _logger.LogError("This error typically indicates issues like:");
                    _logger.LogError("  - Gridding error: Invalid mesh or grid configuration");
                    _logger.LogError("  - Rounding error: Timestep too large for numerical stability");
                    _logger.LogError("  - Convergence failure: Parameters causing non-convergence");
                    _logger.LogError("Input parameters:");
                    _logger.LogError($"  timestep = {_simulationParameters.GetValueOrDefault("timestep")}");
                    _logger.LogError($"  productionTime = {_simulationParameters.GetValueOrDefault("productionTime")}");
                    _logger.LogError($"  productionRate = {_simulationParameters.GetValueOrDefault("productionRate")}");
                    _logger.LogError($"  porosity = {_simulationParameters.GetValueOrDefault("porosity")}");
                    _logger.LogError($"  zonePressure = {_simulationParameters.GetValueOrDefault("zonePressure")}");
                    _logger.LogError($"  zoneLength = {_simulationParameters.GetValueOrDefault("zoneLength")}");
                    
                    throw new InvalidOperationException($"PlaceiT simulation failed with error code {errorCode}. Check logs for parameter details.");
                }
                
                _logger.LogInformation($"PlaceiT simulation completed successfully (error code: {errorCode})");
                
                if (wrapper[1] is object[,] array2D)
                {
                    // Success - cache the 2D array
                    _cachedResultArray = array2D;
                    int resultRows = array2D.GetLength(0);
                    _logger.LogInformation($"PlaceiT simulation successful. Result contains {resultRows} data points");
                    _logger.LogDebug($"First point - Time: {array2D[0, 0]} days, Conc: {array2D[0, 1]} mg/L, Vol: {array2D[0, 2]} m³");
                    if (resultRows > 1)
                    {
                        int lastIdx = resultRows - 1;
                        _logger.LogDebug($"Last point - Time: {array2D[lastIdx, 0]} days, Conc: {array2D[lastIdx, 1]} mg/L, Vol: {array2D[lastIdx, 2]} m³");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Unexpected result format: result[1] is not a 2D array");
                }
            }
            else
            {
                throw new InvalidOperationException($"Unexpected result structure: {result?.GetType().Name ?? "null"}");
            }
        }

        // Extract the requested column from the cached result array
        int rows = _cachedResultArray.GetLength(0);
        double[] outputArray = new double[rows];
        int columnIndex;
        string unit;

        switch (referenceId)
        {
            case "time_days":
                columnIndex = 0;
                unit = "days";
                break;
            case "concentration_mg_L":
                columnIndex = 1;
                unit = "mg/L";
                break;
            case "volume_m3":
                columnIndex = 2;
                unit = "m³";
                break;
            default:
                throw new ArgumentException($"Unknown output reference ID: {referenceId}");
        }

        // Extract column data
        for (int i = 0; i < rows; i++)
        {
            outputArray[i] = Convert.ToDouble(_cachedResultArray[i, columnIndex]);
        }

        _logger.LogInformation($"Returning {referenceId}: {rows} values ({unit})");

        var simulationObjectRef = new Dictionary<string, string> {
            { "stepType", "placeit-simulation" },
            { "functionName", "PlaceiTCOMEntryPoint" },
            { "column", columnIndex.ToString() },
            { "unit", unit }
        };

        return new SimulatorValueItem
        {
            ValueType = SimulatorValueType.DOUBLE_ARRAY,
            Value = new SimulatorValue.DoubleArray(outputArray),
            ReferenceId = referenceId,
            SimulatorObjectReference = simulationObjectRef,
            TimeseriesExternalId = outputConfig.SaveTimeseriesExternalId,
        };
    }

    private dynamic CallPlaceiTCOMEntryPoint(CancellationToken token)
    {
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

            // CRITICAL: Aggressively suppress ALL Excel/VBA alerts and error dialogs
            // This prevents pop-ups (gridding errors, rounding errors, etc.) from blocking execution
            // Excel will auto-dismiss the alerts, allowing VBA to return error codes instead of hanging
            var application = _workbook.Application;
            application.DisplayAlerts = false;           // Suppress all alerts
            application.Interactive = false;              // Block user interaction completely
            application.EnableEvents = false;             // Disable all Excel events
            application.ScreenUpdating = false;          // Prevent screen updates
            
            // Disable all error checking that might show dialogs
            try { application.ErrorCheckingOptions.BackgroundChecking = false; } catch { /* Ignore if not available */ }
            
            // Additional settings to suppress dialogs
            try { application.AlertBeforeOverwriting = false; } catch { /* Ignore if not available */ }
            try { application.AskToUpdateLinks = false; } catch { /* Ignore if not available */ }
            try { application.FeatureInstall = 0; } catch { /* 0 = msoFeatureInstallNone */ }
            
            _logger.LogDebug("Suppressed all Excel alerts and error dialogs");
            
            // Start dialog suppressor to auto-dismiss any error pop-ups from VBA/DLLs
            // This is necessary because PlaceiT VBA code cannot be modified and may show MsgBox dialogs
            using (var dialogSuppressor = new DialogSuppressor(_logger))
            {
                dialogSuppressor.Start();
                
                try
                {
                    // Call the VBA function using Application.Run
                    // VBA functions in Excel must be invoked this way
                    dynamic result = application.Run(
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
                finally
                {
                    dialogSuppressor.Stop();
                }
            }
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
