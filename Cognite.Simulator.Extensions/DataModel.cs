using System;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Basic metadata keys present in all simulator resources in CDF
    /// </summary>
    public static class BaseMetadata
    {
        /// <summary>
        /// Data type metadata key
        /// </summary>
        public const string DataTypeKey = "dataType";
        
        /// <summary>
        /// Simulator metadata key
        /// </summary>
        public const string SimulatorKey = "simulator";

        /// <summary>
        /// Data model version key
        /// </summary>
        public const string DataModelVersionKey = "dataModelVersion";

        /// <summary>
        /// Data model version value. This hardcoded value should be bumped for every
        /// new data model version
        /// </summary>
        public const string DataModelVersionValue = "1.0.2";
    }

    /// <summary>
    /// Metadata keys present in model files
    /// </summary>
    public static class ModelMetadata
    {
        /// <summary>
        /// Model external id key
        /// </summary>
        public const string ExternalId = "modelExternalId";
    }

    /// <summary>
    /// Metadata keys needed when creating tume series in CDF
    /// </summary>
    public static class RoutineRevisionMetadataForTS
    {
        /// <summary>
        /// Routine external id metadata key
        /// </summary>
        public const string RoutineExternalId = "routineExternalId";

        /// <summary>
        /// Routine revision metadata key
        /// </summary>
        public const string RoutineRevisionExternalId = "routineRevisionExternalId";
    }

    /// <summary>
    /// Metadata keys present in the sequences mapping boundary conditions to
    /// time series ids
    /// </summary>
    public static class BoundaryConditionsMapMetadata
    {
        /// <summary>
        /// Data type of boundary conditions map
        /// </summary>
        public const SimulatorDataType DataType = SimulatorDataType.BoundaryConditionsMap;
    }

    /// <summary>
    /// Metadata keys present in types related to simulator variables (e.g. boundary conditions, simulation inputs)
    /// </summary>
    public static class VariableMetadata
    {
        /// <summary>
        /// Variable type metadata key
        /// </summary>
        public const string VariableTypeKey = "referenceId";

        /// <summary>
        /// Variable name metadata key
        /// </summary>
        public const string VariableNameKey = "variableName";

    }

    /// <summary>
    /// Metadata keys present in boundary conditions
    /// </summary>
    public static class BoundaryConditionMetadata
    {
        /// <summary>
        /// Boundary condition variable type metadata key
        /// </summary>
        public const string VariableTypeKey = VariableMetadata.VariableTypeKey;

        /// <summary>
        /// Boundary condition variable name metadata key
        /// </summary>
        public const string VariableNameKey = VariableMetadata.VariableNameKey;

        /// <summary>
        /// Data type of boundary conditions
        /// </summary>
        public const SimulatorDataType DataType = SimulatorDataType.BoundaryCondition;
    }


    /// <summary>
    /// Metadata keys present in simulation inputs
    /// </summary>
    public static class SimulationVariableMetadata
    {
        /// <summary>
        /// simulation input variable type metadata key
        /// </summary>
        public const string VariableRefIdKey = VariableMetadata.VariableTypeKey;

        /// <summary>
        /// Simulation input variable name metadata key
        /// </summary>
        public const string VariableNameKey = VariableMetadata.VariableNameKey;
    }

    /// <summary>
    /// Columns that should be part of key/value pair sequences
    /// </summary>
    public static class KeyValuePairSequenceColumns
    {
        /// <summary>
        /// Key column id
        /// </summary>
        public const string Key = "key";

        /// <summary>
        /// Key column name
        /// </summary>
        public const string KeyName = "Key";

        /// <summary>
        /// Value column id
        /// </summary>
        public const string Value = "value";

        /// <summary>
        /// Value column name
        /// </summary>
        public const string ValueName = "Value";
    }

    /// <summary>
    /// Types of simulator resources that can be stored in CDF
    /// </summary>
    public enum SimulatorDataType
    {
        /// <summary>
        /// Model file data type
        /// </summary>
        ModelFile,
        /// <summary>
        /// Simulation configuration data type
        /// </summary>
        SimulationConfiguration,
        /// <summary>
        /// Boundary conditions map data type
        /// </summary>
        BoundaryConditionsMap,
        /// <summary>
        /// Simulator integration data type
        /// </summary>
        SimulatorIntegration,
        /// <summary>
        /// Simulation output data type
        /// </summary>
        SimulationOutput,
        /// <summary>
        /// Simulation run configuration data type
        /// </summary>
        SimulationRunConfiguration,
        /// <summary>
        /// Simulation calculation data type
        /// </summary>
        SimulationEvent,
        /// <summary>
        /// Model boundary condition data type
        /// </summary>
        BoundaryCondition,
        /// <summary>
        /// Simulation sampled input data type
        /// </summary>
        SimulationInput,
        /// <summary>
        /// Simulation model version data type
        /// </summary>
        SimulationModelVersion
    }

    /// <summary>
    /// Extensions to the data type enumeration
    /// </summary>
    public static class DataTypeExtensions
    {
        /// <summary>
        /// Convert a data type enumeration to a string
        /// </summary>
        /// <param name="dataType">Data type</param>
        /// <returns>String representation</returns>
        public static string MetadataValue(this SimulatorDataType dataType)
        {
            switch (dataType)
            {
                case SimulatorDataType.ModelFile: return "Simulator File";
                case SimulatorDataType.SimulationConfiguration: return "Simulation Configuration";
                case SimulatorDataType.BoundaryConditionsMap: return "Boundary Condition Time Series Map";
                case SimulatorDataType.SimulatorIntegration: return "Simulator Integration";
                case SimulatorDataType.SimulationOutput: return "Simulation Output";
                case SimulatorDataType.SimulationRunConfiguration: return "Run Configuration";
                case SimulatorDataType.SimulationEvent: return "Simulation Calculation";
                case SimulatorDataType.BoundaryCondition: return "Boundary Condition";
                case SimulatorDataType.SimulationInput: return "Simulation Input";
                case SimulatorDataType.SimulationModelVersion: return "Simulation Model Version";

                default: throw new ArgumentException("Invalid data type");
            }
        }
    }
}
