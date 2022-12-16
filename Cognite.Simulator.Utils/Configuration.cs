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
}
