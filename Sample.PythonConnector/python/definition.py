"""
Simulator definition for Cognite Simulator Utils

This module defines the simulator's metadata including:
- Simulator name and external ID
- Supported file types
- Model types
- Step fields for routines
- Unit quantities and conversions

This definition is loaded by SimulatorDefinition.cs at runtime.
"""


def get_simulator_definition():
    """
    Return the simulator definition as a dictionary

    Returns:
        dict: Simulator definition with all metadata
    """
    return {
        "external_id": "PythonSim",
        "name": "Python Simulator",
        "file_extension_types": ["py", "json", "csv"],
        # Model types supported by this simulator
        "model_types": [
            {
                "name": "Steady State",
                "key": "SteadyState",
            },
            {
                "name": "Dynamic",
                "key": "Dynamic",
            },
        ],
        # Step fields define the arguments for get/set/command steps in routines
        "step_fields": [
            {
                "step_type": "get/set",
                "fields": [
                    {
                        "name": "variable",
                        "label": "Variable Name",
                        "info": "Name of the variable in the Python script",
                    }
                ],
            },
            {
                "step_type": "command",
                "fields": [
                    {
                        "name": "command",
                        "label": "Command",
                        "info": "Command to execute (e.g., 'solve', 'simulate')",
                    }
                ],
            },
        ],
        # Unit quantities and their conversions
        "unit_quantities": [
            {
                "name": "Temperature",
                "label": "Temperature",
                "units": [
                    {"name": "C", "label": "Celsius"},
                    {"name": "K", "label": "Kelvin"},
                    {"name": "F", "label": "Fahrenheit"},
                ],
            },
            {
                "name": "Pressure",
                "label": "Pressure",
                "units": [
                    {"name": "bar", "label": "Bar"},
                    {"name": "Pa", "label": "Pascal"},
                    {"name": "psi", "label": "PSI"},
                ],
            },
            {
                "name": "MassFlowRate",
                "label": "Mass Flow Rate",
                "units": [
                    {"name": "kg/s", "label": "Kilograms per second"},
                    {"name": "lb/h", "label": "Pounds per hour"},
                ],
            },
            {
                "name": "Power",
                "label": "Power",
                "units": [
                    {"name": "W", "label": "Watts"},
                    {"name": "kW", "label": "Kilowatts"},
                    {"name": "MW", "label": "Megawatts"},
                ],
            },
        ],
    }
