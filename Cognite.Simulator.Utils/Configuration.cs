using System;
using Cognite.Extractor.Configuration;
using Cognite.Extractor.Logging;
using Cognite.Extractor.Metrics;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils.Automation;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents a simulator configuration, holding the simulator name and dataset id in CDF.
    /// Ideally, all the data related to the simulator should be in this dataset 
    /// </summary>
    public class SimulatorConfig
    {
        /// <summary>
        /// Name or Id of the simulator. E.g. PROSPER, PetroSim
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Simulator data, such as model files, simulation configuration files and events,
        /// should be in this data set. Result inputs and outputs should be
        /// written to this data set
        /// </summary>
        public long DataSetId { get; set; }
    }

    /// <summary>
    /// Configuration for remote logging (logs are stored in CDF)
    /// </summary>
    public class RemoteLogConfig: LogConfig
    {
        /// <summary>
        /// Whether remote logging is enabled
        /// </summary>
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Logging configuration object
    /// </summary>
    public class LoggerConfig: Extractor.Logging.LoggerConfig
    {
        /// <summary>
        /// Remote logging configuration (optional)
        /// Enabled by default, with warning level
        /// Writes logs to CDF simulator logs
        /// </summary>
        public RemoteLogConfig Remote { get; set; }
    }

    /// <summary>
    /// Base configuration object for simulators.
    /// </summary>
    public class BaseConfig : VersionedConfig
    {
        /// <summary>
        /// Type of configuration this represents, local or remote. Will generally always
        /// be local.
        /// </summary>
        public ConfigurationMode Type { get; set; }

        /// <summary>
        /// Logging configuration (optional)
        /// </summary>
        public LoggerConfig Logger { get; set; }

        /// <summary>
        /// Metrics configuration (optional)
        /// </summary>
        public MetricsConfig Metrics { get; set; }

        /// <summary>
        /// Cognite configuration (optional)
        /// </summary>
        public CogniteConfig Cognite { get; set; }

        /// <summary>
        /// State store configuration (optional)
        /// </summary>
        public StateStoreConfig StateStore { get; set; }

        /// <summary>
        /// Generate default configuration objects if the corresponding tags are not present
        /// in the yaml config file/string
        /// </summary>
        public override void GenerateDefaults()
        {
            if (Cognite == null)
            {
                Cognite = new CogniteConfig();
            }

            if (Metrics == null)
            {
                Metrics = new MetricsConfig();
            }

            if (Logger == null)
            {
                Logger = new LoggerConfig();
            }

            if (Logger.Remote == null)
            {
                Logger.Remote = new RemoteLogConfig() {
                    Enabled = true,
                    Level = "warning"
                };
            }

            if (StateStore == null)
            {
                StateStore = new StateStoreConfig();
            }
        }
    }

    /// <summary>
    /// Represents the configuration for a model library. Used to configure <seealso cref="ModelLibraryBase{A, T, U, V}"/>
    /// and derived classes
    /// </summary>
    public class ModelLibraryConfig
    {
        /// <summary>
        /// Interval for fetching new data from CDF and updating the library
        /// </summary>
        public int LibraryUpdateInterval { get; set; } = 10;

        /// <summary>
        /// Id of the library object
        /// </summary>
        public string LibraryId { get; set; }

        /// <summary>
        /// Table containing the state of the library itself (extracted range)
        /// </summary>
        public string LibraryTable { get; set; }

        /// <summary>
        /// Table containing the state of the downloaded files (last time updated)
        /// </summary>
        public string FilesTable { get; set; }

        /// <summary>
        /// Local directory that contains the downloaded files
        /// </summary>
        public string FilesDirectory { get; set; }

        /// <summary>
        /// Maximum number of download attempts before giving up. Default is 3.
        /// </summary>
        public int MaxDownloadAttempts { get; set; } = 3;
    }

    /// <summary>
    /// Represents configuration for a local routine library. Used to configure <seealso cref="RoutineLibraryBase{V}"/>
    /// </summary>
    public class RoutineLibraryConfig
    {
        /// <summary>
        /// Interval for fetching new data from CDF and updating the library
        /// </summary>
        public int LibraryUpdateInterval { get; set; } = 10;
    }

    /// <summary>
    /// Represents the configuration for connectors. This defines basic
    /// connector properties such as name and intervals for status report and
    /// for fetching events
    /// </summary>
    public class ConnectorConfig
    {
        /// <summary>
        /// The connector name prefix. If <see cref="AddMachineNameSuffix"/> is set to <c>false</c>
        /// then this is the connector name
        /// </summary>
        public string NamePrefix { get; set; }

        /// <summary>
        /// If true, the connector name is composed of the prefix <see cref="NamePrefix"/> and
        /// the name of the machine running the connector
        /// </summary>
        public bool AddMachineNameSuffix { get; set; } = true;

        /// <summary>
        /// The connector will update its heartbeat in CDF with this interval (in seconds)
        /// </summary>
        public int StatusInterval { get; set; } = 10;

        /// <summary>
        /// The connector will fetch simulation runs from CDF with this interval (in seconds)
        /// </summary>
        public int FetchRunsInterval { get; set; } = 5;

        /// <summary>
        /// The connector will run simulation run resource found on CDF that are not older than
        /// this value (in seconds). In case it finds items older than this, the runs will
        /// fail due to timeout
        /// TODO: We should use this so that our runs dont pile up
        /// </summary>
        public int SimulationRunTolerance { get; set; } = 1800; // 30 min

        /// <summary>
        /// The connector will check if scheduled simulations should be triggered with
        /// this interval (in seconds)
        /// </summary>
        public int SchedulerUpdateInterval { get; set; } = 10;

        /// <summary>
        /// The scheduler will trigger simulations that passed the scheduled time, but will tolerate
        /// missing the scheduled time by this much time (in seconds)
        /// </summary>
        public int SchedulerTolerance { get; set; } = 300;

        /// <summary>
        /// Configuration related to error tolerance before reporting a failed run to the pipeline in CDF 
        /// </summary>
        public PipelineNotificationConfig PipelineNotification { get; set; }

        /// <summary>
        /// Configure License checking, enable or change frequency
        /// </summary>
        public LicenseCheckConfig LicenseCheck { get; set; } = new LicenseCheckConfig();

        /// <summary>
        /// Returns the connector name, composed of the configured prefix and suffix
        /// </summary>
        /// <returns>Connector name</returns>
        public string GetConnectorName()
        {
            if (!AddMachineNameSuffix)
            {
                return NamePrefix;
            }
            return $"{NamePrefix}{Environment.MachineName}";
        }
    }

    /// <summary>
    /// Pipeline notification configuration. This states how many errors
    /// can happen in a time frame before notifying the extraction pipeline.
    /// Usually connectors can recover from intermittent errors, and this policy can
    /// reduce the number of times alerts are generated due to pipeline errors
    /// </summary>
    public class PipelineNotificationConfig
    {
        /// <summary>
        /// Maximum number of error allowed withing the time frame
        /// </summary>
        public int MaxErrors { get; set; } = 10;

        /// <summary>
        /// Size of the time frame in minutes
        /// </summary>
        public int MaxTime { get; set; } = 10;
    }

    /// <summary>
    /// Configuration for license checks
    /// </summary>
    public class LicenseCheckConfig
    {
        /// <summary>
        /// The connector will check for license updates with this interval (in seconds)
        /// </summary>
        public int Interval { get; set; } = 3600;

        /// <summary>
        /// Only check for license if this is set to true
        /// </summary>
        public bool Enabled { get; set; }
    }

    public class DefaultConnectorConfig : ConnectorConfig
    {
        public ModelLibraryConfig ModelLibrary { get; set; }
        public RoutineLibraryConfig RoutineLibrary { get; set; }
    }

    public class DefaultConfig<TAutomationConfig> : BaseConfig 
    where TAutomationConfig : AutomationConfig, new()
    {
        public SimulatorConfig Simulator { get; set; }
        public DefaultConnectorConfig Connector { get; set; }

        public TAutomationConfig Automation { get; set; }

        public override void GenerateDefaults()
        {
            base.GenerateDefaults();

            if (Connector == null) Connector = new DefaultConnectorConfig();

            if (Simulator == null) Simulator = new SimulatorConfig();

            if (Automation == null) Automation = new TAutomationConfig();

            if (Connector.ModelLibrary == null) Connector.ModelLibrary = new ModelLibraryConfig();
            if (Connector.ModelLibrary.LibraryId == null) Connector.ModelLibrary.LibraryId = "ModelLibraryState";
            if (Connector.ModelLibrary.LibraryTable == null) Connector.ModelLibrary.LibraryTable = "ModelLibrary";
            if (Connector.ModelLibrary.FilesTable == null) Connector.ModelLibrary.FilesTable = "ModelLibraryFiles";
            if (Connector.ModelLibrary.FilesDirectory == null) Connector.ModelLibrary.FilesDirectory = "./files";

            if (Connector.RoutineLibrary == null) Connector.RoutineLibrary = new RoutineLibraryConfig();

            if (Connector.PipelineNotification == null) Connector.PipelineNotification = new PipelineNotificationConfig();
        }
    }
}
