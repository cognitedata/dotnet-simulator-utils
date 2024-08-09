using CogniteSdk.Alpha;

static class SimulatorDefinition {
    public static SimulatorCreate Get() {
        return new SimulatorCreate()
            {
                ExternalId = "NewSim",
                Name = "NewSim",
                FileExtensionTypes = new List<string> { "xlsx" },
                ModelTypes = new List<SimulatorModelType> {
                    new SimulatorModelType {
                        Name = "Steady State",
                        Key = "SteadyState",
                    }
                },
                StepFields = new List<SimulatorStepField> {
                    new SimulatorStepField {
                        StepType = "get/set",
                        Fields = new List<SimulatorStepFieldParam> {
                            new SimulatorStepFieldParam {
                                Name = "address",
                                Label = "Address",
                                Info = "Enter the address to set",
                            },
                        },
                    },
                    new SimulatorStepField {
                        StepType = "command",
                        Fields = new List<SimulatorStepFieldParam> {
                            new SimulatorStepFieldParam {
                                Name = "command",
                                Label = "Command",
                                Info = "Enter the command to run",
                            },
                        },
                    },
                },
                UnitQuantities = new List<SimulatorUnitQuantity>() {
                    new SimulatorUnitQuantity {
                        Name = "Temperature",
                        Label = "Temperature",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Name = "C",
                                Label = "Celsius",
                            },
                        },
                    },
                },
            };
    }
}
