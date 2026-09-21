using System.Collections.Generic;
using System.Threading;

using Cognite.Simulator.Utils;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public class RoutineImplementationBaseTest
    {
        private class TestRoutine : RoutineImplementationBase
        {
            private readonly SimulatorValueItem? _output;

            public TestRoutine(SimulatorRoutineRevision config, SimulatorValueItem? output, ILogger logger)
                : base(config, new Dictionary<string, SimulatorValueItem>(), logger)
            {
                _output = output;
            }

            public override SimulatorValueItem? GetOutput(
                SimulatorRoutineRevisionOutput outputConfig,
                Dictionary<string, string> arguments,
                CancellationToken token)
            {
                return _output;
            }

            public override void RunCommand(Dictionary<string, string> arguments, CancellationToken token)
            {
            }

            public override void SetInput(
                SimulatorRoutineRevisionInput inputConfig,
                SimulatorValueItem input,
                Dictionary<string, string> arguments,
                CancellationToken token)
            {
            }
        }

        private static SimulatorRoutineRevision BuildRoutineRevision() => new SimulatorRoutineRevision
        {
            ExternalId = "UnitTest-1",
            Configuration = new SimulatorRoutineRevisionConfiguration()
            {
                Outputs = new List<SimulatorRoutineRevisionOutput>() {
                    new SimulatorRoutineRevisionOutput() {
                        Name = "Output 1",
                        ReferenceId = "OC1",
                        ValueType = SimulatorValueType.DOUBLE,
                    },
                },
            },
            Script = new List<SimulatorRoutineRevisionScriptStage>() {
                new SimulatorRoutineRevisionScriptStage() {
                    Order = 1,
                    Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                        new SimulatorRoutineRevisionScriptStep() {
                            Order = 1,
                            StepType = "Get",
                            Arguments = new Dictionary<string, string>() {
                                { "referenceId", "OC1" },
                            },
                        }
                    },
                }
            }
        };

        [Fact]
        // A connector implementation signals an undefined/unrepresentable output by returning null
        // from GetOutput. PerformSimulation must skip that output rather than including a null value.
        public void PerformSimulationSkipsOutputWhenGetOutputReturnsNull()
        {
            var routine = new TestRoutine(BuildRoutineRevision(), null, new Mock<ILogger>().Object);

            var result = routine.PerformSimulation(CancellationToken.None);

            Assert.False(result.ContainsKey("OC1"));
        }

        [Fact]
        public void PerformSimulationKeepsOutputWhenGetOutputReturnsValue()
        {
            var output = new SimulatorValueItem
            {
                ReferenceId = "OC1",
                ValueType = SimulatorValueType.DOUBLE,
                Value = new SimulatorValue.Double(1.23),
            };
            var routine = new TestRoutine(BuildRoutineRevision(), output, new Mock<ILogger>().Object);

            var result = routine.PerformSimulation(CancellationToken.None);

            Assert.True(result.ContainsKey("OC1"));
            Assert.Equal(1.23, ((SimulatorValue.Double)result["OC1"].Value).Value);
        }
    }
}
