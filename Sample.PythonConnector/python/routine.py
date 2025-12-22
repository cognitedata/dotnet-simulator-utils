"""
Example Python routine implementation for Cognite Simulator Utils

This module is dynamically imported by PythonBridgeRoutine.cs
Users should implement a class called SimulatorRoutine with the following methods:
- __init__(model_path): Initialize the routine with the model path
- set_input(arguments, value): Set an input value
- get_output(arguments): Get an output value
- run_command(arguments): Run a simulation command
"""

import json
import sys


class SimulatorRoutine:
    """
    Routine for performing simulations
    """

    def __init__(self, model_path):
        """
        Initialize the routine with the model path

        Args:
            model_path (str): Path to the model file
        """
        self.model_path = model_path
        self.variables = {}
        print(f"SimulatorRoutine initialized with model: {model_path}", file=sys.stderr)

    def set_input(self, arguments, value):
        """
        Set an input value for the simulation

        Args:
            arguments (dict): Dictionary containing step arguments (e.g., {"variable": "inlet_temp"})
            value: The value to set (can be float, str, int, etc.)
        """
        if "variable" not in arguments:
            raise ValueError("Missing required 'variable' key in arguments")

        variable_name = arguments["variable"]
        if not variable_name:
            raise ValueError("'variable' cannot be empty")

        self.variables[variable_name] = value
        print(f"Set variable '{variable_name}' = {value}", file=sys.stderr)

    def get_output(self, arguments):
        """
        Get an output value from the simulation

        Args:
            arguments (dict): Dictionary containing step arguments (e.g., {"variable": "outlet_temp"})

        Returns:
            The value of the requested variable
        """
        if "variable" not in arguments:
            raise ValueError("Missing required 'variable' key in arguments")

        variable_name = arguments["variable"]
        if not variable_name:
            raise ValueError("'variable' cannot be empty")

        if variable_name not in self.variables:
            raise ValueError(
                f"Variable '{variable_name}' not found in simulation results"
            )

        value = self.variables[variable_name]
        print(f"Get variable '{variable_name}' = {value}", file=sys.stderr)
        return value

    def run_command(self, arguments):
        """
        Run a simulation command

        Args:
            arguments (dict): Dictionary containing command arguments
                             (e.g., {"command": "solve"} or any custom arguments)
        """
        command = arguments.get("command", "simulate")
        print(f"Running command: {command}", file=sys.stderr)

        # Example: Run a simple heat exchanger simulation
        # This is where you would call your actual simulation logic
        results = self._run_simulation()

        # Update variables with simulation results
        self.variables.update(results)

        print(f"Command '{command}' completed. Results: {results}", file=sys.stderr)

    def _run_simulation(self):
        """
        Internal method to run the actual simulation
        This is an example implementation - users should replace with their own logic

        Returns:
            dict: Simulation results
        """
        # Get input parameters with defaults
        inlet_temp = self.variables.get("inlet_temp", 20.0)
        flow_rate = self.variables.get("flow_rate", 1.0)
        heat_capacity = self.variables.get(
            "heat_capacity", 4186.0
        )  # Water at 20°C (J/kg·K)
        heat_transfer = self.variables.get("heat_transfer", 10000.0)  # Watts

        # Validation
        if flow_rate <= 0:
            raise ValueError("Flow rate must be positive")
        if heat_capacity <= 0:
            raise ValueError("Heat capacity must be positive")

        # Calculate outlet temperature
        # Q = m * Cp * ΔT
        # ΔT = Q / (m * Cp)
        delta_t = heat_transfer / (flow_rate * heat_capacity)
        outlet_temp = inlet_temp + delta_t

        # Calculate heat duty in kW
        heat_duty = heat_transfer / 1000.0

        # Calculate efficiency (simplified model: assumes 95% efficiency)
        max_possible_temp_rise = heat_transfer / (flow_rate * heat_capacity) / 0.95
        efficiency = (
            (delta_t / max_possible_temp_rise) * 100
            if max_possible_temp_rise > 0
            else 0
        )

        # Determine status based on output temperature
        if outlet_temp > 100:
            status = "warning_high_temperature"
        elif outlet_temp < inlet_temp:
            status = "error_cooling_detected"
        else:
            status = "converged"

        return {
            "outlet_temp": round(outlet_temp, 2),
            "heat_duty": round(heat_duty, 2),
            "temperature_rise": round(delta_t, 2),
            "efficiency": round(efficiency, 2),
            "status": status,
        }
