#!/usr/bin/env python3
"""
Example Python simulation script for Cognite Simulator Utils
This script simulates a heat exchanger with basic thermodynamic calculations.

Usage: python simulate.py <input_json> <output_json>
"""

import sys
import json
import math


def main():
    """Main entry point for the simulation script"""
    if len(sys.argv) != 3:
        print("Usage: python simulate.py <input_json> <output_json>", file=sys.stderr)
        sys.exit(1)

    input_file = sys.argv[1]
    output_file = sys.argv[2]

    # Read input variables from JSON file
    try:
        with open(input_file, "r") as f:
            inputs = json.load(f)
    except FileNotFoundError:
        print(f"Error: Input file '{input_file}' not found", file=sys.stderr)
        sys.exit(1)
    except json.JSONDecodeError as e:
        print(f"Error: Invalid JSON in input file: {e}", file=sys.stderr)
        sys.exit(1)

    print(f"Received inputs: {inputs}")

    # Perform simulation calculations
    try:
        results = perform_simulation(inputs)
    except Exception as e:
        print(f"Error during simulation: {e}", file=sys.stderr)
        sys.exit(1)

    # Write output variables to JSON file
    try:
        with open(output_file, "w") as f:
            json.dump(results, f, indent=2)
    except Exception as e:
        print(f"Error writing output file: {e}", file=sys.stderr)
        sys.exit(1)

    print(f"Simulation complete. Results: {results}")


def perform_simulation(inputs):
    """
    Perform heat exchanger simulation

    Inputs:
        - inlet_temp: Inlet temperature (°C)
        - flow_rate: Flow rate (kg/s)
        - heat_capacity: Specific heat capacity (J/kg·K)
        - heat_transfer: Heat transfer rate (W)

    Outputs:
        - outlet_temp: Outlet temperature (°C)
        - heat_duty: Total heat duty (kW)
        - temperature_rise: Temperature increase (°C)
        - efficiency: Heat transfer efficiency (%)
        - status: Simulation status
    """
    # Extract input parameters with defaults
    inlet_temp = float(inputs.get("inlet_temp", 20.0))
    flow_rate = float(inputs.get("flow_rate", 1.0))
    heat_capacity = float(inputs.get("heat_capacity", 4186.0))  # Water at 20°C
    heat_transfer = float(inputs.get("heat_transfer", 10000.0))

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
        (delta_t / max_possible_temp_rise) * 100 if max_possible_temp_rise > 0 else 0
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


if __name__ == "__main__":
    main()
