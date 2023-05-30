using System;
using System.Collections.Generic;
using System.Text;

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
    /// Represents the configuration for a file library. Used to configure <seealso cref="FileLibrary{T, U}"/>
    /// and derived classes
    /// </summary>
    public class FileLibraryConfig
    {
        /// <summary>
        /// Interval for writing the library state to the local store.
        /// </summary>
        public int StateStoreInterval { get; set; } = 10;
        
        /// <summary>
        /// Interval for fetching new data from CDF and updating the library
        /// </summary>
        public int LibraryUpdateInterval { get; set; } = 5;
        
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
        /// The connector will fetch simulation calculation events from CDF with this interval (in seconds)
        /// </summary>
        public int FetchEventsInterval { get; set; } = 5;

        /// <summary>
        /// The connector will run simulation events found on CDF that are not older than
        /// this value (in seconds). In case it finds events older than this, the events will
        /// fail due to timeout
        /// </summary>
        public int SimulationEventTolerance { get; set; } = 1800; // 30 min

        /// <summary>
        /// The maximum number of rows to insert into a single sequence in CDF
        /// </summary>
        public long MaximumNumberOfSequenceRows { get; set; } = 100_000;

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

        public bool UseSimulatorsApi { get; set; }

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
    /// Staging area configuration
    /// </summary>
    public class StagingConfig
    {
        /// <summary>
        /// Whether or not to enable the staging area
        /// </summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>
        /// Name of the database to be used
        /// </summary>
        public string Database { get; set; } = "SIMCONFIG";
        
        /// <summary>
        /// Name of the table to be used
        /// </summary>
        public string Table { get; set; }
    }

}
