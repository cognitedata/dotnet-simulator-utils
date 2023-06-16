using Cognite.Extractor.Common;
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
        /// <inheritdoc/>
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

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public V GetSimulationConfiguration(
            string simulator,
            string modelName,
            string calcName)
        {
            var calcConfigs = SimulationConfigurations.Values
                .Where(c => c.Simulator == simulator &&
                    c.ModelName == modelName &&
                    c.CalculationName == calcName);
            if (calcConfigs.Any())
            {
                return calcConfigs.First();
            }
            return null;
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public T GetSimulationConfigurationState(
            string simulator,
            string modelName,
            string calcName)
        {
            var calcConfigs = SimulationConfigurations
                .Where(c => c.Value.Simulator == simulator &&
                    c.Value.ModelName == modelName &&
                    c.Value.CalculationName == calcName);
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
        /// Determines if the given configuration exists or not by trying to fetch it
        /// from CDF. This method can be overridden to add extra verification steps
        /// </summary>
        /// <param name="state">Configuration state</param>
        /// <param name="config">Configuration object</param>
        /// <param name="token">Cancellation token</param>
        /// <returns><c>true</c> if the configuration can be fetched from CDF, <c>false</c> otherwise</returns>
        protected virtual async Task<bool> ConfigurationFileExistsInCdf(
            T state,
            V config,
            CancellationToken token)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            var configsInCdf = await CdfFiles.FindConfigurationFiles(
                config.Calculation,
                state.DataSetId,
                token).ConfigureAwait(false);
            return configsInCdf.Any(v => v.ExternalId == state.Id);
        }

        /// <inheritdoc/>
        public async Task<bool> VerifyLocalConfigurationState(
            T state,
            V config,
            CancellationToken token)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            var exists = await ConfigurationFileExistsInCdf(state, config, token).ConfigureAwait(false);
            if (exists)
            {
                return true;
            }
            Logger.LogWarning("Removing {Model} - {Calc} calculation configuration, not found in CDF",
                state.ModelName,
                config.CalculationName);
            State.Remove(state.Id);
            SimulationConfigurations.Remove(state.Id);
            await RemoveStates(new List<T> { state }, token).ConfigureAwait(false);
            return false;
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

    /// <summary>
    /// Interface for libraries that can provide configuration information
    /// </summary>
    /// <typeparam name="T">Configuration state type</typeparam>
    /// <typeparam name="V">Configuration object type</typeparam>
    public interface IConfigurationProvider<T,V>
    {
        /// <summary>
        /// Dictionary of simulation configurations. The key is the file external ID
        /// </summary>
        Dictionary<string, V> SimulationConfigurations { get; }

        /// <summary>
        /// Get the simulator configuration state object with the given parameters
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        /// <param name="calcType">Calculation type</param>
        /// <param name="calcTypeUserDefined">User defined calculation type</param>
        /// <returns>Simulation configuration state object</returns>
        T GetSimulationConfigurationState(
            string simulator,
            string modelName,
            string calcType,
            string calcTypeUserDefined);

        /// <summary>
        /// Get the simulator configuration state object with the given parameters
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        /// <param name="calcName">Calculation name</param>
        /// <returns>Simulation configuration state object</returns>
        T GetSimulationConfigurationState(
            string simulator,
            string modelName,
            string calcName);

        /// <summary>
        /// Get the simulation configuration object with the given properties
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        /// <param name="calcName">Calculation name</param>
        /// <returns>Simulation configuration state object</returns>
        V GetSimulationConfiguration(
            string simulator,
            string modelName,
            string calcName);

        /// <summary>
        /// Get the simulation configuration object with the given properties
        /// </summary>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        /// <param name="calcType">Calculation type</param>
        /// <param name="calcTypeUserDefined">User defined calculation type</param>
        /// <returns>Simulation configuration object</returns>
        V GetSimulationConfiguration(
            string simulator,
            string modelName,
            string calcType,
            string calcTypeUserDefined);

        /// <summary>
        /// Persists the configuration library state from memory to the store
        /// </summary>
        /// <param name="token">Cancellation token</param>
        Task StoreLibraryState(CancellationToken token);

        /// <summary>
        /// Verify that the configuration with the given state and object exists in
        /// CDF. In case it does not, should remove from the local state store and
        /// stop tracking it
        /// </summary>
        /// <param name="state">Configuration state</param>
        /// <param name="config">Configuration object</param>
        /// <param name="token">Cancellation token</param>
        /// <returns><c>true</c> in case the configuration exists in CDF, <c>false</c> otherwise</returns>
        Task<bool> VerifyLocalConfigurationState(T state, V config, CancellationToken token);
    }

    /// <summary>
    /// Represent a configuration object for simulation routines
    /// </summary>
    public class SimulationConfigurationWithRoutine : SimulationConfigurationWithDataSampling
    {
        /// <summary>
        /// Times series that will hold simulation output data points
        /// </summary>
        public IEnumerable<OutputTimeSeriesConfiguration> OutputTimeSeries { get; set; }
        
        /// <summary>
        /// Simulation routine
        /// </summary>
        public IEnumerable<CalculationProcedure> Routine { get; set; }
    }

    /// <summary>
    /// Represents the groups (procedures) that contain simulation steps in a routine
    /// </summary>
    public class CalculationProcedure
    {
        /// <summary>
        /// The order in witch to execute this procedure, in relation to other
        /// procedures
        /// </summary>
        public int Order { get; set; }
        
        /// <summary>
        /// Steps contained in this procedure
        /// </summary>
        public IEnumerable<CalculationProcedureStep> Steps { get; set; }
    }

    /// <summary>
    /// Represent a simulation step
    /// </summary>
    public class CalculationProcedureStep
    {
        /// <summary>
        /// Order in which to execute this step
        /// </summary>
        public int Step { get; set; }
        
        /// <summary>
        /// Step type. When using <see cref="RoutineImplementationBase"/> as a base class for a routine,
        /// the valid types are <c>Get</c>, <c>Set</c> and <c>Command</c>
        /// </summary>
        public string Type { get; set; }
        
        /// <summary>
        /// Dictionary containing any argument needed by specific simulator client to execute the step
        /// </summary>
        public Dictionary<string, string> Arguments { get; set; }
    }
    
    /// <summary>
    /// Represents a configuration object for steady state simulations
    /// </summary>
    public class SimulationConfigurationWithDataSampling : SimulationConfigurationBase
    {
        /// <summary>
        /// Data sampling configuration
        /// </summary>
        public DataSamplingConfiguration DataSampling { get; set; }
        
        /// <summary>
        /// Logical check configuration
        /// </summary>
        public LogicalCheckConfiguration LogicalCheck { get; set; }
        
        /// <summary>
        /// Steady state detection configuration
        /// </summary>
        public SteadyStateDetectionConfiguration SteadyStateDetection { get; set; }
        
        /// <summary>
        /// Simulation input time series configuration
        /// </summary>
        public IEnumerable<InputTimeSeriesConfiguration> InputTimeSeries { get; set; }
    }

    /// <summary>
    /// Configures how to sample data from CDF. The validation window is the time length evaluated, 
    /// and the sampling window is the minimum time length that can be used to sample data.
    /// </summary>
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

    /// <summary>
    /// Configures how to run a logical check (e.g. well status check)
    /// </summary>
    public class LogicalCheckConfiguration
    {
        /// <summary>
        /// Whether or not to run logical check for this configuration 
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

    /// <summary>
    /// Configures how to run steady state detection
    /// </summary>
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

    /// <summary>
    /// Time series configuration
    /// </summary>
    public class TimeSeriesConfiguration
    {
        /// <summary>
        /// Input name
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Input type
        /// </summary>
        public string Type { get; set; }
        
        /// <summary>
        /// Input unit (e.g. degC, BARg)
        /// </summary>
        public string Unit { get; set; }
        
        /// <summary>
        /// Input unit type (e.g. Temperature, Pressure)
        /// </summary>
        public string UnitType { get; set; }
    }

    /// <summary>
    /// Output time series configuration
    /// </summary>
    public class OutputTimeSeriesConfiguration :TimeSeriesConfiguration
    {
        /// <summary>
        /// External id of the time series that will contain simulation output data points
        /// </summary>
        public string ExternalId { get; set; }
    }
    
    
    /// <summary>
    /// Input time series configuration
    /// </summary>
    public class InputTimeSeriesConfiguration : TimeSeriesConfiguration
    {        
        /// <summary>
        /// External ID of the time series in CDF containing the input data points
        /// </summary>
        public string SensorExternalId { get; set; }

        /// <summary>
        /// Data points aggregate type conforming to CDF. One of <c>average</c>, <c>max</c>, <c>min</c>, <c>count</c>, 
        /// <c>sum</c>, <c>interpolation</c>, <c>stepInterpolation</c>, <c>totalVariation</c>, <c>continuousVariance</c>, <c>discreteVariance</c>
        /// </summary>
        public string AggregateType { get; set; }

        /// <summary>
        /// External ID to use when saving the input sample in CDF
        /// </summary>
        public string SampleExternalId { get; set; }
    }
    
    /// <summary>
    /// Simulation schedule configuration
    /// </summary>
    public class ScheduleConfiguration
    {
        /// <summary>
        /// Whether or not to run on schedule
        /// </summary>
        public bool Enabled { get; set; }
        
        /// <summary>
        /// Start time in milliseconds since Unix epoch
        /// </summary>
        public long Start { get; set; }

        /// <summary>
        /// Simulation frequency. The format it <c>number(w|d|h|m|s)</c>
        /// </summary>
        public string Repeat { get; set; }

        /// <summary>
        /// Start time as a <see cref="DateTime"/> object
        /// </summary>
        public DateTime StartDate => CogniteTime.FromUnixTimeMilliseconds(Start);
        
        /// <summary>
        /// Simulation frequency as a <see cref="TimeSpan"/> object
        /// </summary>
        public TimeSpan RepeatTimeSpan => SimulationUtils.ConfigurationTimeStringToTimeSpan(Repeat);
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
        /// Simulation schedule configuration
        /// </summary>
        public ScheduleConfiguration Schedule { get; set; }

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
        private long? _lastRun;

        /// <summary>
        /// Timestamp of the last time a simulation was ran using this configuration
        /// </summary>
        public long? LastRun
        {
            get => _lastRun;
            set
            {
                if (value == _lastRun) return;
                LastTimeModified = DateTime.UtcNow;
                _lastRun = value;
            }
        }

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
                _lastRun = statePoco.LastRun;
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
                LastRun = LastRun,
            };
        }
    }

    /// <summary>
    /// Data object that contains the simulation configuration state properties to be persisted
    /// by the state store. These properties are restored to the state on initialization
    /// </summary>
    public class ConfigurationStateBasePoco : FileStatePoco
    {
        /// <summary>
        /// Timestamp of the last simulation run
        /// </summary>
        [StateStoreProperty("last-run")]
        public long? LastRun { get; set; }

        /// <summary>
        /// External ID of the sequence in CDF containing the run configuration
        /// </summary>
        [StateStoreProperty("run-data-sequence")]
        public string RunDataSequence { get; set; }
        
        /// <summary>
        /// Index of the last row with data in the run configuration
        /// </summary>
        [StateStoreProperty("run-sequence-last-row")]
        public long RunSequenceLastRow { get; set; }
    }
}
