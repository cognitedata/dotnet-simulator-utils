using CogniteSdk.Alpha;

namespace SampleConnector{
    public static class SimulatorDefinition {
        public static SimulatorCreate Get() {
            return new SimulatorCreate()
                {
                    ExternalId = "CALCULATOR-DEMO",
                    Name = "CALCULATOR-DEMO",
                    FileExtensionTypes = new List<string> { "demo" },
                    ModelTypes = new List<SimulatorModelType> {
                        new SimulatorModelType {
                            Name = "General",
                            Key = "General",
                        },
                    },
                    StepFields = new List<SimulatorStepField> {
                        new SimulatorStepField {
                            StepType = "get/set",
                            Fields = new List<SimulatorStepFieldParam> {
                                new SimulatorStepFieldParam {
                                    Name = "address",
                                    Label = "Address",
                                    Info = "The address to set, for example SET A",
                                },
                            },
                        },
                        new SimulatorStepField {
                            StepType = "command",
                            Fields = new List<SimulatorStepFieldParam> {
                                new SimulatorStepFieldParam {
                                    Name = "command",
                                    Label = "Command to perform",
                                    Info = "plus, minus, multiply, divide",
                                },
                            },
                        },
                    },
                    UnitQuantities = new List<SimulatorUnitQuantity> {
                        new SimulatorUnitQuantity {
                            Name = "LiqRate/GasRate",
                            Label = "Liquid Gas Rate",
                            Units = new List<SimulatorUnitEntry> {
                                new SimulatorUnitEntry {
                                    Label = "STB/MMscf",
                                    Name = "STB/MMscf",
                                },
                                new SimulatorUnitEntry {
                                    Label = "Sm³/Sm³",
                                    Name = "Sm3/Sm3",
                                },
                                new SimulatorUnitEntry {
                                    Label = "m³/m³",
                                    Name = "m3/m3",
                                },
                                new SimulatorUnitEntry {
                                    Label = "m³/m³Vn",
                                    Name = "m3/m3Vn",
                                },
                                new SimulatorUnitEntry {
                                    Label = "STB/m³Vn",
                                    Name = "STB/m3Vn",
                                },
                                new SimulatorUnitEntry {
                                    Label = "Sm³/kSm³",
                                    Name = "Sm3/kSm3",
                                },
                                new SimulatorUnitEntry {
                                    Label = "Sm³/MSm³",
                                    Name = "Sm3/MSm3",
                                },
                            },
                        },
                    }
                };
        }
    }
}