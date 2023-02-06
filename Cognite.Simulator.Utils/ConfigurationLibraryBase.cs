using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents a local configuration file library. This is a <see cref="FileLibrary{T, U}"/> that
    /// fetches JSON simulation configuration files from CDF, save a local copy and parses the JSON
    /// content as an object that can be used to drive simulations
    /// </summary>
    /// <typeparam name="T">Type of the state object used in this library</typeparam>
    /// <typeparam name="U">Type of the data object used to serialize and deserialize state</typeparam>
    /// <typeparam name="V">Configuration object type. The contents of the JSON file are deserialized
    /// to an object of this type. properties of this object should use pascal case while the JSON
    /// properties should be lower camel case</typeparam>
    public abstract class ConfigurationLibraryBase<T, U, V> : FileLibrary<T, U>, IConfigurationProvider<T, V>
        where T : ConfigurationStateBase
        where U : FileStatePoco
        where V : SimulationConfigurationBase
    {
        /// <summary>
        /// Dictionary of simulation configurations. The key is the file external ID
        /// </summary>
        public Dictionary<string, V> SimulationConfigurations { get; }

        /// <summary>
        /// Creates a new instance of the library using the provided parameters
        /// </summary>
        /// <param name="config">Library configuration</param>
        /// <param name="simulators">Dictionary of simulators</param>
        /// <param name="cdf">CDF destination object</param>
        /// <param name="logger">Logger</param>
        /// <param name="downloadClient">HTTP client to download files</param>
        /// <param name="store">State store for models state</param>
        public ConfigurationLibraryBase(
            FileLibraryConfig config, 
            IList<SimulatorConfig> simulators, 
            CogniteDestination cdf, 
            ILogger logger, 
            FileDownloadClient downloadClient, 
            IExtractionStateStore store = null) : 
            base(SimulatorDataType.SimulationConfiguration, config, simulators, cdf, logger, downloadClient, store)
        {
            SimulationConfigurations = new Dictionary<string, V>();
        }

        /// <summary>
        /// Get the simulation configuration object with the given properties
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        /// <param name="calcType">Calculation type</param>
        /// <param name="calcTypeUserDefined">User defined calculation type</param>
        /// <returns>Simulation configuration object</returns>
        public V GetSimulationConfiguration(
            string simulator,
            string modelName,
            string calcType,
            string calcTypeUserDefined)
        {
            var calcConfigs = SimulationConfigurations.Values
                .Where(c => c.Simulator == simulator &&
                    c.ModelName == modelName &&
                    c.CalculationType == calcType &&
                    (string.IsNullOrEmpty(calcTypeUserDefined) || c.CalcTypeUserDefined == calcTypeUserDefined));
            if (calcConfigs.Any())
            {
                return calcConfigs.First();
            }
            return null;
        }

        /// <summary>
        /// Get the simulator configuration state object with the given parameters
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        /// <param name="calcType">Calculation type</param>
        /// <param name="calcTypeUserDefined">User defined calculation type</param>
        /// <returns>Simulation configuration state object</returns>
        public T GetSimulationConfigurationState(
            string simulator,
            string modelName,
            string calcType,
            string calcTypeUserDefined)
        {
            var calcConfigs = SimulationConfigurations
                .Where(c => c.Value.Simulator == simulator &&
                    c.Value.ModelName == modelName &&
                    c.Value.CalculationType == calcType &&
                    (string.IsNullOrEmpty(calcTypeUserDefined) || c.Value.CalcTypeUserDefined == calcTypeUserDefined));
            if (calcConfigs.Any())
            {
                var id = calcConfigs.First().Key;
                if (State.TryGetValue(id, out var configState))
                {
                    return configState;
                }
            }
            return null;
        }

        /// <summary>
        /// Process model files that have been downloaded
        /// </summary>
        /// <param name="token">Cancellation token</param>
        protected override void ProcessDownloadedFiles(CancellationToken token)
        {
            Task.Run(() => ReadConfigurations(), token).Wait(token);
        }

        private void ReadConfigurations()
        {
            var files = State.Values
                .Where(f => !string.IsNullOrEmpty(f.FilePath) && !f.Deserialized).ToList();
            foreach (var file in files)
            {
                try
                {
                    var json = JsonConvert.DeserializeObject<V>(
                        System.IO.File.ReadAllText(file.FilePath),
                        new JsonSerializerSettings
                        {
                            ContractResolver = new DefaultContractResolver
                            {
                                NamingStrategy = new CamelCaseNamingStrategy()
                            },
                            Converters = new List<JsonConverter>()
                            {
                                new Newtonsoft.Json.Converters.StringEnumConverter()
                            }
                        });
                    if (!SimulationConfigurations.ContainsKey(file.Id))
                    {
                        SimulationConfigurations.Add(file.Id, json);
                    }
                    else
                    {
                        SimulationConfigurations[file.Id] = json;
                    }
                    file.Deserialized = true;
                }
                catch (Exception e)
                {
                    Logger.LogError("Could not parse simulation configuration for model {ModelName}: {Error}", file.ModelName, e.Message);
                }
            }
        }
    }

    public interface IConfigurationProvider<T,V>
    {
        T GetSimulationConfigurationState(
            string simulator,
            string modelName,
            string calcType,
            string calcTypeUserDefined);
        V GetSimulationConfiguration(
            string simulator,
            string modelName,
            string calcType,
            string calcTypeUserDefined);
        Task StoreLibraryState(CancellationToken token);
    }
    public class SimulationConfigurationWithDataSampling : SimulationConfigurationBase
    {
        public DataSamplingConfiguration DataSampling { get; set; }
        public LogicalCheckConfiguration LogicalCheck { get; set; }
        public SteadyStateDetectionConfiguration SteadyStateDetection { get; set; }
        public IEnumerable<InputTimeSeriesConfiguration> InputTimeSeries { get; set; }
    }

    public class DataSamplingConfiguration
    {
        /// <summary>
        /// Validation window in minutes
        /// </summary>
        public int ValidationWindow { get; set; }

        /// <summary>
        /// Sampling window in minutes
        /// </summary>
        public int SamplingWindow { get; set; }

        /// <summary>
        /// Sampling granularity in minutes
        /// </summary>
        public int Granularity { get; set; }

        /// <summary>
        /// The validation window can be moved to the past by setting
        /// this offset. The format it <c>number(w|d|h|m|s)</c>
        /// </summary>
        public string ValidationEndOffset { get; set; } = "0s";
    }

    public class LogicalCheckConfiguration
    {
        /// <summary>
        /// Whether or not to run logical check (well status) for this configuration 
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// External ID of the time series to run the logical check against
        /// </summary>
        public string ExternalId { get; set; }

        /// <summary>
        /// Data points aggregate type conforming to CDF. One of <c>average</c>, <c>max</c>, <c>min</c>, <c>count</c>, 
        /// <c>sum</c>, <c>interpolation</c>, <c>stepInterpolation</c>, <c>totalVariation</c>, <c>continuousVariance</c>, <c>discreteVariance</c>
        /// </summary>
        public string AggregateType { get; set; }

        /// <summary>
        /// Equality operator. One of <c>eq</c>, <c>ne</c>, <c>gt</c>, <c>ge</c>, <c>lt</c>, <c>le</c>
        /// </summary>
        public string Check { get; set; }

        /// <summary>
        /// The value to compare against using the <see cref="Check"/> operator
        /// </summary>
        public double Value { get; set; }

    }

    public class SteadyStateDetectionConfiguration
    {
        /// <summary>
        /// Whether or not to run steady state detection for this configuration 
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// External ID of the time series to be evaluated
        /// </summary>
        public string ExternalId { get; set; }

        /// <summary>
        /// Data points aggregate type conforming to CDF. One of <c>average</c>, <c>max</c>, <c>min</c>, <c>count</c>, 
        /// <c>sum</c>, <c>interpolation</c>, <c>stepInterpolation</c>, <c>totalVariation</c>, <c>continuousVariance</c>, <c>discreteVariance</c>
        /// </summary>
        public string AggregateType { get; set; }

        /// <summary>
        /// Minimum size of section (segment distance)
        /// </summary>
        public int MinSectionSize { get; set; }

        /// <summary>
        /// Variance threshold
        /// </summary>
        public double VarThreshold { get; set; }

        /// <summary>
        /// Slope threshold
        /// </summary>
        public double SlopeThreshold { get; set; }
    }

    public class InputTimeSeriesConfiguration
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Unit { get; set; }
        public string UnitType { get; set; }
        public string SensorExternalId { get; set; }
        public string AggregateType { get; set; }
    }


    /// <summary>
    /// Base class for simulation configuration objects. This object should be
    /// used to deserialize the contents of a configuration file in CDF
    /// </summary>
    public class SimulationConfigurationBase
    {
        /// <summary>
        /// Simulator name
        /// </summary>
        public string Simulator { get; set; }
        
        /// <summary>
        /// Model name
        /// </summary>
        public string ModelName { get; set; }
        
        /// <summary>
        /// Calculation name
        /// </summary>
        public string CalculationName { get; set; }
        
        /// <summary>
        /// Calculation type. Used to identify different types of calculations
        /// supported by a given simulator.
        /// </summary>
        public string CalculationType { get; set; }
        
        /// <summary>
        /// User defined calculation type. User defined calculations will have
        /// <b>UserDefined</b> as <see cref="CalculationType"/>. This property
        /// provides a way of differentiating user defined calculations
        /// </summary>
        public string CalcTypeUserDefined { get; set; } = "";
        
        /// <summary>
        /// Connector name
        /// </summary>
        public string Connector { get; set; }
        
        /// <summary>
        /// Email of the user who created this configuration
        /// </summary>
        public string UserEmail { get; set; } = "";

        /// <summary>
        /// Calculation object crated from this configuration
        /// </summary>
        public SimulatorCalculation Calculation => new SimulatorCalculation
        {
            Model = new SimulatorModel
            {
                Name = ModelName,
                Simulator = Simulator
            },
            Name = CalculationName,
            Type = CalculationType,
            UserDefinedType = CalcTypeUserDefined
        };
    }

    /// <summary>
    /// This base class represents the state of a simulation configuration file
    /// </summary>
    public class ConfigurationStateBase : FileState
    {
        private string _runDataSequence;
        private long _runSequenceLastRow;

        /// <summary>
        /// External ID of the run configuration sequence in CDF
        /// </summary>
        public string RunDataSequence
        {
            get => _runDataSequence;
            set
            {
                if (value == _runDataSequence) return;
                LastTimeModified = DateTime.UtcNow;
                _runDataSequence = value;
            }
        }

        /// <summary>
        /// Index of the last row in the run configuration sequence
        /// </summary>
        public long RunSequenceLastRow
        {
            get => _runSequenceLastRow;
            set
            {
                if (value == _runSequenceLastRow) return;
                LastTimeModified = DateTime.UtcNow;
                _runSequenceLastRow = value;
            }
        }

        /// <summary>
        /// Indicates if the JSON content of the file has been deserialized
        /// </summary>
        public bool Deserialized { get; set; }

        /// <summary>
        /// Creates a new simulation configuration file state with the provided id
        /// </summary>
        /// <param name="id"></param>
        public ConfigurationStateBase(string id) : base(id)
        {
        }
        
        /// <summary>
        /// Data type of the file. For simulation configuration files, this is <see cref="SimulatorDataType.SimulationConfiguration"/> 
        /// </summary>
        /// <returns>String representation of <see cref="SimulatorDataType.SimulationConfiguration"/></returns>
        public override string GetDataType()
        {
            return SimulatorDataType.SimulationConfiguration.MetadataValue();
        }

        /// <summary>
        /// Returns the file extension used to store the simulation configuration files locally. 
        /// <b>json</b> by default
        /// </summary>
        /// <returns>File extension</returns>
        public override string GetExtension()
        {
            return "json";
        }

        /// <summary>
        /// Initialize this simulation configuration state using a data object from the state store
        /// </summary>
        /// <param name="poco">Data object</param>
        public override void Init(FileStatePoco poco)
        {
            base.Init(poco);
            if (poco is ConfigurationStateBasePoco statePoco)
            {
                _runDataSequence = statePoco.RunDataSequence;
                _runSequenceLastRow = statePoco.RunSequenceLastRow;

            }
        }
        
        /// <summary>
        /// Get the data object with the simulation configuration state properties to be persisted by
        /// the state store
        /// </summary>
        /// <returns>File data object</returns>
        public override FileStatePoco GetPoco()
        {
            return new ConfigurationStateBasePoco
            {
                Id = Id,
                ModelName = ModelName,
                Source = Source,
                DataSetId = DataSetId,
                FilePath = FilePath,
                CreatedTime = CreatedTime,
                CdfId = CdfId,
                RunDataSequence = RunDataSequence,
                RunSequenceLastRow = RunSequenceLastRow,
            };
        }
    }
    public class ConfigurationStateBasePoco : FileStatePoco
    {
        [StateStoreProperty("run-data-sequence")]
        public string RunDataSequence { get; set; }
        [StateStoreProperty("run-sequence-last-row")]
        public long RunSequenceLastRow { get; set; }
    }
}
