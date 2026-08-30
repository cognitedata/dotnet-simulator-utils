"""
MuJoCo Simulator Definition for Cognite Simulator Integration

This module defines the MuJoCo physics simulator's metadata including:
- Simulator name and external ID
- Supported file types (MJCF XML, URDF)
- Model types (Robotics, Biomechanics, etc.)
- Step fields for routines
- Unit quantities for physics simulation

This definition is loaded by PythonSimulatorDefinitionBridge.cs at runtime.
"""


def get_simulator_definition():
    """
    Return the MuJoCo simulator definition as a dictionary

    Returns:
        dict: Simulator definition with all metadata
    """
    return {
        "external_id": "MuJoCo",
        "name": "MuJoCo Physics Simulator",
        "file_extension_types": ["xml", "mjcf", "urdf"],
        # Model types supported by MuJoCo
        "model_types": [
            {
                "name": "Robotics",
                "key": "Robotics",
            },
            {
                "name": "Biomechanics",
                "key": "Biomechanics",
            },
            {
                "name": "Soft Body",
                "key": "SoftBody",
            },
            {
                "name": "General Physics",
                "key": "GeneralPhysics",
            },
        ],
        # Step fields define the arguments for get/set/command steps in routines
        "step_fields": [
            {
                "step_type": "get/set",
                "fields": [
                    {
                        "name": "object_type",
                        "label": "Object Type",
                        "info": "Type of MuJoCo object (body, joint, actuator, sensor, geom, site)",
                    },
                    {
                        "name": "object_name",
                        "label": "Object Name",
                        "info": "Name of the object in the MuJoCo model",
                    },
                    {
                        "name": "property",
                        "label": "Property",
                        "info": "Property to get/set (pos, vel, ctrl, qpos, qvel, sensordata)",
                    },
                    {
                        "name": "index",
                        "label": "Index",
                        "info": "Optional index for array properties (0, 1, 2 for x, y, z)",
                    },
                ],
            },
            {
                "step_type": "command",
                "fields": [
                    {
                        "name": "command",
                        "label": "Command",
                        "info": "Command to execute (step, reset, forward, inverse)",
                    },
                    {
                        "name": "steps",
                        "label": "Number of Steps",
                        "info": "Number of simulation steps to run (for 'step' command)",
                    },
                ],
            },
        ],
        # Unit quantities for physics simulation
        "unit_quantities": [
            {
                "name": "Length",
                "label": "Length",
                "units": [
                    {"name": "m", "label": "Meters"},
                    {"name": "cm", "label": "Centimeters"},
                    {"name": "mm", "label": "Millimeters"},
                ],
            },
            {
                "name": "Mass",
                "label": "Mass",
                "units": [
                    {"name": "kg", "label": "Kilograms"},
                    {"name": "g", "label": "Grams"},
                ],
            },
            {
                "name": "Time",
                "label": "Time",
                "units": [
                    {"name": "s", "label": "Seconds"},
                    {"name": "ms", "label": "Milliseconds"},
                ],
            },
            {
                "name": "Force",
                "label": "Force",
                "units": [
                    {"name": "N", "label": "Newtons"},
                    {"name": "kN", "label": "Kilonewtons"},
                ],
            },
            {
                "name": "Torque",
                "label": "Torque",
                "units": [
                    {"name": "Nm", "label": "Newton-meters"},
                ],
            },
            {
                "name": "Angle",
                "label": "Angle",
                "units": [
                    {"name": "rad", "label": "Radians"},
                    {"name": "deg", "label": "Degrees"},
                ],
            },
            {
                "name": "Velocity",
                "label": "Velocity",
                "units": [
                    {"name": "m/s", "label": "Meters per second"},
                    {"name": "rad/s", "label": "Radians per second"},
                ],
            },
            {
                "name": "Acceleration",
                "label": "Acceleration",
                "units": [
                    {"name": "m/s2", "label": "Meters per second squared"},
                    {"name": "rad/s2", "label": "Radians per second squared"},
                ],
            },
        ],
    }
