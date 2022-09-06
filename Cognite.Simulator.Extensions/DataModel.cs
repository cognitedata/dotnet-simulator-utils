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
        /// Model name metadata key
        /// </summary>
        public const string NameKey = "modelName";

        /// <summary>
        /// Data type of model files
        /// </summary>
        public const SimulatorDataType DataType = SimulatorDataType.ModelFile;
    }

    /// <summary>
    /// Metadata keys present in calculations (simulation configurations)
    /// </summary>
    public static class CalculationMetadata
    {
        /// <summary>
        /// Calculation type metadata key
        /// </summary>
        public const string TypeKey = "calcType";
        
        /// <summary>
        /// Type of user defined calculation metadata key
        /// </summary>
        public const string UserDefinedTypeKey = "calcTypeUserDefined";

        /// <summary>
        /// Data type of calculation files
        /// </summary>
        public const SimulatorDataType DataType = SimulatorDataType.SimulationConfiguration;
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
        SimulationConfiguration
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
                default: throw new ArgumentException("Invalid data type");
            }
        }
    }
}
