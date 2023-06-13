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
        /// Model name metadata key
        /// </summary>
        public const string NameKey = "modelName";

        /// <summary>
        /// Model version metadata key
        /// </summary>
        public const string VersionKey = "version";

        /// <summary>
        /// Error message metadata key. This metadata key is present only in files with errors
        /// that prevent the file from being opened by the simulator
        /// </summary>
        public const string ErrorMessageKey = "errorMessage";

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
        /// Calculation name metadata key
        /// </summary>
        public const string NameKey = "calcName";

        /// <summary>
        /// Type of user defined calculation metadata key
        /// </summary>
        public const string UserDefinedTypeKey = "calcTypeUserDefined";

        /// <summary>
        /// Result type metadata key
        /// </summary>
        public const string ResultTypeKey = "resultType";

        /// <summary>
        /// Result name metadata key
        /// </summary>
        public const string ResultNameKey = "resultName";

        /// <summary>
        /// Data type of calculation files
        /// </summary>
        public const SimulatorDataType DataType = SimulatorDataType.SimulationConfiguration;
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
        public const string VariableTypeKey = "variableType";

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
        public const string VariableTypeKey = VariableMetadata.VariableTypeKey;

        /// <summary>
        /// Simulation input variable name metadata key
        /// </summary>
        public const string VariableNameKey = VariableMetadata.VariableNameKey;
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
        /// Data type of simulation integrations
        /// </summary>
        public const SimulatorDataType DataType = SimulatorDataType.SimulatorIntegration;
    }

    /// <summary>
    /// Metadata keys present in simulation events (simulation calculations)
    /// </summary>
    public static class SimulationEventMetadata
    {
        /// <summary>
        /// Event status metadata key 
        /// </summary>
        public const string StatusKey = "status";
        
        /// <summary>
        /// Event status message metadata key
        /// </summary>
        public const string StatusMessageKey = "statusMessage";
        
        /// <summary>
        /// Model version metadata key
        /// </summary>
        public const string ModelVersionKey = "modelVersion";
        
        /// <summary>
        /// Run type metadata key
        /// </summary>
        public const string RunTypeKey = "runType";
        
        /// <summary>
        /// User email metadata key
        /// </summary>
        public const string UserEmailKey = "userEmail";
        
        /// <summary>
        /// External id of the simulation configuration metadata key
        /// </summary>
        public const string CalculationIdKey = "calcConfig";

        /// <summary>
        /// Indicates that the start of the validation time should be overwritten with the metadata value
        /// </summary>
        public const string ValidationEndOverwriteKey = "validationEndOverwrite";

        /// <summary>
        /// Data type of simulation events
        /// </summary>
        public const SimulatorDataType DataType = SimulatorDataType.SimulationEvent;
    }

    /// <summary>
    /// Possible values for the simulation event status metadata
    /// </summary>
    public static class SimulationEventStatusValues
    {
        /// <summary>
        /// Ready status
        /// </summary>
        public const string Ready = "ready";
        
        /// <summary>
        /// Running status
        /// </summary>
        public const string Running = "running";
        
        /// <summary>
        /// Success status
        /// </summary>
        public const string Success = "success";
        
        /// <summary>
        /// Failure status
        /// </summary>
        public const string Failure = "failure";
    }

    /// <summary>
    /// Possible values for the simulation events run type metadata
    /// </summary>
    public static class SimulationEventRunTypeValues
    {
        /// <summary>
        /// Manual run
        /// </summary>
        public const string Manual = "manual";
        
        /// <summary>
        /// Scheduled run
        /// </summary>
        public const string Scheduled = "scheduled";
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
    /// Row keys that should be present in simulator integration sequences
    /// </summary>
    public static class SimulatorIntegrationSequenceRows
    {
        /// <summary>
        /// Heartbeat key (Last time seen)
        /// </summary>
        public const string Heartbeat = "heartbeat";
        
        /// <summary>
        /// Data set id key. Data set containg the data used and generated by the simulator integration
        /// </summary>
        public const string DataSetId = "dataSetId";
        
        /// <summary>
        /// Connector version key. Version of the connector associated with this simulator integration
        /// </summary>
        public const string ConnectorVersion = "connectorVersion";

        /// <summary>
        /// Simulator version key. Installed version of the simulator
        /// </summary>
        public const string SimulatorVersion = "simulatorVersion";

        /// <summary>
        /// Api enabled key. Indicates if the simulator api is enabled for simulation runs or if it's using CDF Events
        /// </summary>
        public const string SimulatorsApiEnabled = "apiEnabled";

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
