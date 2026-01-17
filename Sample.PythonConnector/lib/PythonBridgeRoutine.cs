using Cognite.Simulator.Utils;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

namespace Sample.PythonConnector.Lib;

public class PythonBridgeRoutine : PythonBridgeBase
{
    private readonly string _modelPath;
    private readonly PythonConfig _config;
    
    internal dynamic? RoutineInstance { get; private set; }

    public PythonBridgeRoutine(
        string modelPath,
        SimulatorRoutineRevision routineRevision,
        Dictionary<string, SimulatorValueItem> inputData,
        ILogger logger,
        PythonConfig config)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        ArgumentNullException.ThrowIfNull(config);
        
        _modelPath = modelPath;
        _config = config;
        _routine = new PythonRoutineImplementation(routineRevision, inputData, logger, this);
        LoadRoutineModule();
    }

    private readonly PythonRoutineImplementation _routine;

    public Dictionary<string, SimulatorValueItem> PerformSimulation(CancellationToken token)
    {
        return _routine.PerformSimulation(token);
    }

    private void LoadRoutineModule()
    {
        RoutineInstance = LoadPythonModule(_config.RoutinePyPath, "SimulatorRoutine", _modelPath);
    }

    private class PythonRoutineImplementation : RoutineImplementationBase
    {
        private readonly PythonBridgeRoutine _parent;

        public PythonRoutineImplementation(
            SimulatorRoutineRevision routineRevision,
            Dictionary<string, SimulatorValueItem> inputData,
            ILogger logger,
            PythonBridgeRoutine parent)
            : base(routineRevision, inputData, logger)
        {
            _parent = parent;
        }

        public override void SetInput(
            SimulatorRoutineRevisionInput inputConfig,
            SimulatorValueItem input,
            Dictionary<string, string> arguments,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(arguments);

            if (_parent.RoutineInstance == null)
                throw new InvalidOperationException("Python routine not initialized");

            _parent.RunPython(() =>
            {
                using var pyArgs = ToPyDict(arguments);
                object value = _parent.ConvertToPythonValue(input);
                _parent.RoutineInstance.set_input(pyArgs, value);
            }, "set input", isSimulation: true);
        }

        public override SimulatorValueItem GetOutput(
            SimulatorRoutineRevisionOutput outputConfig,
            Dictionary<string, string> arguments,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(outputConfig);
            ArgumentNullException.ThrowIfNull(arguments);

            if (_parent.RoutineInstance == null)
                throw new InvalidOperationException("Python routine not initialized");

            return _parent.RunPython(() =>
            {
                using var pyArgs = ToPyDict(arguments);
                dynamic rawValue = _parent.RoutineInstance.get_output(pyArgs);
                SimulatorValue value = _parent.ConvertFromPythonValue(rawValue, outputConfig.ValueType);
                
                return new SimulatorValueItem
                {
                    ValueType = outputConfig.ValueType,
                    Value = value,
                    ReferenceId = outputConfig.ReferenceId,
                    SimulatorObjectReference = arguments,
                    TimeseriesExternalId = outputConfig.SaveTimeseriesExternalId,
                };
            }, "get output", isSimulation: true);
        }

        public override void RunCommand(Dictionary<string, string> arguments, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(arguments);

            if (_parent.RoutineInstance == null)
                throw new InvalidOperationException("Python routine not initialized");

            _parent.RunPython(() =>
            {
                using var pyArgs = ToPyDict(arguments);
                _parent.RoutineInstance.run_command(pyArgs);
            }, "run command", isSimulation: true);
        }
    }

    private object ConvertToPythonValue(SimulatorValueItem input) => input.Value switch
    {
        SimulatorValue.Double d => d.Value,
        SimulatorValue.String s => s.Value,
        _ => throw new NotImplementedException($"{input.ValueType} not implemented")
    };

    private SimulatorValue ConvertFromPythonValue(dynamic pythonValue, SimulatorValueType targetType) => targetType switch
    {
        SimulatorValueType.DOUBLE => new SimulatorValue.Double(Convert.ToDouble(pythonValue)),
        SimulatorValueType.STRING => new SimulatorValue.String(pythonValue?.ToString() ?? ""),
        _ => throw new NotImplementedException($"{targetType} not implemented")
    };
}
