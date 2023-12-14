using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Represents a simulator model file
    /// </summary>
    public class SimulatorModel
    {
        /// <summary>
        /// Simulator name
        /// </summary>
        public string Simulator { get; set; }

        /// <summary>
        /// Model name
        /// </summary>
        public string Name { get; set; }

    }

    /// <summary>
    /// Represents a simulator calculation file
    /// </summary>
    public class SimulatorCalculation
    {
        /// <summary>
        /// Calculation type (e.g. IPR/VLP)
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Calculation name (e.g. Rate by Nodal Analysis)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Calculation type - user defined (e.g. CustomIprVlp)
        /// </summary>
        public string UserDefinedType { get; set; }

        /// <summary>
        /// Simulator model associated with this calculation
        /// </summary>
        public SimulatorModel Model { get; set; }
    }

    /// <summary>
    /// Represents simulation tabular results as columns and rows
    /// </summary>
    public class SimulationTabularResults
    {
        /// <summary>
        /// Result type (e.g. SystemCurves)
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Result name (e.g. System Curves)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Calculation that produced these tabular results
        /// </summary>
        public SimulatorCalculation Calculation { get; set; }

        /// <summary>
        /// Columns with simulation results. The dictionary key
        /// represents the column header
        /// </summary>
        public IDictionary<string, SimulationResultColumn> Columns { get; set; }

        /// <summary>
        /// Returns the maximum number of rows accross all columns
        /// </summary>
        /// <returns>Maximum number of rows</returns>
        public int MaxNumOfRows()
        {
            return Columns.Select(c => c.Value.NumOfRows()).Max();
        }
    }

    /// <summary>
    /// Represents a simulation result column
    /// </summary>
    public abstract class SimulationResultColumn
    {
        /// <summary>
        /// Metadata to be atached to the column
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Count the number of rows
        /// </summary>
        /// <returns>Number of rows</returns>
        public abstract int NumOfRows();
    }

    /// <summary>
    /// Represents a numeric simulation result column
    /// </summary>
    public class SimulationNumericResultColumn : SimulationResultColumn
    {
        private List<double> _rows;

        /// <summary>
        /// Numeric row values
        /// </summary>
        public IEnumerable<double> Rows { get => _rows; set => _rows = value.ToList(); }

        /// <summary>
        /// Count the number of rows
        /// </summary>
        /// <returns>Number of rows</returns>
        public override int NumOfRows()
        {
            return Rows != null ? Rows.Count() : 0;
        }

        /// <summary>
        /// Add a numeric value to this column
        /// </summary>
        /// <param name="value">Numeric value</param>
        public void Add(double value)
        {
            if (_rows == null)
            {
                _rows = new List<double>();
            }
            _rows.Add(value);
        }
    }

    /// <summary>
    /// Represents a string simulation result column
    /// </summary>
    public class SimulationStringResultColumn : SimulationResultColumn
    {
        private List<string> _rows;

        /// <summary>
        /// String row values
        /// </summary>
        public IEnumerable<string> Rows { get => _rows; set => _rows = value.ToList(); }

        /// <summary>
        /// Count the number of rows
        /// </summary>
        /// <returns>Number of rows</returns>
        public override int NumOfRows()
        {
            return Rows != null ? Rows.Count() : 0;
        }

        /// <summary>
        /// Add a string value to this column
        /// </summary>
        /// <param name="value">String value</param>
        public void Add(string value)
        {
            if (_rows == null)
            {
                _rows = new List<string>();
            }
            _rows.Add(value);
        }
    }

    /// <summary>
    /// Represents a simulation run event
    /// </summary>
    public class SimulationEvent
    {
        /// <summary>
        /// Reference to the simulation configuration file (calculation)
        /// </summary>
        public SimulatorCalculation Calculation { get; set; }

        /// <summary>
        /// User who initiated the event (manually triggered or configured a scheduled run)
        /// </summary>
        public string UserEmail { get; set; }

        /// <summary>
        /// Type of the simulation run
        /// </summary>
        public string RunType { get; set; }

        /// <summary>
        /// Identifier of the connector that will handle this event
        /// </summary>
        public string Connector { get; set; }

        /// <summary>
        /// ID of the CDF data set that contains this event
        /// </summary>
        public long? DataSetId { get; set; }

        /// <summary>
        /// External ID of the CDF file containing the simulation configuration
        /// </summary>
        public string CalculationId { get; set; }

        /// <summary>
        /// Typically, the connector samples data using the current time: time the event was picked for execution.
        /// In case the data should be sampled from a time in the past, than this overwrite should be used.
        /// </summary>
        public long? ValidationEndOverwrite { get; set; }
    }

    /// <summary>
    /// Represents a boundary condition associated with a simulator model
    /// </summary>
    public class BoundaryCondition
    {

        /// <summary>
        /// Model associated with this boundary condition
        /// </summary>
        public SimulatorModel Model { get; set; }


        /// <summary>
        /// Boundary condition key (identifier)
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Boundary condition name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Boundary condition unit
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// ID of the CDF data set that contains this boundary condition
        /// </summary>
        public long? DataSetId { get; set; }
    }

    /// <summary>
    /// Represents the sampled inputs used in a calculation
    /// </summary>
    public class SimulationInput : SimulationTimeSeries
    {
        /// <summary>
        /// Time series external id. Auto-generated by default
        /// </summary>
        public override string TimeSeriesExternalId => string.IsNullOrEmpty(ExternalIdOverwrite) ?
            $"{Calculation.Model.Simulator}-INPUT-{Calculation.GetCalcTypeForIds()}-{Type}-{Calculation.Model.GetModelNameForIds()}"
            : ExternalIdOverwrite;

        internal override string TimeSeriesName =>
            $"{Name} - INPUT - {Calculation.GetCalcNameForNames()} - {Calculation.Model.GetModelNameForNames()}";

        internal override string TimeSeriesDescription =>
            $"Input sampled for {Calculation.Name} - {Calculation.Model.Name}";
    }

    /// <summary>
    /// Represents the results of a calculation
    /// </summary>
    public class SimulationOutput : SimulationTimeSeries
    {
        /// <summary>
        /// Time series external id. Auto-generated by default
        /// </summary>
        public override string TimeSeriesExternalId => string.IsNullOrEmpty(ExternalIdOverwrite) ?
            $"{Calculation.Model.Simulator}-OUTPUT-{Calculation.GetCalcTypeForIds()}-{Type}-{Calculation.Model.GetModelNameForIds()}"
            : ExternalIdOverwrite;

        internal override string TimeSeriesName => 
            $"{Name} - OUTPUT - {Calculation.GetCalcNameForNames()} - {Calculation.Model.GetModelNameForNames()}";

        internal override string TimeSeriesDescription =>
            $"Calculation result for {Calculation.Name} - {Calculation.Model.Name}";
    }

    /// <summary>
    /// Represents a simulation variable associated with a calculation. For instance,
    /// simulation sampled inputs and result outputs
    /// </summary>
    public abstract class SimulationTimeSeries
    {
        /// <summary>
        /// Allows overwriting the default time series external id
        /// with the one provided by the user
        /// </summary>
        protected string ExternalIdOverwrite { get; set; } = "";

        /// <summary>
        /// Calculation associated with this variable
        /// </summary>
        public SimulatorCalculation Calculation { get; set; }

        /// <summary>
        /// Variable type
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Variable name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Variable unit
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// Any other metadata related to this variable
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Time series external id. Auto-generated
        /// </summary>
        public abstract string TimeSeriesExternalId {
            get;
        }
        internal abstract string TimeSeriesName
        {
            get;
        }
        internal abstract string TimeSeriesDescription
        {
            get;
        }

        /// <summary>
        /// Add a (key, value) pair as metadata
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        public void AddMetadata(string key, string value)
        {
            if (Metadata == null)
            {
                Metadata = new Dictionary<string, string>();
            }
            Metadata[key] = value;
        }

        /// <summary>
        /// Get the metadata value associated with the given key, if any
        /// </summary>
        /// <param name="key">Key</param>
        /// <returns>Metadata value, if key exists. Else, <c>null</c></returns>
        public string GetMetadata(string key)
        {
            if (Metadata != null && Metadata.ContainsKey(key))
            {
                return Metadata[key];
            }
            return null;
        }

        /// <summary>
        /// Overwrite the time series External ID with the one provided as parameters.
        /// If this method is not used, the time series External ID is auto-generated.
        /// </summary>
        /// <param name="externalId">New External ID to be used</param>
        public void OverwriteTimeSeriesId(string externalId)
        {
            ExternalIdOverwrite = externalId;
        }
    }
}
