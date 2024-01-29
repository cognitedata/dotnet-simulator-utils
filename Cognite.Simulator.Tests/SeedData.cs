using System;
using System.Collections.Generic;
using CogniteSdk.Alpha;

namespace Cognite.Simulator.Tests {
    public class SeedData {
        public static SimulatorRoutineRevisionCreate SimulatorRoutineRevisionWithInputConstants = new SimulatorRoutineRevisionCreate() {
            Configuration = new SimulatorRoutineRevisionConfiguration() {
                Schedule = new SimulatorRoutineRevisionSchedule() {
                    Enabled = false,
                },
                DataSampling = new SimulatorRoutineRevisionDataSampling() {
                    ValidationWindow = 1440,
                    SamplingWindow = 60,
                    Granularity = 1,
                    ValidationEndOffset = "10m"
                },
                LogicalCheck = new SimulatorRoutineRevisionLogicalCheck() {
                    Enabled = true,
                    TimeseriesExternalId = "SimConnect-IntegrationTests-OnOffValues",
                    Aggregate = "stepInterpolation",
                    Operator = "eq",
                    Value = 1,
                },
                SteadyStateDetection = new SimulatorRoutineRevisionSteadyStateDetection() {
                    Enabled = true,
                    TimeseriesExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                    Aggregate = "average",
                    MinSectionSize = 60,
                    VarThreshold = 1.0,
                    SlopeThreshold = -3.0,
                },
                // TODO rename type to SimulatorRoutineRevisionInputConstant type
                InputConstants = new List<InputConstants>() {
                    new InputConstants() {
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-IC1-SampledSsd",
                        Value = "42",
                        Unit = "STB/MMscf",
                        UnitType = "LiqRate/GasRate",
                        Name = "Input Constant 1",
                        ReferenceId = "IC1",
                    },
                    new InputConstants() {
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-IC2-SampledSsd",
                        Value = "100",
                        Unit = "STB/MMscf",
                        UnitType = "LiqRate/GasRate",
                        Name = "Input Constant 2",
                        ReferenceId = "IC2",
                    },
                },
                OutputTimeseries = new List<SimulatorRoutineRevisionOutputTimeseries>() {
                    new SimulatorRoutineRevisionOutputTimeseries() {
                        Name = "Output Test 1",
                        ReferenceId = "OT1",
                        Unit = "STB/MMscf",
                        UnitType = "LiqRate/GasRate",
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-OT1-Output",
                    },
                },
                InputTimeseries = new List<SimulatorRoutineRevisionInputTimeseries>(),
            },
            ExternalId = $"IntegrationTests-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            RoutineExternalId = $"IntegrationTests-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            // TODO: Rename type to SimulatorRoutineRevisionScriptStage
            Script = new List<SimulatorRoutineRevisionStage> () {
                new SimulatorRoutineRevisionStage() {
                    Order = 1,
                    Description = "Set simulation inputs",
                    Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                        new SimulatorRoutineRevisionScriptStep() {
                            Order = 1,
                            StepType = "Set",
                            Arguments = new Dictionary<string, string>() {
                                { "argumentType", "inputConstant" },
                                { "referenceId", "IC1" },
                            },
                        },
                        new SimulatorRoutineRevisionScriptStep() {
                            Order = 1,
                            StepType = "Set",
                            Arguments = new Dictionary<string, string>() {
                                { "argumentType", "inputConstant" },
                                { "referenceId", "IC2" },
                            },
                        },
                    },
                },
            }
        };
    }
}