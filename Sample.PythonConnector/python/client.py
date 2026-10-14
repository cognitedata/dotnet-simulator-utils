"""
MuJoCo Client Implementation for Cognite Simulator Integration

This module provides the client interface for MuJoCo physics simulation.
It handles:
- Connection testing (verifying MuJoCo is installed)
- Version reporting
- Model file loading and validation (MJCF XML and URDF files)

This is dynamically imported by PythonBridgeClient.cs
"""
# type: ignore
# MuJoCo types are resolved at runtime

import sys
import os
from typing import Any, Dict, Optional

# Lazy-load mujoco to avoid stack overflow when imported via pythonnet
# MuJoCo is a large native library that can exhaust .NET's default stack
_mujoco = None  # type: ignore


def _get_mujoco():
    """Lazy-load MuJoCo when first needed"""
    global _mujoco
    if _mujoco is None:
        try:
            import mujoco

            _mujoco = mujoco
        except ImportError:
            pass
    return _mujoco


class SimulatorClient:
    """
    Client for interacting with MuJoCo physics simulator
    """

    def __init__(self) -> None:
        """Initialize the MuJoCo simulator client"""
        self.model: Any = None
        self.model_path: Optional[str] = None
        # Don't load MuJoCo during construction - defer to first use
        # This avoids stack overflow when running via pythonnet

    def test_connection(self) -> None:
        """
        Test if MuJoCo is accessible and working

        Raises:
            Exception: If MuJoCo is not available or connection test fails
        """
        mujoco = _get_mujoco()
        if mujoco is None:
            raise Exception("MuJoCo is not installed. Install with: pip install mujoco")

        try:
            # Test MuJoCo by creating a minimal model
            test_xml = """
            <mujoco>
                <worldbody>
                    <body name="test_body">
                        <geom type="sphere" size="0.1"/>
                    </body>
                </worldbody>
            </mujoco>
            """
            test_model = mujoco.MjModel.from_xml_string(test_xml)
            test_data = mujoco.MjData(test_model)

            # Run one simulation step to verify everything works
            mujoco.mj_step(test_model, test_data)
        except Exception as e:
            raise Exception(f"MuJoCo connection test failed: {e}")

    def get_connector_version(self) -> str:
        """
        Get the connector version

        Returns:
            str: Connector version string
        """
        return "1.0.0-mujoco"

    def get_simulator_version(self) -> str:
        """
        Get the MuJoCo simulator version

        Returns:
            str: MuJoCo version string
        """
        mujoco = _get_mujoco()
        if mujoco is None:
            return "MuJoCo not installed"
        return f"MuJoCo {mujoco.__version__}"

    def open_model(self, file_path: str) -> Dict[str, Any]:
        """
        Open and validate a MuJoCo model file (MJCF XML or URDF)

        Args:
            file_path (str): Path to the model file

        Returns:
            dict: Result with 'success' (bool) and optional 'error' (str)
        """
        print(f"Opening MuJoCo model: {file_path}", file=sys.stderr)

        mujoco = _get_mujoco()
        if mujoco is None:
            return {"success": False, "error": "MuJoCo is not installed"}

        # Check if file exists
        if not os.path.exists(file_path):
            return {"success": False, "error": f"Model file not found: {file_path}"}

        # Check file extension
        ext = os.path.splitext(file_path)[1].lower()
        if ext not in [".xml", ".mjcf", ".urdf"]:
            return {
                "success": False,
                "error": f"Unsupported file type: {ext}. Supported types: .xml, .mjcf, .urdf",
            }

        try:
            # Load the model (MJCF and URDF both use from_xml_path)
            self.model = mujoco.MjModel.from_xml_path(file_path)
            self.model_path = file_path

            # Extract model info for logging
            n_bodies = self.model.nbody
            n_joints = self.model.njnt
            n_actuators = self.model.nu
            n_sensors = self.model.nsensor
            timestep = self.model.opt.timestep

            print(f"Model loaded successfully:", file=sys.stderr)
            print(f"  - Bodies: {n_bodies}", file=sys.stderr)
            print(f"  - Joints: {n_joints}", file=sys.stderr)
            print(f"  - Actuators: {n_actuators}", file=sys.stderr)
            print(f"  - Sensors: {n_sensors}", file=sys.stderr)
            print(f"  - Timestep: {timestep}s", file=sys.stderr)

            return {"success": True}

        except Exception as e:
            error_msg = str(e)
            print(f"Failed to load model: {error_msg}", file=sys.stderr)
            return {
                "success": False,
                "error": f"Failed to load MuJoCo model: {error_msg}",
            }

    def get_model_info(self) -> Optional[Dict[str, Any]]:
        """
        Get information about the currently loaded model

        Returns:
            dict: Model information or None if no model is loaded
        """
        if self.model is None:
            return None

        return {
            "path": self.model_path,
            "n_bodies": self.model.nbody,
            "n_joints": self.model.njnt,
            "n_actuators": self.model.nu,
            "n_sensors": self.model.nsensor,
            "n_geoms": self.model.ngeom,
            "timestep": self.model.opt.timestep,
            "gravity": list(self.model.opt.gravity),
        }
