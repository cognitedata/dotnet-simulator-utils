using CogniteSdk.Alpha;

namespace Sample.PythonConnector;

static class SimulatorDefinition
{
    public static SimulatorCreate Get()
    {
        return new SimulatorCreate()
        {
            ExternalId = "PythonSim",
            Name = "Python Simulator",
            FileExtensionTypes = new List<string> { "py", "json", "csv" },
            ModelTypes = new List<SimulatorModelType> {
                new SimulatorModelType {
                    Name = "Steady State",
                    Key = "SteadyState",
                },
                new SimulatorModelType {
                    Name = "Dynamic",
                    Key = "Dynamic",
                }
            },
            StepFields = new List<SimulatorStepField> {
                new SimulatorStepField {
                    StepType = "get/set",
                    Fields = new List<SimulatorStepFieldParam> {
                        new SimulatorStepFieldParam {
                            Name = "variable",
                            Label = "Variable Name",
                            Info = "Name of the variable in the Python script",
                        }
                    },
                },
                new SimulatorStepField {
                    StepType = "command",
                    Fields = new List<SimulatorStepFieldParam> {
                        new SimulatorStepFieldParam {
                            Name = "script",
                            Label = "Python Script",
                            Info = "Python code to execute",
                        }
                    },
                }
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
                        new SimulatorUnitEntry {
                            Name = "K",
                            Label = "Kelvin",
                        },
                    },
                },
                new SimulatorUnitQuantity {
                    Name = "Pressure",
                    Label = "Pressure",
                    Units = new List<SimulatorUnitEntry> {
                        new SimulatorUnitEntry {
                            Name = "bar",
                            Label = "Bar",
                        },
                        new SimulatorUnitEntry {
                            Name = "Pa",
                            Label = "Pascal",
                        },
                    },
                }
            },
        };
    }
}
