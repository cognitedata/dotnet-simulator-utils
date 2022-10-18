using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Cognite.Simulator.Extensions
{
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

        public SimulatorCalculation Calculation { get; set; }

        /// <summary>
        /// Columns with simulation results. The dictionary key
        /// represents the column header
        /// </summary>
        public IDictionary<string, SimulationResultColumn> Columns { get; set; }

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

        public override int NumOfRows()
        {
            return Rows != null ? Rows.Count() : 0;
        }

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

        public override int NumOfRows()
        {
            return Rows != null ? Rows.Count() : 0;
        }
        public void Add(string value)
        {
            if (_rows == null)
            {
                _rows = new List<string>();
            }
            _rows.Add(value);
        }
    }
}
