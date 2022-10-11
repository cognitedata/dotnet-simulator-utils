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
        public const string DataModelVersion = "1.0.2";
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
    /// Matadata keys present in the sequences mapping boundary conditions to
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
    /// Metadata keys present in the sequences containing information about the
    /// existing simulation integrations
    /// </summary>
    public static class SimulatorIntegrationMetadata
    {
        /// <summary>
        /// Name of the connector handling the integration with a simulator
        /// </summary>
        public const string ConnectorNameKey = "connector";
        
        /// <summary>
        /// Data type of simulation integarations
        /// </summary>
        public const SimulatorDataType DataType = SimulatorDataType.SimulatorIntegration;
    }

    /// <summary>
    /// Columns that should be part of the boundary conditions to time series id map
    /// </summary>
    public static class BoundaryConditionsSequenceColumns
    {
        /// <summary>
        /// Boundary condition id
        /// </summary>
        public const string Id = "boundary-condition";
        
        /// <summary>
        /// Time series external id
        /// </summary>
        public const string TimeSeries = "time-series";
        
        /// <summary>
        /// Boundary condition name
        /// </summary>
        public const string Name = "boundary-condition-name";
        
        /// <summary>
        /// Address of the boundary condition in the source system (simulator)
        /// </summary>
        public const string Address = "boundary-condition-address";
    }

    public static class KeyValuePairSequenceColumns
    {
        public const string Key = "key";
        public const string KeyName = "Key";
        public const string Value = "value";
        public const string ValueName = "Value";
    }

    public static class SimulatorIntegrationSequenceRows
    {
        public const string Heartbeat = "heartbeat";
        public const string DataSetId = "dataSetId";
        public const string ConnectorVersion = "connectorVersion";
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
                default: throw new ArgumentException("Invalid data type");
            }
        }
    }
}
