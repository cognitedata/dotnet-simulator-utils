using CogniteSdk.Alpha;

namespace SampleConnector
{
    public static class SimulatorDefinition
    {
        public static SimulatorCreate Get()
        {
            return new SimulatorCreate()
            {
                ExternalId = "WEATHER-VIKRAM-2211-DEMO",
                Name = "WEATHER-VIKRAM-2211-DEMO",
                FileExtensionTypes = new List<string> { "weather", "json" },
                ModelTypes = new List<SimulatorModelType> {
                        new SimulatorModelType {
                            Name = "General",
                            Key = "General",
                        },
                    },
                StepFields = new List<SimulatorStepField> {
                        new SimulatorStepField {
                            StepType = "get",
                            Fields = new List<SimulatorStepFieldParam> {
                                new SimulatorStepFieldParam {
                                    Name = "variable",
                                    Label = "Variable",
                                    Info = "The variable to set, for example Humidity, Temperature, Windspeed",
                                },
                            },
                        },
                        new SimulatorStepField {
                            StepType = "set",
                            Fields = new List<SimulatorStepFieldParam> {
                                new SimulatorStepFieldParam {
                                    Name = "location",
                                    Label = "Location",
                                    Info = "The location to set, for example Oslo, Bengaluru",
                                },
                            },
                        },
                        new SimulatorStepField {
                            StepType = "command",
                            Fields = new List<SimulatorStepFieldParam> {
                                new SimulatorStepFieldParam {
                                    Name = "command",
                                    Label = "Command to perform",
                                    Info = "get weather data",
                                },
                            },
                        },
                    },
                UnitQuantities = new List<SimulatorUnitQuantity> {
                        new SimulatorUnitQuantity {
                        Name = "Temperature",
                        Label = "Temperature",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Label = "Celsius",
                                Name = "C",
                            },
                            new SimulatorUnitEntry {
                                Label = "Fahrenheit",
                                Name = "F",
                            },
                            new SimulatorUnitEntry {
                                Label = "Kelvin",
                                Name = "K",
                            },
                        },
                    },
                    new SimulatorUnitQuantity {
                        Name = "Pressure",
                        Label = "Atmospheric Pressure",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Label = "hPa",
                                Name = "hPa",
                            },
                            new SimulatorUnitEntry {
                                Label = "mbar",
                                Name = "mbar",
                            },
                            new SimulatorUnitEntry {
                                Label = "mmHg",
                                Name = "mmHg",
                            },
                        },
                    },
                    new SimulatorUnitQuantity {
                        Name = "Speed",
                        Label = "Wind Speed",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Label = "m/s",
                                Name = "m/s",
                            },
                            new SimulatorUnitEntry {
                                Label = "km/h",
                                Name = "km/h",
                            },
                            new SimulatorUnitEntry {
                                Label = "mph",
                                Name = "mph",
                            },
                        },
                    },
                }
            };
        }
    }
}