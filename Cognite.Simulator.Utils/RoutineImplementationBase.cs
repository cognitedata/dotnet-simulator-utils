using Cognite.Simulator.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    public abstract class RoutineImplementationBase
    {
        private readonly IEnumerable<CalculationProcedure> _routine;
        private readonly SimulationConfigurationWithRoutine _config;
        private readonly Dictionary<string, double> _inputData;
        private readonly Dictionary<string, double> _simulationResults;

        public RoutineImplementationBase(
            SimulationConfigurationWithRoutine config,
            Dictionary<string, double> inputData)
        {
            _routine = config.Routine;
            _config = config;
            _simulationResults = new Dictionary<string, double>();
            _inputData = inputData;
        }

        public abstract void SetTimeSeriesInput(InputTimeSeriesConfiguration inputConfig, double value, Dictionary<string, string> arguments);

        public abstract void SetManualInput(string value, Dictionary<string, string> arguments);

        public abstract double GetTimeSeriesOutput(OutputTimeSeriesConfiguration outputConfig, Dictionary<string, string> arguments);

        public abstract void RunCommand(string command, Dictionary<string, string> arguments);

        public Dictionary<string, double> PerformSimulation()
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
                throw new SimulationException($"Command error: Assignment type not defined");
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

    public class SimulationRoutineException : SimulationException
    {
        public int Procedure { get; } = 0;
        public int Step { get; } = 0;
        public string OriginalMessage { get; }

        public SimulationRoutineException(string message, int procedure = 0, int step = 0)
            : base($"{procedure}.{step}: {message}")
        {
            Procedure = procedure;
            Step = step;
            OriginalMessage = message;
        }
    }
}