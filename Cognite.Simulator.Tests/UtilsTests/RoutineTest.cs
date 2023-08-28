

using System;
using System.Collections.Generic;
using Cognite.Simulator.Utils;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public class RoutineBuilderTest
    {
        private SimulationConfigurationWithRoutine simulationConfig;
        public RoutineBuilderTest() : base() {
            var inputConstants = new List<InputConstantConfiguration>
            {
                new InputConstantConfiguration
                {
                    Name = "Constant1",
                    SaveTimeseriesExternalId = "Constant1"
                },
            };

            var outputTimeSeries = new List<OutputTimeSeriesConfiguration>
            {
                new OutputTimeSeriesConfiguration
                {
                    Name = "OutputSeries1",
                    ExternalId = "OutputSeries1"
                },
            };

            var simulationConfig = new SimulationConfigurationWithRoutine
            {
                CalculationType = "UserDefined",
                InputConstants = inputConstants,
                OutputTimeSeries = outputTimeSeries,
            };
            this.simulationConfig = simulationConfig;
        }

        public SimulationConfigurationWithRoutine SetAndGetBasicRoutine()
        {
            var routine = new List<CalculationProcedure>
            {
                new CalculationProcedure
                {
                    Order = 1,
                    Steps = new List<CalculationProcedureStep>
                    {
                        new CalculationProcedureStep
                        {
                            Step = 1,
                            Type = "Get",
                            Arguments = new Dictionary<string, object>
                            {
                                { "type", "manual" },
                                { "objectName", "PRODUCED GAS" },
                                { "objectProperty", "Mass Flow" },
                                { "storeInLocalVariable", "val"}
                            }
                        },
                        new CalculationProcedureStep
                        {
                            Step = 2,
                            Type = "Set",
                            Arguments = new Dictionary<string, object>
                            {
                                { "type", "manual" },
                                { "objectName", "PRODUCED GAS" },
                                { "objectProperty", "Mass Flow" },
                                { "value", "val" },
                                { "substituteLocalVariableIn", "value"},
                                { "useLocalVariable", "val"}
                            }
                        },
                    }
                },
            };
            this.simulationConfig.Routine = routine;
            return this.simulationConfig;
        }

        public SimulationConfigurationWithRoutine SetAndGetLoopRoutine()
        {
            var routine = new List<CalculationProcedure>
            {
                new CalculationProcedure
                {
                    Order = 1,
                    Steps = new List<CalculationProcedureStep>
                    {
                        new CalculationProcedureStep
                        {
                            Step = 1,
                            Type = "Get",
                            Arguments = new Dictionary<string, object>
                            {
                                { "type", "userDefined" },
                                { "value", "1" },
                                { "storeInLocalVariable", "val"}
                            }
                        },
                        new CalculationProcedureStep
                        {
                            Step = 2,
                            Type = "Set",
                            Arguments = new Dictionary<string, object>
                            {
                                { "type", "manual" },
                                { "objectName", "PRODUCED GAS" },
                                { "objectProperty", "Mass Flow" },
                                { "value", "val" },
                                { "substituteLocalVariableIn", "value"},
                                { "useLocalVariable", "val"}
                            }
                        },
                        new CalculationProcedureStep
                        {
                            Step = 3,
                            Type = "Loop",
                            Arguments = new Dictionary<string, object>
                            {
                                { "type", "manual" },
                                { "timesToLoop", "4" },
                                { "loopIterator", "i" },
                                { "steps", new JArray
                                    {
                                        new JObject
                                        {
                                            { "step", 1 },
                                            { "type", "Get" },
                                            { "arguments", new JObject
                                                {
                                                    { "type", "userDefined" },
                                                    { "value", "200" },
                                                    { "storeInLocalVariable", "val"}
                                                }
                                            }
                                        },
                                        new JObject
                                        {
                                            { "step", 2 },
                                            { "type", "Set" },
                                            { "arguments", new JObject
                                                {
                                                    { "type", "manual" },
                                                    { "objectName", "PRODUCED GAS" },
                                                    { "objectProperty", "Mass Flow" },
                                                    { "value", "100i" },
                                                    { "substituteLocalVariableIn", "value"},
                                                    { "useLocalVariable", "i"}
                                                }
                                            }
                                        },
                                    }
                                    
                                },
                            }
                        },
                    }
                },
            };
            this.simulationConfig.Routine = routine;
            return this.simulationConfig;
        }

        public SimulationConfigurationWithRoutine SetAndGetConditionalRoutine()
        {
            var routine = new List<CalculationProcedure>
            {
                new CalculationProcedure
                {
                    Order = 1,
                    Steps = new List<CalculationProcedureStep>
                    {
                        new CalculationProcedureStep
                        {
                            Step = 1,
                            Type = "Get",
                            Arguments = new Dictionary<string, object>
                            {
                                { "type", "userDefined" },
                                { "value", "1" },
                                { "storeInLocalVariable", "val"}
                            }
                        },
                        new CalculationProcedureStep
                        {
                            Step = 2,
                            Type = "Set",
                            Arguments = new Dictionary<string, object>
                            {
                                { "type", "manual" },
                                { "objectName", "PRODUCED GAS" },
                                { "objectProperty", "Mass Flow" },
                                { "value", "val" },
                                { "substituteLocalVariableIn", "value"},
                                { "useLocalVariable", "val"}
                            }
                        },
                        new CalculationProcedureStep
                        {
                            Step = 3,
                            Type = "Contional",
                            Arguments = new Dictionary<string, object>
                            {
                                { "type", "manual" },
                                { "leftSide", "val"},
                                { "comparator" , "=="},
                                { "rightSide" , "1"},
                                { "ifSteps", new JArray
                                    {
                                        new JObject
                                        {
                                            { "step", 1 },
                                            { "type", "Get" },
                                            { "arguments", new JObject
                                                {
                                                    { "type", "userDefined" },
                                                    { "value", "200" },
                                                    { "storeInLocalVariable", "val"}
                                                }
                                            }
                                        },
                                        new JObject
                                        {
                                            { "step", 2 },
                                            { "type", "Set" },
                                            { "arguments", new JObject
                                                {
                                                    { "type", "manual" },
                                                    { "objectName", "PRODUCED GAS" },
                                                    { "objectProperty", "Mass Flow" },
                                                    { "value", "Local variable is = val" },
                                                    { "substituteLocalVariableIn", "value"},
                                                    { "useLocalVariable", "val"}
                                                }
                                            }
                                        },
                                    }
                                    
                                },
                            }
                        },
                    }
                },
            };
            this.simulationConfig.Routine = routine;
            return this.simulationConfig;
        }

        public SimulationConfigurationWithRoutine GetConfig() {
            return this.simulationConfig;
        }
    }

    public class RoutineRunnerTestCommon: RoutineImplementationBase {
        public RoutineRunnerTestCommon(SimulationConfigurationWithRoutine simulationConfig, Dictionary<string, double> arguments)
            :base(simulationConfig, arguments)
        {
        }

        public override string GetPropertyValue(Dictionary<string, string> args)
        {
            throw new NotImplementedException();
        }

        public override double GetTimeSeriesOutput(OutputTimeSeriesConfiguration outputConfig, Dictionary<string, string> arguments)
        {
            throw new NotImplementedException();
        }

        public override void RunCommand(string command, Dictionary<string, string> arguments)
        {
            throw new NotImplementedException();
        }

        public override void SetManualInput(string value, Dictionary<string, string> arguments)
        {
            throw new NotImplementedException();
        }

        public override void SetTimeSeriesInput(InputTimeSeriesConfiguration inputConfig, double value, Dictionary<string, string> arguments)
        {
            throw new NotImplementedException();
        }
    }

    public class RoutineRunnerTestBasic: RoutineRunnerTestCommon
    {
        private readonly ITestOutputHelper _output;
        public RoutineRunnerTestBasic(ITestOutputHelper output)
            :base(new RoutineBuilderTest().SetAndGetBasicRoutine(), null)
        {
            _output = output;
        }

        [Fact]
        protected void TriggerSimulation()
        {
            _output.WriteLine("Testing creation of variables in the global scope");
            Assert.False(this.IsLocalVariableDefined("val"));
            this.PerformSimulation();
            Assert.True(this.IsLocalVariableDefined("val"));
        }

        public override string GetPropertyValue(Dictionary<string, string> args)
        {
            return "mockedValueFromSimulator";
        }

        public override void SetManualInput(string value, Dictionary<string, string> arguments)
        {
            _output.WriteLine("Testing if the correct value is set from the local variable on the simulator");
            Assert.True(value == "mockedValueFromSimulator");
        }
    }

    public class RoutineRunnerTestLoop: RoutineRunnerTestCommon
    {
        private readonly ITestOutputHelper _output;
        private int functionCalls = 0;
        public RoutineRunnerTestLoop(ITestOutputHelper output)
            :base(new RoutineBuilderTest().SetAndGetLoopRoutine(), null)
        {
            _output = output;
        }

        [Fact]
        protected void TriggerSimulation()
        {
            Assert.False(this.IsLocalVariableDefined("val"));
            this.PerformSimulation();
            var val = this.GetLocalVariable("val");
            Assert.True(val.Value == "1");
            Assert.True(val.Accessor == "0--val");
            Assert.True(this.IsLocalVariableDefined("val"));

            _output.WriteLine($"Testing if iterator has been garbage collected");
            Assert.False(this.IsLocalVariableDefined("i"));

            _output.WriteLine($"Testing if did go to the Loop statement. ");
            // The value is 5 because the loop is executed 4 times, and the first call was made from outside the loop
            Assert.True(functionCalls == 5);
        }

        public override void SetManualInput(string value, Dictionary<string, string> arguments)
        {
            if (functionCalls == 0)  {
                _output.WriteLine("Testing created variables in the global scope");
                var val = this.GetLocalVariable("val");
                Assert.True(value == "1");
                Assert.True(val.DeclaredInLoopIteration == 0);
                functionCalls++;
            } else {
                var val = this.GetLocalVariable("val");
                _output.WriteLine($"Testing created variables in the loop scope: Variable value = {val.Value}");
                Assert.True(val.Scope == 1);
                Assert.True(val.DeclaredInLoopIteration == (functionCalls-1));
                Assert.True(val.Value == "200");

                _output.WriteLine($"Making sure that same named variables in different scopes get local values");
                Assert.True(val.Value != "1");

                var iterator = this.GetLocalVariable("i");
                _output.WriteLine($"Testing iterator: {iterator.Value}");
                Assert.True(iterator.Value == (functionCalls-1).ToString());
                functionCalls++;
            }
        }
    }

    public class RoutineRunnerConditionTest: RoutineRunnerTestCommon
    {
        private readonly ITestOutputHelper _output;
        private int functionCalls = 0;
        public RoutineRunnerConditionTest(ITestOutputHelper output)
            :base(new RoutineBuilderTest().SetAndGetLoopRoutine(), null)
        {
            _output = output;
        }

        [Fact]
        protected void TriggerSimulation()
        {
            this.PerformSimulation();
            _output.WriteLine($"Testing if did go to the If statement");
            Assert.True(functionCalls == 1);
        }

        public override void SetManualInput(string value, Dictionary<string, string> arguments)
        {
            if (functionCalls == 0)  {
                _output.WriteLine("Testing created variables in the global scope");
                Assert.True(value == "1");
                functionCalls++;
            } else {
                var val = this.GetLocalVariable("val");
                _output.WriteLine($"If we end up in the conditional, this part would be called");
                Assert.True(val.Scope == 1);
                Assert.True(val.Value == "Local variable is = 200");
                functionCalls++;
            }
        }
    }
}