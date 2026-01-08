"""
MuJoCo Routine Implementation for Cognite Simulator Integration

This module provides the routine interface for running MuJoCo physics simulations.
It handles:
- Setting inputs (actuator controls, body positions, joint states)
- Getting outputs (sensor data, body states, joint positions/velocities)
- Running simulation commands (step, reset, forward kinematics, inverse dynamics)

This is dynamically imported by PythonBridgeRoutine.cs
"""
# type: ignore
# MuJoCo types are resolved at runtime

import sys
from typing import Any, Dict, Optional

try:
    import mujoco  # type: ignore
    import numpy as np

    MUJOCO_AVAILABLE = True
except ImportError:
    MUJOCO_AVAILABLE = False
    mujoco = None  # type: ignore
    np = None  # type: ignore


class SimulatorRoutine:
    """
    Routine for performing MuJoCo physics simulations
    """

    def __init__(self, model_path: str) -> None:
        """
        Initialize the routine with a MuJoCo model

        Args:
            model_path (str): Path to the MuJoCo model file (MJCF XML or URDF)
        """
        self.model_path = model_path
        self.model: Any = None
        self.data: Any = None
        self.simulation_time: float = 0.0
        self.step_count: int = 0

        if not MUJOCO_AVAILABLE:
            raise RuntimeError(
                "MuJoCo is not installed. Install with: pip install mujoco"
            )

        # Load the model
        try:
            self.model = mujoco.MjModel.from_xml_path(model_path)
            self.data = mujoco.MjData(self.model)
            print(
                f"SimulatorRoutine initialized with MuJoCo model: {model_path}",
                file=sys.stderr,
            )
            print(f"  - Timestep: {self.model.opt.timestep}s", file=sys.stderr)
            print(
                f"  - Bodies: {self.model.nbody}, Joints: {self.model.njnt}",
                file=sys.stderr,
            )
            print(
                f"  - Actuators: {self.model.nu}, Sensors: {self.model.nsensor}",
                file=sys.stderr,
            )
        except Exception as e:
            raise RuntimeError(f"Failed to load MuJoCo model: {e}")

    def set_input(self, arguments: Dict[str, str], value: Any) -> None:
        """
        Set an input value for the simulation

        Args:
            arguments (dict): Dictionary containing step arguments:
                - object_type: Type of object (actuator, joint, body, qpos, qvel)
                - object_name: Name of the object (optional for qpos/qvel)
                - property: Property to set (ctrl, pos, vel, etc.)
                - index: Optional index for array properties
            value: The value to set (float or array)
        """
        object_type = arguments.get("object_type", "actuator").lower()
        object_name = arguments.get("object_name", "")
        prop = arguments.get("property", "ctrl").lower()
        index_str = arguments.get("index", "")

        try:
            index = int(index_str) if index_str else None
        except ValueError:
            index = None

        print(
            f"Setting {object_type}.{object_name}.{prop}[{index}] = {value}",
            file=sys.stderr,
        )

        try:
            if object_type == "actuator":
                self._set_actuator(object_name, prop, value, index)
            elif object_type == "joint":
                self._set_joint(object_name, prop, value, index)
            elif object_type == "body":
                self._set_body(object_name, prop, value, index)
            elif object_type == "qpos":
                self._set_qpos(value, index)
            elif object_type == "qvel":
                self._set_qvel(value, index)
            else:
                raise ValueError(f"Unknown object type: {object_type}")
        except Exception as e:
            raise ValueError(f"Failed to set input: {e}")

    def get_output(self, arguments: Dict[str, str]) -> Any:
        """
        Get an output value from the simulation

        Args:
            arguments (dict): Dictionary containing step arguments:
                - object_type: Type of object (sensor, body, joint, qpos, qvel, time)
                - object_name: Name of the object
                - property: Property to get (sensordata, pos, vel, etc.)
                - index: Optional index for array properties

        Returns:
            The value of the requested property
        """
        object_type = arguments.get("object_type", "sensor").lower()
        object_name = arguments.get("object_name", "")
        prop = arguments.get("property", "sensordata").lower()
        index_str = arguments.get("index", "")

        try:
            index = int(index_str) if index_str else None
        except ValueError:
            index = None

        print(f"Getting {object_type}.{object_name}.{prop}[{index}]", file=sys.stderr)

        try:
            if object_type == "sensor":
                return self._get_sensor(object_name, index)
            elif object_type == "body":
                return self._get_body(object_name, prop, index)
            elif object_type == "joint":
                return self._get_joint(object_name, prop, index)
            elif object_type == "qpos":
                return self._get_qpos(index)
            elif object_type == "qvel":
                return self._get_qvel(index)
            elif object_type == "time":
                return self.data.time
            elif object_type == "energy":
                return self._get_energy(prop)
            else:
                raise ValueError(f"Unknown object type: {object_type}")
        except Exception as e:
            raise ValueError(f"Failed to get output: {e}")

    def run_command(self, arguments: Dict[str, str]) -> None:
        """
        Run a simulation command

        Args:
            arguments (dict): Dictionary containing command arguments:
                - command: Command to run (step, reset, forward, inverse)
                - steps: Number of steps for 'step' command (default: 1)
        """
        command = arguments.get("command", "step").lower()
        steps_str = arguments.get("steps", "1")

        try:
            steps = int(steps_str) if steps_str else 1
        except ValueError:
            steps = 1

        print(f"Running command: {command} (steps={steps})", file=sys.stderr)

        if command == "step":
            self._step(steps)
        elif command == "reset":
            self._reset()
        elif command == "forward":
            self._forward()
        elif command == "inverse":
            self._inverse()
        else:
            raise ValueError(f"Unknown command: {command}")

        print(
            f"Command '{command}' completed. Time: {self.data.time:.4f}s",
            file=sys.stderr,
        )

    # ==================== Private Methods ====================

    def _set_actuator(
        self, name: str, prop: str, value: Any, index: Optional[int]
    ) -> None:
        """Set actuator control value"""
        if prop == "ctrl":
            if name:
                actuator_id = mujoco.mj_name2id(
                    self.model, mujoco.mjtObj.mjOBJ_ACTUATOR, name
                )
                if actuator_id < 0:
                    raise ValueError(f"Actuator not found: {name}")
                self.data.ctrl[actuator_id] = float(value)
            elif index is not None:
                self.data.ctrl[index] = float(value)
            else:
                raise ValueError("Must specify actuator name or index")
        else:
            raise ValueError(f"Unknown actuator property: {prop}")

    def _set_joint(
        self, name: str, prop: str, value: Any, index: Optional[int]
    ) -> None:
        """Set joint state"""
        joint_id = mujoco.mj_name2id(self.model, mujoco.mjtObj.mjOBJ_JOINT, name)
        if joint_id < 0:
            raise ValueError(f"Joint not found: {name}")

        qpos_adr = self.model.jnt_qposadr[joint_id]
        qvel_adr = self.model.jnt_dofadr[joint_id]

        if prop == "qpos" or prop == "pos":
            if index is not None:
                self.data.qpos[qpos_adr + index] = float(value)
            else:
                self.data.qpos[qpos_adr] = float(value)
        elif prop == "qvel" or prop == "vel":
            if index is not None:
                self.data.qvel[qvel_adr + index] = float(value)
            else:
                self.data.qvel[qvel_adr] = float(value)
        else:
            raise ValueError(f"Unknown joint property: {prop}")

    def _set_body(self, name: str, prop: str, value: Any, index: Optional[int]) -> None:
        """Set body state (requires mocap body)"""
        body_id = mujoco.mj_name2id(self.model, mujoco.mjtObj.mjOBJ_BODY, name)
        if body_id < 0:
            raise ValueError(f"Body not found: {name}")

        # Check if it's a mocap body
        mocap_id = self.model.body_mocapid[body_id]
        if mocap_id < 0:
            raise ValueError(
                f"Body '{name}' is not a mocap body. Use joint properties instead."
            )

        if prop == "pos":
            if index is not None:
                self.data.mocap_pos[mocap_id, index] = float(value)
            else:
                self.data.mocap_pos[mocap_id, :] = value
        elif prop == "quat":
            if index is not None:
                self.data.mocap_quat[mocap_id, index] = float(value)
            else:
                self.data.mocap_quat[mocap_id, :] = value
        else:
            raise ValueError(f"Unknown mocap body property: {prop}")

    def _set_qpos(self, value: Any, index: Optional[int]) -> None:
        """Set generalized position directly"""
        if index is not None:
            self.data.qpos[index] = float(value)
        else:
            self.data.qpos[:] = value

    def _set_qvel(self, value: Any, index: Optional[int]) -> None:
        """Set generalized velocity directly"""
        if index is not None:
            self.data.qvel[index] = float(value)
        else:
            self.data.qvel[:] = value

    def _get_sensor(self, name: str, index: Optional[int]) -> float:
        """Get sensor reading"""
        sensor_id = mujoco.mj_name2id(self.model, mujoco.mjtObj.mjOBJ_SENSOR, name)
        if sensor_id < 0:
            raise ValueError(f"Sensor not found: {name}")

        sensor_adr = self.model.sensor_adr[sensor_id]
        sensor_dim = self.model.sensor_dim[sensor_id]

        if index is not None:
            if index >= sensor_dim:
                raise ValueError(
                    f"Sensor index {index} out of range (dim={sensor_dim})"
                )
            return float(self.data.sensordata[sensor_adr + index])
        elif sensor_dim == 1:
            return float(self.data.sensordata[sensor_adr])
        else:
            # Return first element for multi-dimensional sensors
            return float(self.data.sensordata[sensor_adr])

    def _get_body(self, name: str, prop: str, index: Optional[int]) -> float:
        """Get body state"""
        body_id = mujoco.mj_name2id(self.model, mujoco.mjtObj.mjOBJ_BODY, name)
        if body_id < 0:
            raise ValueError(f"Body not found: {name}")

        if prop == "pos" or prop == "xpos":
            if index is not None:
                return float(self.data.xpos[body_id, index])
            return float(self.data.xpos[body_id, 0])  # Return x by default
        elif prop == "quat" or prop == "xquat":
            if index is not None:
                return float(self.data.xquat[body_id, index])
            return float(self.data.xquat[body_id, 0])
        elif prop == "vel" or prop == "cvel":
            if index is not None:
                return float(self.data.cvel[body_id, index])
            return float(self.data.cvel[body_id, 0])
        else:
            raise ValueError(f"Unknown body property: {prop}")

    def _get_joint(self, name: str, prop: str, index: Optional[int]) -> float:
        """Get joint state"""
        joint_id = mujoco.mj_name2id(self.model, mujoco.mjtObj.mjOBJ_JOINT, name)
        if joint_id < 0:
            raise ValueError(f"Joint not found: {name}")

        qpos_adr = self.model.jnt_qposadr[joint_id]
        qvel_adr = self.model.jnt_dofadr[joint_id]

        if prop == "qpos" or prop == "pos":
            if index is not None:
                return float(self.data.qpos[qpos_adr + index])
            return float(self.data.qpos[qpos_adr])
        elif prop == "qvel" or prop == "vel":
            if index is not None:
                return float(self.data.qvel[qvel_adr + index])
            return float(self.data.qvel[qvel_adr])
        else:
            raise ValueError(f"Unknown joint property: {prop}")

    def _get_qpos(self, index: Optional[int]) -> float:
        """Get generalized position"""
        if index is not None:
            return float(self.data.qpos[index])
        return float(self.data.qpos[0])

    def _get_qvel(self, index: Optional[int]) -> float:
        """Get generalized velocity"""
        if index is not None:
            return float(self.data.qvel[index])
        return float(self.data.qvel[0])

    def _get_energy(self, energy_type: str) -> float:
        """Get energy metrics"""
        if energy_type == "potential":
            return float(self.data.energy[0])
        elif energy_type == "kinetic":
            return float(self.data.energy[1])
        elif energy_type == "total":
            return float(self.data.energy[0] + self.data.energy[1])
        else:
            raise ValueError(f"Unknown energy type: {energy_type}")

    def _step(self, n_steps: int = 1) -> None:
        """Run simulation steps"""
        for _ in range(n_steps):
            mujoco.mj_step(self.model, self.data)
            self.step_count += 1
        self.simulation_time = self.data.time

    def _reset(self) -> None:
        """Reset simulation to initial state"""
        mujoco.mj_resetData(self.model, self.data)
        self.simulation_time = 0.0
        self.step_count = 0
        print("Simulation reset to initial state", file=sys.stderr)

    def _forward(self) -> None:
        """Compute forward kinematics (positions from qpos)"""
        mujoco.mj_forward(self.model, self.data)

    def _inverse(self) -> None:
        """Compute inverse dynamics (forces from accelerations)"""
        mujoco.mj_inverse(self.model, self.data)
