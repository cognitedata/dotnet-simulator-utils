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
    /// Types of simulator resources that can be stored in CDF
    /// </summary>
    public enum SimulatorDataType
    {
        /// <summary>
        /// Simulation output data type
        /// </summary>
        SimulationOutput,
        /// <summary>
        /// Simulation sampled input data type
        /// </summary>
        SimulationInput,
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
                case SimulatorDataType.SimulationOutput: return "Simulation Output";
                case SimulatorDataType.SimulationInput: return "Simulation Input";

                default: throw new ArgumentException("Invalid data type");
            }
        }
    }
}
