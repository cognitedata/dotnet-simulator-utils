"""
Example Python client implementation for Cognite Simulator Utils

This module is dynamically imported by PythonBridgeClient.cs
Users should implement a class called SimulatorClient with the following methods:
- test_connection(): Test if the simulator is accessible
- get_connector_version(): Return the connector version string
- get_simulator_version(): Return the simulator version string
- open_model(file_path): Open and validate a model file
"""

import sys
import os


class SimulatorClient:
    """
    Client for interacting with the Python-based simulator
    """

    def __init__(self):
        """Initialize the simulator client"""
        self.model_path = None
        print("SimulatorClient initialized", file=sys.stderr)

    def test_connection(self):
        """
        Test if the simulator is accessible

        Raises:
            Exception: If the connection test fails
        """
        # Example: Check if Python is working
        try:
            python_version = f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
            print(
                f"Python connection test successful. Version: {python_version}",
                file=sys.stderr,
            )
        except Exception as e:
            raise Exception(f"Connection test failed: {e}")

    def get_connector_version(self):
        """
        Get the connector version

        Returns:
            str: Connector version string
        """
        return "1.0.0"

    def get_simulator_version(self):
        """
        Get the simulator version

        Returns:
            str: Simulator version string (e.g., Python version)
        """
        return f"Python {sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"

    def open_model(self, file_path):
        """
        Open and validate a model file

        Args:
            file_path (str): Path to the model file

        Returns:
            dict: Result with 'success' (bool) and optional 'error' (str)
        """
        print(f"Opening model: {file_path}", file=sys.stderr)

        # Check if file exists
        if not os.path.exists(file_path):
            return {"success": False, "error": f"Model file not found: {file_path}"}

        # Validate the file (example: check if it's a .py file)
        if file_path.endswith(".py"):
            try:
                # Try to compile the Python file to check syntax
                with open(file_path, "r") as f:
                    code = f.read()
                compile(code, file_path, "exec")

                self.model_path = file_path
                print(f"Model validated successfully: {file_path}", file=sys.stderr)
                return {"success": True}
            except SyntaxError as e:
                return {"success": False, "error": f"Syntax error in model file: {e}"}
        else:
            # For non-Python files, just check if they exist
            self.model_path = file_path
            print(f"Model file accepted: {file_path}", file=sys.stderr)
            return {"success": True}
