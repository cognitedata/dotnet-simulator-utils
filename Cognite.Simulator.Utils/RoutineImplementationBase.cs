using Cognite.Simulator.Utils;
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
    /// This class parses routines of type <see cref="SimulationConfigurationWithRoutine"/>
    /// and calls the abstract methods that executes each step type.
    /// </summary>
    public abstract class RoutineImplementationBase
    {
        private readonly IEnumerable<CalculationProcedure> _routine;
        private readonly SimulationConfigurationWithRoutine _config;
        private readonly Dictionary<string, double> _inputData;
        private readonly Dictionary<string, double> _simulationResults;

        /// <summary>
        /// Creates a new simulation routine with the given configuration
        /// </summary>
        /// <param name="config">Simulation configuration object</param>
        /// <param name="inputData">Data to use as input</param>
        public RoutineImplementationBase(
            SimulationConfigurationWithRoutine config,
            Dictionary<string, double> inputData)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            _routine = config.Routine;
            _config = config;
            _simulationResults = new Dictionary<string, double>();
            _inputData = inputData;
        }

        /// <summary>
        /// Implements a step that sets the value sampled from a time series
        /// as input to a simulation
        /// </summary>
        /// <param name="inputConfig">Time series input configuration</param>
        /// <param name="value">Value to set</param>
        /// <param name="arguments">Extra arguments</param>
        public abstract void SetTimeSeriesInput(
            InputTimeSeriesConfiguration inputConfig, 
            double value, 
            Dictionary<string, string> arguments);

        /// <summary>
        /// Implements a step that sets a manual value as input to a simulation
        /// </summary>
        /// <param name="value">Value to set</param>
        /// <param name="arguments">Extra arguments</param>
        public abstract void SetManualInput(
            string value, 
            Dictionary<string, string> arguments);

        /// <summary>
        /// Gets a numeric simulation result that should be saved
        /// as a time series
        /// </summary>
        /// <param name="outputConfig">Output time series configuration</param>
        /// <param name="arguments">Extra arguments</param>
        /// <returns></returns>
        public abstract double GetTimeSeriesOutput(
            OutputTimeSeriesConfiguration outputConfig, 
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
        public virtual Dictionary<string, double> PerformSimulation()
        {
            _simulationResults.Clear();
            if (_config.CalculationType != "UserDefined")
            {
                throw new SimulationException($"Calculation type not supported: {_config.CalculationType}");
            }
            if (_routine == null || !_routine.Any())
            {
                throw new SimulationException("Missing calculation routine");
            }

            var orderedRoutine = _routine.OrderBy(p => p.Order).ToList();

            foreach (var procedure in orderedRoutine)
            {
                try
                {
                    ParseProcedure(procedure);
                }
                catch (SimulationRoutineException e)
                {
                    throw new SimulationRoutineException(e.OriginalMessage, procedure.Order, e.Step);
                }

            }
            return _simulationResults;
        }

        private void ParseProcedure(CalculationProcedure procedure)
        {
            var orderedSteps = procedure.Steps.OrderBy(s => s.Step).ToList();
            foreach (var step in orderedSteps)
            {
                try
                {
                    switch (step.Type)
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
                            throw new SimulationRoutineException($"Invalid procedure step: {step.Type}", step: step.Step);
                    };
                }
                catch (Exception e) when (e is SimulationException)
                {
                    throw new SimulationRoutineException(e.Message, step: step.Step);
                }
            }
        }

        private void ParseCommand(Dictionary<string, string> arguments)
        {
            if (!arguments.TryGetValue("type", out string argType))
            {
                argType = null;
            }
            var extraArgs = arguments.Where(s => s.Key != "type")
                .ToDictionary(dict => dict.Key, dict => dict.Value);
            // Perform command
            RunCommand(argType, extraArgs);
        }

        private void ParseGet(Dictionary<string, string> arguments)
        {
            if (!arguments.TryGetValue("type", out string argType))
            {
                throw new SimulationException($"Get error: Assignment type not defined");
            }
            if (!arguments.TryGetValue("value", out string argValue))
            {
                throw new SimulationException($"Get error: Output value not defined");
            }
            var extraArgs = arguments.Where(s => s.Key != "type" && s.Key != "value")
                .ToDictionary(dict => dict.Key, dict => dict.Value);
            
            if (argType == "outputTimeSeries")
            {
                // Get the simulation result as a time series data point
                var matchingOutputs = _config.OutputTimeSeries.Where(i => i.Type == argValue).ToList();
                if (matchingOutputs.Any())
                {
                    var output = matchingOutputs.First();
                    _simulationResults[output.Type] = GetTimeSeriesOutput(output, extraArgs);
                }
            }
            else
            {
                throw new SimulationException($"Get error: Invalid output type {argType}");
            }
        }

        private void ParseSet(Dictionary<string, string> arguments)
        {
            if (!arguments.TryGetValue("type", out string argType))
            {
                throw new SimulationException($"Set error: Assignment type not defined");
            }
            if (!arguments.TryGetValue("value", out string argValue))
            {
                throw new SimulationException($"Set error: Input value not defined");
            }
            var extraArgs = arguments.Where(s => s.Key != "type" && s.Key != "value")
                .ToDictionary(dict => dict.Key, dict => dict.Value);

            if (argType == "inputTimeSeries")
            {
                var matchingInputs = _config.InputTimeSeries.Where(i => i.Type == argValue).ToList();
                if (matchingInputs.Any() && _inputData.ContainsKey(argValue))
                {
                    // Set input time series
                    SetTimeSeriesInput(matchingInputs.First(), _inputData[argValue], extraArgs);
                }
                else
                {
                    throw new SimulationException($"Set error: Input time series with key {argValue} not found");
                }
            }
            else if (argType == "manual")
            {
                // Set manual input
                SetManualInput(argValue, extraArgs);
            }
            else
            {
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
        /// Which procedure failed
        /// </summary>
        public int Procedure { get; }
        
        /// <summary>
        /// Which step failed
        /// </summary>
        public int Step { get; }
        
        /// <summary>
        /// Original message coming from the simulator
        /// </summary>
        public string OriginalMessage { get; }

        /// <summary>
        /// Creates a new exception with the provided parameters
        /// </summary>
        /// <param name="message">Simulator error message</param>
        /// <param name="procedure">Procedure number</param>
        /// <param name="step">Step number</param>
        public SimulationRoutineException(string message, int procedure = 0, int step = 0)
            : base($"{procedure}.{step}: {message}")
        {
            Procedure = procedure;
            Step = step;
            OriginalMessage = message;
        }
    }
}