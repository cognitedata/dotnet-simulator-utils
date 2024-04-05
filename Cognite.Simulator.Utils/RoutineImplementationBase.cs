using Cognite.Simulator.Utils;
using CogniteSdk.Alpha;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Base implementation for simulation routines.
    /// This class parses routines of type <see cref="SimulatorRoutineRevision" />
    /// and calls the abstract methods that executes each step type.
    /// </summary>
    public abstract class RoutineImplementationBase
    {
        private readonly IEnumerable<SimulatorRoutineRevisionScriptStage> _script;
        private readonly SimulatorRoutineRevisionConfiguration _config;
        private readonly Dictionary<string, SimulatorValueItem> _inputData;
        private readonly Dictionary<string, SimulatorValueItem> _simulationResults;

        /// <summary>
        /// Creates a new simulation routine with the given routine revision
        /// </summary>
        /// <param name="routineRevision">Routine revision object</param>
        /// <param name="inputData">Data to use as input</param>
        public RoutineImplementationBase(
            SimulatorRoutineRevision routineRevision,
            Dictionary<string, SimulatorValueItem> inputData)
        {
            if (routineRevision == null)
            {
                throw new ArgumentNullException(nameof(routineRevision));
            }
            _script = routineRevision.Script;
            _config = routineRevision.Configuration;
            _simulationResults = new Dictionary<string, SimulatorValueItem>();
            _inputData = inputData;
        }

        /// <summary>
        /// Implements a step that sets the value of an input to a simulation
        /// </summary>
        /// <param name="inputConfig">Input configuration</param>
        /// <param name="input">Input value</param>
        /// <param name="arguments">Extra arguments</param>
        public abstract void SetInput(
            SimulatorRoutineRevisionInput inputConfig,
            SimulatorValueItem input,
            Dictionary<string, string> arguments);

        /// <summary>
        /// Gets a numeric simulation result that should be saved
        /// as a time series
        /// </summary>
        /// <param name="outputConfig">Output time series configuration</param>
        /// <param name="arguments">Extra arguments</param>
        /// <returns></returns>
        public abstract SimulatorValueItem GetOutput(
            SimulatorRoutineRevisionOutput outputConfig,
            Dictionary<string, string> arguments);

        /// <summary>
        /// Invoke the given command on the simulator using the provided arguments.
        /// The <paramref name="command"/> parameter can be <c>null</c> in case 
        /// the provided arguments are sufficient to run the command
        /// </summary>
        /// <param name="command">Command to invoke, or <c>null</c></param>
        /// <param name="arguments">Extra arguments</param>
        public abstract void RunCommand(
            string command,
            Dictionary<string, string> arguments);

        /// <summary>
        /// Perform the simulation routine and collect the results
        /// </summary>
        /// <returns>Simulation results</returns>
        /// <exception cref="SimulationException">When the simulation configuration is invalid</exception>
        /// <exception cref="SimulationRoutineException">When the routine execution fails</exception>
        public virtual Dictionary<string, SimulatorValueItem> PerformSimulation()
        {
            _simulationResults.Clear();
            // if (_config.CalculationType != "UserDefined")
            // {
            //     throw new SimulationException($"Calculation type not supported: {_config.CalculationType}");
            // }
            if (_script == null || !_script.Any())
            {
                throw new SimulationException("Missing routine script");
            }

            var orderedRoutine = _script.OrderBy(p => p.Order).ToList();

            foreach (var stage in orderedRoutine)
            {
                try
                {
                    ParseScriptStage(stage);
                }
                catch (SimulationRoutineException e)
                {
                    throw new SimulationRoutineException(e.OriginalMessage, stage.Order, e.StepNumber);
                }

            }
            return _simulationResults;
        }

        private void ParseScriptStage(SimulatorRoutineRevisionScriptStage stage)
        {
            var orderedSteps = stage.Steps.OrderBy(s => s.Order).ToList();
            foreach (var step in orderedSteps)
            {
                try
                {
                    switch (step.StepType)
                    {
                        case "Command":
                            {
                                ParseCommand(step.Arguments);
                                break;
                            }
                        case "Set":
                            {
                                ParseSet(step.Arguments);
                                break;
                            }
                        case "Get":
                            {
                                ParseGet(step.Arguments);
                                break;
                            }
                            throw new SimulationRoutineException($"Invalid procedure step: {step.StepType}", stepNumber: step.Order);
                    };
                }
                catch (Exception e) when (e is SimulationException)
                {
                    throw new SimulationRoutineException(e.Message, stepNumber: step.Order);
                }
            }
        }

        private void ParseCommand(Dictionary<string, string> arguments)
        {
            if (!arguments.TryGetValue("argumentType", out string argType))
            {
                argType = null;
            }
            var extraArgs = arguments.Where(s => s.Key != "argumentType")
                .ToDictionary(dict => dict.Key, dict => dict.Value);
            // Perform command
            RunCommand(argType, extraArgs);
        }

        private void ParseGet(Dictionary<string, string> arguments)
        {
            if (!arguments.TryGetValue("argumentType", out string argType))
            {
                throw new SimulationException($"Get error: Assignment type not defined");
            }
            if (!arguments.TryGetValue("referenceId", out string argRefId))
            {
                throw new SimulationException($"Get error: Output value not defined");
            }
            var extraArgs = arguments.Where(s => s.Key != "referenceId" && s.Key != "argumentType")
                .ToDictionary(dict => dict.Key, dict => dict.Value);
            
            var matchingOutputs = _config.Outputs.Where(i => i.ReferenceId == argRefId).ToList();
            if (matchingOutputs.Any())
                {
                    var output = matchingOutputs.First();
                    if (argType == "outputTimeSeries")
                    {
                        // Get the simulation result as a time series data point
                        _simulationResults[output.ReferenceId] = GetOutput(output, extraArgs);
                    }
                }
            else
            {
                throw new SimulationException($"Get error: Invalid output type {argType}");
            }
        }

        private void ParseSet(Dictionary<string, string> arguments)
        {
            if (!arguments.TryGetValue("argumentType", out string argType))
            {
                throw new SimulationException($"Set error: Assignment type not defined");
            }
            if (!arguments.TryGetValue("referenceId", out string argRefId))
            {
                throw new SimulationException($"Set error: Input value not defined");
            }
            var extraArgs = arguments.Where(s => s.Key != "referenceId" && s.Key != "argumentType")
                .ToDictionary(dict => dict.Key, dict => dict.Value);

            switch (argType)
            {
                case "inputTimeSeries":
                    var matchingInputs = _config.Inputs.Where(i => i.ReferenceId == argRefId && i.IsTimeSeries).ToList();
                    if (matchingInputs.Any() && _inputData.ContainsKey(argRefId))
                    {
                        // Set input time series
                        SetInput(matchingInputs.First(), _inputData[argRefId], extraArgs);
                    }
                    else
                    {
                        throw new SimulationException($"Set error: Input time series with key {argRefId} not found");
                    }
                    break;
                case "inputConstant":
                    var matchingInputManualValues = _config.Inputs.Where(i => i.ReferenceId == argRefId && i.IsConstant).ToList();
                    if (matchingInputManualValues.Any() && _inputData.ContainsKey(argRefId))
                    {
                        var inputManualValue = matchingInputManualValues.First();
                        // if (inputManualValue.Unit != null)
                        // {
                        //     extraArgs.Add("unit", inputManualValue.Unit.Name);
                        //     if (inputManualValue.Unit.Type != null)
                        //     {
                        //         extraArgs.Add("unitType", inputManualValue.Unit.Type);
                        //     }
                        // } TODO: this is a part of the SimulatorRoutineRevisionInput now, do we need to add it to the extraArgs?
                        // Set constant input
                        SetInput(inputManualValue, _inputData[argRefId], extraArgs);
                    }
                    else
                    {
                        throw new SimulationException($"Set error: Constant value input with key {argRefId} not found");
                    }
                    break;
                default:
                    throw new SimulationException($"Set error: Invalid argument type {argType}");
            }
        }
    }

    /// <summary>
    /// Represents a simulation routine error
    /// </summary>
    public class SimulationRoutineException : SimulationException
    {
        /// <summary>
        /// Which stage failed
        /// </summary>
        public int StageNumber { get; }

        /// <summary>
        /// Which step failed
        /// </summary>
        public int StepNumber { get; }

        /// <summary>
        /// Original message coming from the simulator
        /// </summary>
        public string OriginalMessage { get; }

        /// <summary>
        /// Creates a new exception with the provided parameters
        /// </summary>
        /// <param name="message">Simulator error message</param>
        /// <param name="stageNumber">Stage number</param>
        /// <param name="stepNumber">Step number</param>
        public SimulationRoutineException(string message, int stageNumber = 0, int stepNumber = 0)
            : base($"{stageNumber}.{stepNumber}: {message}")
        {
            StageNumber = stageNumber;
            StepNumber = stepNumber;
            OriginalMessage = message;
        }
    }
}