using System;
using System.Collections.Generic;
using System.Text;

namespace Cognite.Simulator.Utils
{
    public class SimulatorConfig
    {
        /// <summary>
        /// Name or Id of the simulator. E.g. PROSPER
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Model files, simulation configuration files and events for this
        /// simulator are read from this data set. Result inputs and outputs are
        /// written to this data set
        /// </summary>
        public long DataSetId { get; set; }
    }

    public class FileLibraryConfig
    {
        public int StateStoreInterval { get; set; } = 10;
        public int LibraryUpdateInterval { get; set; } = 5;
        public string LibraryId { get; set; }
        public string LibraryTable { get; set; }
        public string FilesTable { get; set; }
        public string FilesDirectory { get; set; }
    }
}
