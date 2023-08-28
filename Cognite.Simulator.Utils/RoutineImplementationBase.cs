using Cognite.Simulator.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

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
        private Dictionary<string, LocalVariable> _variables;

        private Dictionary<string, string> _specialVariables;
        // private int _loopIteration ;
        private int _currentScope;
        // declare a variable to hold the current loop iterator as a Stack
        private Stack<string> _currentLoopIterator;


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
            _variables = new Dictionary<string, LocalVariable>();
            _currentScope = 0;
            _currentLoopIterator = new Stack<string>();
            _specialVariables = new Dictionary<string, string>();
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
        /// Gets a property value from the simulator
        /// </summary>
        /// <param name="args">args helpful in retreiving property from the simulator</param>
        public abstract string GetPropertyValue(Dictionary<string, string> args);  


        /// <summary>
        /// Creates a local variable
        /// </summary>
        /// <param name="localVariable"></param>
        /// <param name="value"></param>
        public void CreateLocalVariable(string localVariable, string value)
        {
            string accessor = CreateLocalVariableAccessor(localVariable);
            string iterator = _currentLoopIterator.Any() ?  _currentLoopIterator.Peek() : "";
            // Console.WriteLine($"\r\nCreating local variable {localVariable} with value = {value} . Level = {_currentScope} Iter = {iterator} \r\n");
            _variables[accessor] = new LocalVariable { 
                Name = localVariable, 
                Value = value,
                DeclaredInLoopIteration = _currentLoopIterator.Any() ? int.Parse(GetLocalVariable(_currentLoopIterator.Peek()).Value) : 0,
                DeclaredInLoopIterator = iterator,
                Scope = _currentScope,
                Accessor = accessor
            };
        }

        // <summary>
        /// Gets a local variable
        /// </summary>
        /// <param name="localVariable"></param>
        /// <param name="value"></param>
        public LocalVariable GetLocalVariable(string localVariable)
        {
            // int nestingLevel = _currentScopeLevel; 
            // int customLoopIteration = _loopIteration;
            // while (true)
            // {
            //     try
            //     {
            //         string accessor = CreateLocalVariableAccessor(localVariable, nestingLevel, customLoopIteration);
            //         if (!_variables.ContainsKey(accessor))
            //         {
            //             throw new SimulationException($"Access local variable error: local variable {localVariable} not defined (Nesting level: {nestingLevel})");
            //         }

            //         string value = _variables[accessor].Value;
            //         return value;
            //     }
            //     catch (Exception e) when (e is SimulationException)
            //     {
            //         if (nestingLevel <= 0)
            //         {
            //             throw new SimulationRoutineException($"Unable to access local variable {localVariable}");
            //         }
            //         nestingLevel--; 
            //         customLoopIteration = 0;
            //     }
            // }

            // First try to access the variable in the current scope
            string accessor = CreateLocalVariableAccessor(localVariable, _currentScope);
            // Console.WriteLine($"Accessing local variable {localVariable} with accessor = {accessor} . Level = {_currentScopeLevel} \r\n");
            if (_variables.ContainsKey(accessor))
            {
                // Console.WriteLine($"Accessing local variable {localVariable} with value = {_variables[accessor].Value} . Level = {_currentScopeLevel} \r\n");
                return _variables[accessor];
            } else {
                string currentLoopIterator = _currentLoopIterator.Any() ? _currentLoopIterator.Peek() : "";

                int currentLoopIteration = 0;
                if (_currentLoopIterator.Any())
                {
                    currentLoopIteration = int.Parse(_specialVariables[currentLoopIterator]);
                }
                // Loop through all the variables and find the ones declared in the nearest parent scope
                var matchingVariablesInParentScope = _variables.Where(v => v.Value.Name == localVariable && v.Value.Scope < _currentScope).ToList();
                var matchingVariablesInCurrentScope = _variables.Where(v => v.Value.Name == localVariable && v.Value.Scope == _currentScope && v.Value.DeclaredInLoopIteration == currentLoopIteration && v.Value.DeclaredInLoopIterator == currentLoopIterator).ToList();
                if (matchingVariablesInCurrentScope.Any())
                {
                    var matchingVariable = matchingVariablesInCurrentScope.OrderByDescending(v => v.Value.Scope).First();
                    // Console.WriteLine($"Accessing local variable {localVariable} with value = {matchingVariable.Value} . Level = {_currentScopeLevel} \r\n");
                    return matchingVariable.Value;
                } else if (matchingVariablesInParentScope.Any())
                {
                    var matchingVariable = matchingVariablesInParentScope.OrderByDescending(v => v.Value.Scope).First();
                    // Console.WriteLine($"Accessing local variable {localVariable} with value = {matchingVariable.Value} . Level = {_currentScopeLevel} \r\n");
                    return matchingVariable.Value;
                } else if (_specialVariables.ContainsKey(localVariable)) {
                    return new LocalVariable { 
                        Name = localVariable, 
                        Value = _specialVariables[localVariable],
                        DeclaredInLoopIteration = -1,
                        DeclaredInLoopIterator = "",
                        Scope = -1,
                        Accessor = localVariable
                    };
                }
                 else {
                    throw new SimulationRoutineException($"Unable to access local variable {localVariable}. Has it been declared?");
                }
            }
        }

        // public void SetLocalVariableValue(string localVariable, string value)
        // {
        //     if (IsLocalVariableDefined(localVariable))
        //     {
        //         string accessor = GetLocalVariable(localVariable).Accessor;
        //         // Console.WriteLine($"\r\nSetting local variable {localVariable} with value = {value} . Level = {_currentScopeLevel} Iter = {LoopIterator} \r\n");
        //         _variables[accessor].Value = value;
        //     } else {
        //         throw new SimulationRoutineException($"Unable to set local variable {localVariable}. Has it been declared?");
        //     }
        // }

        /// <summary>
        /// Creates a local variable accessor - to access it in the dictionary  
        /// <param name="localVariable">Name of the local variable</param>
        /// <param name="loopNestedLevel">Nesting level of the loop in which the variable was declared (for nested loops)</param>
        /// <param name="customLoopIteration">Iteration of the loop in which the variable was declared</param>
        /// </summary>
       public string CreateLocalVariableAccessor(string localVariable, int? scopeLevel = null)
        {
            int nestingLevel = scopeLevel ?? _currentScope;
            // int iteration = customLoopIteration ?? LoopIterator;
            string iteration = "";
            foreach (var item in _currentLoopIterator.Reverse())
            {
                iteration += _specialVariables[item] + "_";
            }

            return $"{nestingLevel}-{iteration}-{localVariable}";
        }

        /// <summary>
        /// Checks if a local variable is defined
        /// <param name="localVariable">Name of the local variable</param>
        /// </summary>
        public bool IsLocalVariableDefined(string localVariable)
        {
            try{
                //Simply checks if the variable is accessible
                GetLocalVariable(localVariable);
                return true;
            }
            catch(Exception e) when (e is SimulationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Implements a step that sets a local value as input to a simulation
        /// </summary>
        /// <param name="localVariable">Variable(s) to set</param>
        /// <param name="argType">argType</param>
        /// <param name="arguments">Extra arguments</param>
        public void SetLocalVariable(string localVariable , string argType, Dictionary<string, string> arguments)
        {
            
            switch (argType)
            {
                case "userDefined":
                    if (!arguments.TryGetValue("value", out string localVariableValue))
                    {
                        throw new SimulationException($"Get local error: value for assignment to local variable not defined");
                    }
                    CreateLocalVariable(localVariable, localVariableValue);
                    break;
                case "manual":
                    string value = GetPropertyValue(arguments);
                    CreateLocalVariable(localVariable, value);
                    break;
                case "timeseries":
                    if (!arguments.TryGetValue("value", out string tsVal))
                    {
                        throw new SimulationException($"Get local error: value for assignment to local variable not defined");
                    }
                    var matchingInputs = _config.InputTimeSeries.Where(i => i.Type == tsVal).ToList();
                    if (matchingInputs.Any() && _inputData.ContainsKey(tsVal))
                    {
                        // Set input time series
                        CreateLocalVariable(localVariable, _inputData[tsVal].ToString());
                    }
                    break;
                default:
                    break;
            }
        }

        static void DebugLog(string message)
        {
            Console.WriteLine($"{message}\r\n");
        }

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
            // Console.WriteLine($"Performing simulation with {_routine} routine");
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

        private void ParseLoop(Dictionary<string, object> arguments){
            if (!arguments.TryGetValue("steps", out object stepsObject) || !(stepsObject is JArray steps))
            {
                throw new SimulationException("Loop error: steps not defined");
            }

            var args = arguments
                        .Where(s => s.Key != "steps")
                        .ToDictionary(dict => dict.Key, dict => (string) dict.Value);

            args = PerformVariableSubstitutionInArguments(args);

            if (!args.TryGetValue("timesToLoop", out string timesToLoop))
            {
                throw new SimulationException($"Loop error: timesToLoop not defined");
            }
            if (!args.TryGetValue("loopIterator", out string localLoopIterator))
            {
                throw new SimulationException($"Loop error: loopIterator not defined");
            }


            try
            {
                var stepsArray = steps.Select(item =>  new CalculationProcedureStep
                    {
                        Type = (string)item["type"],
                        Step = (int)item["step"], // This is where you cast "Step" to int
                        Arguments = item["arguments"].ToObject<Dictionary<string, object>>()
                    }
                ).ToArray();

                int timesToLoopValue = int.Parse(timesToLoop);

                _specialVariables[localLoopIterator] = "0";
                _currentLoopIterator.Push(localLoopIterator);
                _currentScope++;    
                for (int i = 1; i <= timesToLoopValue; i++)
                {
                    // IncrementLoopIterator();
                    // DebugLog($"--Loop iteration {i}--\r\n");
                    foreach (var step in stepsArray)
                    {
                        StepParse(step.Type, step.Step, step.Arguments);
                    }
                    _specialVariables[localLoopIterator] = i.ToString();
                    CleanupScopedVariablesAfterLoopIteration(i, _currentLoopIterator.Peek());
                }
                // LoopIterator -= timesToLoopValue;
                _specialVariables.Remove(localLoopIterator);
                _currentLoopIterator.Pop();
                CleanupScopedVariables(_currentScope);
                _currentScope--;
            }
            catch (System.Exception e)
            {

                throw new SimulationRoutineException($"Unable to parse steps array {e.Message}");
            }
        }

        private bool SolveEquation(string leftSide, string comparator, string rightSide)
        {
            // Sanitize inputs by removing leading and trailing white spaces
            leftSide = leftSide.Trim();
            rightSide = rightSide.Trim();

            double leftValue, rightValue;

            if (double.TryParse(leftSide, out leftValue) && double.TryParse(rightSide, out rightValue))
            {
                switch (comparator)
                {
                    case "==": return Math.Abs(leftValue - rightValue) < double.Epsilon;
                    case "!=": return Math.Abs(leftValue - rightValue) >= double.Epsilon;
                    case ">=": return leftValue >= rightValue;
                    case "<=": return leftValue <= rightValue;
                    case "<": return leftValue < rightValue;
                    case ">": return leftValue > rightValue;
                    default: throw new NotSupportedException("Unsupported comparator");
                }
            }
            else
            {
                switch (comparator)
                {
                    case "==": return leftSide == rightSide;
                    case "!=": return leftSide != rightSide;
                    default: throw new SimulationRoutineException($"Unable to parse equation {leftSide} {comparator} {rightSide}");
                }
            }
        }


        void VerifyArgumentString<T>(Dictionary<string, T> arguments, string argumentName, out T argumentValue)
        {
            if (!arguments.TryGetValue(argumentName, out argumentValue))
            {
                throw new SimulationException($"Loop error: {argumentName} not defined");
            }
        }


        private void ParseConditional(Dictionary<string, object> arguments, string[] keysToExclude) {
            var elseSteps = new JArray();
            
            if (!arguments.TryGetValue("ifSteps", out object ifStepsObject) || !(ifStepsObject is JArray ifSteps))
            {
                throw new SimulationException("Conditional error: ifSteps not defined");
            }

            if (arguments.TryGetValue("elseSteps", out object elseStepsObject) && (elseStepsObject is JArray elseStepsParsed))
            {
                elseSteps = elseStepsParsed;
            }

            var args = arguments
                .Where(s => !keysToExclude.Contains(s.Key))
                .ToDictionary(dict => dict.Key, dict => (string)dict.Value);

            args = PerformVariableSubstitutionInArguments(args);

            VerifyArgumentString(args, "leftSide", out string leftSide);

            VerifyArgumentString(args, "comparator", out string comparator);

            VerifyArgumentString(args, "rightSide", out string rightSide);

            bool equationSolution = SolveEquation(leftSide, comparator, rightSide);

            Console.WriteLine($"equationSolution: {equationSolution} Solving = {leftSide} {comparator} {rightSide}");

            var stepsToRun = equationSolution ? ifSteps : elseSteps;

            _currentScope++;
            stepsToRun.Select(item => new CalculationProcedureStep
            {
                Type = (string)item["type"],
                Step = (int)item["step"],
                Arguments = item["arguments"].ToObject<Dictionary<string, object>>()
            })
            .ToList()
            .ForEach(step =>
            {
                StepParse(step.Type, step.Step, step.Arguments);
            });
            CleanupScopedVariables(_currentScope);
            _currentScope--;
            Console.WriteLine($"Current scope level: {_currentScope} after conditional");
        }

        private void CleanupScopedVariables(int level)
        {
            // Remove all variables that were declared in the current scope
            var variablesToRemove = _variables.Where(v => v.Value.Scope == level).ToList();
            foreach (var variable in variablesToRemove)
            {
                _variables.Remove(variable.Key);
            }
        }

        private void CleanupScopedVariablesAfterLoopIteration(int level, string currentLoopIterator = "")
        {
            // Remove all variables that were declared in the current scope
            var variablesToRemove = _variables.Where(v => v.Value.DeclaredInLoopIteration == level && v.Value.DeclaredInLoopIterator == currentLoopIterator).ToList();
            foreach (var variable in variablesToRemove)
            {
                _variables.Remove(variable.Key);
            }
        }

        private void StepParse(string stepType, int step , Dictionary<string, object> stepArguments)
        {
            // Console.WriteLine($"StepParse: {stepType} {step} . Scope Level : {_currentScope}");
            
            string[] keysToExclude = { "steps", "ifSteps", "elseSteps" };

            var stringArgs = stepArguments.Where(s => !keysToExclude.Contains(s.Key))
                                .ToDictionary(dict => dict.Key, dict => (string) dict.Value);
            try
            {
                switch (stepType)
                {
                    case "Command":
                        {
                            ParseCommand(stringArgs);
                            break;
                        }
                    case "Set":
                        {
                            ParseSet(stringArgs);
                            break;
                        }
                    case "Get":
                        {
                            ParseGet(stringArgs);
                            break;
                        }
                    case "Loop":
                        {
                            ParseLoop(stepArguments);
                            break;
                        }
                    case "Conditional":
                        {
                            ParseConditional(stepArguments, keysToExclude);
                            break;
                        }
                        throw new SimulationRoutineException($"Invalid procedure step: {stepType}", step: step);
                };
            }
            catch (Exception e) when (e is SimulationException)
            {
                throw new SimulationRoutineException(e.Message, step: step);
            }
        }

        private void ParseProcedure(CalculationProcedure procedure)
        {
            var orderedSteps = procedure.Steps.OrderBy(s => s.Step).ToList();
            foreach (var step in orderedSteps)
            {
                StepParse(step.Type, step.Step, step.Arguments);
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
            arguments = PerformVariableSubstitutionInArguments(arguments);
            if (!arguments.TryGetValue("value", out string argValue) )
            {   
                if (!arguments.TryGetValue("storeInLocalVariable", out string checkLocalVariable)){
                    throw new SimulationException($"Get error: Output value not defined");
                }
            }
            

            var extraArgs = arguments.Where(s => s.Key != "type" && s.Key != "value")
                .ToDictionary(dict => dict.Key, dict => dict.Value);
            switch (argType)
            {
                case "outputTimeSeries":
                    // Get the simulation result as a time series data point
                    var matchingOutputs = _config.OutputTimeSeries.Where(i => i.Type == argValue).ToList();
                    if (matchingOutputs.Any())
                    {
                        if (arguments.TryGetValue("useLocalVariable", out string localVariableObj) && localVariableObj is string tslocalVariable)
                        {
                            // Set timeseries with local variable value
                            var output = matchingOutputs.First();
                            try
                            {
                                _simulationResults[output.Type] = double.Parse(GetLocalVariable(tslocalVariable).Value);

                            }
                            catch (System.Exception)
                            {
                                throw new SimulationRoutineException($"Unable to convert local variable ${tslocalVariable} with value ${GetLocalVariable(tslocalVariable).Value} to double");
                            }
                        } else {
                            // Set output time series
                            var output = matchingOutputs.First();
                            _simulationResults[output.Type] = GetTimeSeriesOutput(output, extraArgs);                        
                        }
                    }   
                    break;
                case "manual":
                case "userDefined":
                case "timeseries":
                    if (!arguments.TryGetValue("storeInLocalVariable", out string argTypeObj) || !(argTypeObj is string localVariable))
                    {
                        throw new SimulationException($"Get error: localVariable not defined");
                    }
                    // Set manual input (from inside the routine, legacy)
                    SetLocalVariable(localVariable, argType, arguments);
                    break;        
                default:
                    throw new SimulationException($"Get error: Invalid output type {argType}");
            }
        }

        private string ReplaceAllInstancesInString(string input, string search, string replace)
        {
            int index = input.IndexOf(search);
            while (index != -1)
            {   
                input = input.Substring(0, index) + replace + input.Substring(index + search.Length);
                index = input.IndexOf(search, index + replace.Length);
            }
            return input;
        }

        private string SubstituteLocalVariable(string currentValue, string localVariable)
        {
            if (IsLocalVariableDefined(localVariable) )
            {
                string newValue = GetLocalVariable(localVariable).Value;
                return ReplaceAllInstancesInString(currentValue, localVariable,newValue );
            } else if ( _specialVariables.ContainsKey(localVariable) )
            {
                string newValue = _specialVariables[localVariable];
                return ReplaceAllInstancesInString(currentValue, localVariable,newValue );
            }
            else
            {
                throw new SimulationException($"SubstituteLocalVariable local error: local variable {localVariable} not defined");
            }
        }

        private Dictionary<string, string> PerformVariableSubstitutionInArguments(Dictionary<string, string> arguments)
        {
            // Substitute local variables
            if (arguments.TryGetValue("useLocalVariable", out string localVariable))
            {
                if (arguments.TryGetValue("substituteLocalVariableIn", out string substitutionKey))
                {
                    arguments[substitutionKey] = SubstituteLocalVariable(arguments[substitutionKey], localVariable);
                }
            }
            return arguments;
        }

        private void ParseSet(Dictionary<string, string> arguments)
        {
            if (!arguments.TryGetValue("type", out string argType))            {
                throw new SimulationException($"Set error: Assignment type not defined");
            }

            arguments = PerformVariableSubstitutionInArguments(arguments);

            if (!arguments.TryGetValue("value", out string argValue))
            {
                throw new SimulationException($"Set error: Input value not defined");
            }
            var extraArgs = arguments.Where(s => s.Key != "type" && s.Key != "value")
                .Where(pair => pair.Key != "type" && pair.Key != "value" && pair.Value is string)
                .ToDictionary(dict => dict.Key, dict => (string) dict.Value);

            switch(argType) {
                case "inputTimeSeries":
                    var matchingInputs = _config.InputTimeSeries.Where(i => i.Type == argValue).ToList();
                    if (matchingInputs.Any() && _inputData.ContainsKey(argValue))
                    {
                        if (arguments.TryGetValue("storeInLocalVariable", out string localVariableObj) && localVariableObj is string tslocalVariable)
                        {
                            // Set local variable with the time series value
                            CreateLocalVariable(tslocalVariable, _inputData[argValue].ToString());
                        } else {
                            // Set input time series
                            SetTimeSeriesInput(matchingInputs.First(), _inputData[argValue], extraArgs);
                        }
                    }
                    else
                    {
                        throw new SimulationException($"Set error: Input time series with key {argValue} not found");
                    }
                    break;
                case "inputConstant":
                    var matchingInputManualValues = _config.InputConstants.Where(i => i.Type == argValue).ToList();
                    if (matchingInputManualValues.Any() && _inputData.ContainsKey(argValue))
                    {
                        var inputManualValue = matchingInputManualValues.First();
                        extraArgs.Add("unit", inputManualValue.Unit);
                        if (inputManualValue.UnitType != null) {
                            extraArgs.Add("unitType", inputManualValue.UnitType);
                        }
                        // Set manual input
                        SetManualInput(_inputData[argValue].ToString(), extraArgs);
                    }
                    else
                    {
                        throw new SimulationException($"Set error: Manual value input with key {argValue} not found");
                    }
                    break;
                case "manual":
                    // Set manual input (from inside the routine, legacy)
                    SetManualInput(argValue, extraArgs);
                    break;            
                default:
                    throw new SimulationException($"Set error: Invalid argument type {argType}");
            }
        }    
    }

    /// <summary>
    /// Represents a local variable
    /// </summary>
    public class LocalVariable {
        /// <summary>
        /// For fast access to this variable in the dictionary
        /// </summary>
        public string Accessor { get; set; }

        /// <summary>
        /// Name of the variable
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Value of the variable
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Declared in which loop iteration
        /// </summary>
        public int DeclaredInLoopIteration { get; set; }

        /// <summary>
        /// Declared in which loop iterator
        /// </summary>
        public string DeclaredInLoopIterator { get; set; }

        /// <summary>
        /// The scope this variable is created in
        /// </summary>
        public int Scope { get; set; }
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