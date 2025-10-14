using CogniteSdk.Alpha;

/// <summary>
/// PlaceiT Excel COM Simulator Definition
/// 
/// Expected ZIP Package Structure:
/// - 1x .xlsx or .xlsm file (Excel simulator with PlaceiTCOMEntryPoint function)
/// - 2x .dll files (PlaceiT COM extensions)  
/// - 1x .sif file (PlaceiT configuration)
/// - 1x .exe file (PlaceiT application - optional)
/// </summary>
static class SimulatorDefinition
{
    public static SimulatorCreate Get()
    {
        return new SimulatorCreate()
        {
            ExternalId = "PlaceiTExcelCOM_v2",
            Name = "PlaceiT Excel COM Connector (ZIP Package)",
            FileExtensionTypes = new List<string> { "zip" },
            ModelTypes = new List<SimulatorModelType> { 
                    new SimulatorModelType {
                        Name = "PlaceiT COM-Enabled Spreadsheet Package",
                        Key = "PlaceiTComSpreadsheetPackage",
                    }
                },
            StepFields = new List<SimulatorStepField> {
                    new SimulatorStepField {
                        StepType = "placeit-simulation",
                        Fields = new List<SimulatorStepFieldParam> {
                            new SimulatorStepFieldParam {
                                Name = "porosity",
                                Label = "Zone1 Porosity",
                                Info = "Zone1 porosity (dimensionless fraction)",
                            },
                            new SimulatorStepFieldParam {
                                Name = "zonePressure",
                                Label = "Zone1 Pressure",
                                Info = "Zone1 pressure in psi",
                            },
                            new SimulatorStepFieldParam {
                                Name = "zoneLength",
                                Label = "Zone1 Length",
                                Info = "Zone1 length in meters",
                            },
                            new SimulatorStepFieldParam {
                                Name = "isothermOption",
                                Label = "Isotherm Type",
                                Info = "Isotherm type: 0 = Freundlich, 1 = Langmuir",
                                Options = new List<SimulatorStepFieldOption> {
                                    new SimulatorStepFieldOption {
                                        Label = "Freundlich",
                                        Value = "0",
                                    },
                                    new SimulatorStepFieldOption {
                                        Label = "Langmuir",
                                        Value = "1",
                                    }
                                },
                            },
                            new SimulatorStepFieldParam {
                                Name = "isothermValues",
                                Label = "Isotherm Parameters",
                                Info = "Array of 2 values: For Freundlich [n, k], For Langmuir [a, b]",
                            },
                            new SimulatorStepFieldParam {
                                Name = "adsorptionCapOption",
                                Label = "Adsorption Capacity",
                                Info = "Adsorption capacity: 1 = enabled, 0 = disabled",
                                Options = new List<SimulatorStepFieldOption> {
                                    new SimulatorStepFieldOption {
                                        Label = "Disabled",
                                        Value = "0",
                                    },
                                    new SimulatorStepFieldOption {
                                        Label = "Enabled",
                                        Value = "1",
                                    }
                                },
                            },
                            new SimulatorStepFieldParam {
                                Name = "adsorptionCapValue",
                                Label = "Adsorption Value",
                                Info = "Adsorption value in mg/L",
                            },
                            new SimulatorStepFieldParam {
                                Name = "enabledStages",
                                Label = "Stage Enable Flags",
                                Info = "Array of 12 stage flags: 1 = enabled, 0 = disabled (12 stages supported)",
                            },
                            new SimulatorStepFieldParam {
                                Name = "stagesVol",
                                Label = "Stage Volumes",
                                Info = "Array of 12 values: Volume of fluid injected per enabled stage in m³",
                            },
                            new SimulatorStepFieldParam {
                                Name = "stagesConc",
                                Label = "Stage Concentrations",
                                Info = "Array of 12 values: Concentration of scale inhibitor in injected fluid for each stage in wt%",
                            },
                            new SimulatorStepFieldParam {
                                Name = "injectionRate",
                                Label = "Injection Rate",
                                Info = "Injection rate in m³/hr",
                            },
                            new SimulatorStepFieldParam {
                                Name = "timestep",
                                Label = "Time Step Size",
                                Info = "Time step size for calculation resolution",
                            },
                            new SimulatorStepFieldParam {
                                Name = "productionTime",
                                Label = "Production Duration",
                                Info = "Post-injection production duration in days",
                            },
                            new SimulatorStepFieldParam {
                                Name = "productionRate",
                                Label = "Production Rate",
                                Info = "Production rate in m³/day",
                            },
                            new SimulatorStepFieldParam {
                                Name = "medRange",
                                Label = "Minimum Effective Dose Range",
                                Info = "Array of 2 values: [lower bound, upper bound] of minimum effective dose",
                            },
                        },
                    },
                    new SimulatorStepField {
                        StepType = "com-control",
                        Fields = new List<SimulatorStepFieldParam> {
                            new SimulatorStepFieldParam {
                                Name = "action",
                                Label = "COM Action",
                                Info = "Control action for COM object",
                                Options = new List<SimulatorStepFieldOption> {
                                    new SimulatorStepFieldOption {
                                        Label = "Initialize COM Object",
                                        Value = "Initialize",
                                    },
                                    new SimulatorStepFieldOption {
                                        Label = "Release COM Object",
                                        Value = "Release",
                                    },
                                    new SimulatorStepFieldOption {
                                        Label = "Reset Simulation",
                                        Value = "Reset",
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
                    new SimulatorUnitQuantity {
                        Name = "Pressure",
                        Label = "Pressure",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Name = "psi",
                                Label = "PSI",
                            },
                            new SimulatorUnitEntry {
                                Name = "bar",
                                Label = "Bar",
                            },
                            new SimulatorUnitEntry {
                                Name = "Pa",
                                Label = "Pascal",
                            },
                            new SimulatorUnitEntry {
                                Name = "kPa",
                                Label = "Kilopascal",
                            },
                        },
                    },
                    new SimulatorUnitQuantity {
                        Name = "Length",
                        Label = "Length",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Name = "m",
                                Label = "Meters",
                            },
                            new SimulatorUnitEntry {
                                Name = "ft",
                                Label = "Feet",
                            },
                            new SimulatorUnitEntry {
                                Name = "km",
                                Label = "Kilometers",
                            },
                        },
                    },
                    new SimulatorUnitQuantity {
                        Name = "VolumeRate",
                        Label = "Volume Rate",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Name = "m3/hr",
                                Label = "Cubic Meters per Hour",
                            },
                            new SimulatorUnitEntry {
                                Name = "m3/day",
                                Label = "Cubic Meters per Day",
                            },
                            new SimulatorUnitEntry {
                                Name = "bbl/day",
                                Label = "Barrels per Day",
                            },
                            new SimulatorUnitEntry {
                                Name = "L/min",
                                Label = "Liters per Minute",
                            },
                        },
                    },
                    new SimulatorUnitQuantity {
                        Name = "Time",
                        Label = "Time",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Name = "days",
                                Label = "Days",
                            },
                            new SimulatorUnitEntry {
                                Name = "hours",
                                Label = "Hours",
                            },
                            new SimulatorUnitEntry {
                                Name = "minutes",
                                Label = "Minutes",
                            },
                        },
                    },
                    new SimulatorUnitQuantity {
                        Name = "Volume",
                        Label = "Volume",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Name = "m3",
                                Label = "Cubic Meters",
                            },
                            new SimulatorUnitEntry {
                                Name = "L",
                                Label = "Liters",
                            },
                            new SimulatorUnitEntry {
                                Name = "bbl",
                                Label = "Barrels",
                            },
                        },
                    },
                    new SimulatorUnitQuantity {
                        Name = "Concentration",
                        Label = "Concentration",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Name = "mg/L",
                                Label = "Milligrams per Liter",
                            },
                            new SimulatorUnitEntry {
                                Name = "wt%",
                                Label = "Weight Percent",
                            },
                            new SimulatorUnitEntry {
                                Name = "ppm",
                                Label = "Parts per Million",
                            },
                            new SimulatorUnitEntry {
                                Name = "g/L",
                                Label = "Grams per Liter",
                            },
                        },
                    },
                    new SimulatorUnitQuantity {
                        Name = "Fraction",
                        Label = "Fraction",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Name = "fraction",
                                Label = "Fraction (0-1)",
                            },
                            new SimulatorUnitEntry {
                                Name = "percent",
                                Label = "Percent (0-100)",
                            },
                        },
                    },
                },
        };
    }
}
