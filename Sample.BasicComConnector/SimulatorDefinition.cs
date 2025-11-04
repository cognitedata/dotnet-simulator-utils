using CogniteSdk.Alpha;

static class SimulatorDefinition
{
    public static SimulatorCreate Get()
    {
        return new SimulatorCreate()
        {
            ExternalId = "Excel",
            Name = "Excel",
            FileExtensionTypes = new List<string> { "xlsx" },
            ModelTypes = new List<SimulatorModelType> {
                    new SimulatorModelType {
                        Name = "Spreadsheet",
                        Key = "Spreadsheet",
                    }
                },
            StepFields = new List<SimulatorStepField> {
                new SimulatorStepField {
                    StepType = "get/set",
                    Fields = new List<SimulatorStepFieldParam> {
                        new SimulatorStepFieldParam {
                            Name = "sheet",
                            Label = "Sheet Name",
                            Info = "Name of the worksheet (e.g., 'Sheet1')",
                        },
                        new SimulatorStepFieldParam {
                            Name = "cell",
                            Label = "Cell Reference",
                            Info = "Excel cell reference (e.g., 'A1', 'B2', 'C3')",
                        },
                    },
                },
                new SimulatorStepField {
                    StepType = "command",
                    Fields = new List<SimulatorStepFieldParam> {
                        new SimulatorStepFieldParam {
                            Name = "command",
                            Label = "Command",
                            Info = "Select a command",
                            Options = new List<SimulatorStepFieldOption> {
                                new SimulatorStepFieldOption {
                                    Label = "Pause Calculations",
                                    Value = "Pause",
                                },
                                new SimulatorStepFieldOption {
                                    Label = "Perform Calculation",
                                    Value = "Calculate",
                                }
                            },
                        },
                    },
                },
            },
            UnitQuantities = new List<SimulatorUnitQuantity>() {
                new SimulatorUnitQuantity {
                    Name = "Unitless",
                    Label = "Unitless",
                    Units = new List<SimulatorUnitEntry> {
                        new SimulatorUnitEntry {
                            Name = "",
                            Label = "",
                        },
                    },
                },
            },
        };
    }
}
