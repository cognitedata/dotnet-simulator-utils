using Cognite.Extractor.Common;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents a local routine revision library.
    /// It fetches all routine revisions from CDF and stores them in memory.
    /// It also stores the state of the routine revisions (e.g. scheduling params) in a local state store (LocalLibrary).
    /// </summary>
    /// <typeparam name="T">Type of the state object used in this library</typeparam>
    /// <typeparam name="U">Type of the data object used to serialize and deserialize state</typeparam>
    /// <typeparam name="V">Configuration object type. The contents of the routine revision
    /// to an object of this type. properties of this object should use pascal case while the JSON
    /// properties should be lower camel case</typeparam>
    public abstract class ConfigurationLibraryBase<T, U, V> : LocalLibrary<T, U>, IConfigurationProvider<T, V>
        where T : ConfigurationStateBase
        where U : FileStatePoco
        where V : SimulationConfigurationWithRoutine
    {
        /// <inheritdoc/>
        public Dictionary<string, V> SimulationConfigurations { get; }

        private IList<SimulatorConfig> _simulators;
        /// <inheritdoc/>
        protected CogniteSdk.Resources.Alpha.SimulatorsResource CdfSimulatorResources { get; private set; }

        /// <summary>
        /// Creates a new instance of the library using the provided parameters
        /// </summary>
        /// <param name="config">Library configuration</param>
        /// <param name="simulators">Dictionary of simulators</param>
        /// <param name="cdf">CDF destination object</param>
        /// <param name="logger">Logger</param>
        /// <param name="store">State store for revisions state</param>
        public ConfigurationLibraryBase(
            FileLibraryConfig config,
            IList<SimulatorConfig> simulators,
            CogniteDestination cdf,
            ILogger logger,
            IExtractionStateStore store = null) :
            base(config, logger, store)
        {
            CdfSimulatorResources = cdf.CogniteClient.Alpha.Simulators;
            SimulationConfigurations = new Dictionary<string, V>();
            _simulators = simulators;
        }

        /// <summary>
        /// Fetch routine revisions from CDF and store them in memory and state store
        /// This method is used to populate the local routine library with the latest routine revisions "on demand", i.e. right upon simulation run
        /// </summary>
        private async Task<(V, T)> TryReadRoutineRevisionFromCdf(string routineRevisionExternalId)
        {
            Logger.LogInformation("Local routine revision {Id} not found, attempting to fetch from remote", routineRevisionExternalId);
            try {
                var routineRevisionRes = await CdfSimulatorResources.RetrieveSimulatorRoutineRevisionsAsync(
                    new List<CogniteSdk.Identity> { new CogniteSdk.Identity(routineRevisionExternalId) }
                ).ConfigureAwait(false);
                var routineRevision = routineRevisionRes.FirstOrDefault();
                if (routineRevision != null)
                {
                    return ReadAndSaveRoutineRevision(routineRevision);
                }
            } catch (CogniteException e) {
                Logger.LogError(e, "Cannot find routine revision {Id} on remote", routineRevisionExternalId);
            }
            return (null, null);
        }

        /// <summary>
        /// Looks for the routine revision in the memory with the given external id
        /// </summary>
        public V GetSimulationConfiguration(
            string routineRevisionExternalId
            )
        {
            var calcConfigs = SimulationConfigurations.Values.Where(c => c.ExternalId == routineRevisionExternalId);
            if (calcConfigs.Any())
            {
                return calcConfigs.First();
            }

            (V calcConfig, _) = TryReadRoutineRevisionFromCdf(routineRevisionExternalId).GetAwaiter().GetResult();

            return calcConfig;
        }

        /// <inheritdoc/>
        public T GetSimulationConfigurationState(
            string routineRevisionExternalId)
        {
            var calcConfigs = SimulationConfigurations
                .Where(c => c.Value.ExternalId == routineRevisionExternalId);

            if (calcConfigs.Any())
            {
                var id = calcConfigs.First().Key;
                if (State.TryGetValue(id, out var configState))
                {
                    return configState;
                }
            }

            (_, T newConfigState) = TryReadRoutineRevisionFromCdf(routineRevisionExternalId).GetAwaiter().GetResult();

            return newConfigState;
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
            var revisionRes = await CdfSimulatorResources.RetrieveSimulatorRoutineRevisionsAsync(
                new List<CogniteSdk.Identity> { new CogniteSdk.Identity(long.Parse(state.Id)) },
                token
            ).ConfigureAwait(false);
            if (revisionRes.Count() == 1)
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
        /// Fetch routine revisions from CDF and store them in memory
        /// </summary>
        /// <param name="token">Cancellation token</param>
        protected override void FetchRemoteState(CancellationToken token)
        {
            Task.Run(() => ReadConfigurations(token), token).Wait(token);
        }

        protected virtual T StateFromRoutineRevision(SimulatorRoutineRevision routineRevision)
        {
            throw new NotImplementedException();
        }

        protected virtual V LocalConfigurationFromRoutine(SimulationConfigurationWithRoutine simulationConfigurationWithRoutine) {
            return (V) simulationConfigurationWithRoutine;
        }

        private (V, T) ReadAndSaveRoutineRevision(SimulatorRoutineRevision routineRev) {
            V localConfiguration = null;
            T rState = null;
            if (routineRev.Script == null)
            {
                Logger.LogWarning("Skipping routine revision {Id} because it has no routine", routineRev.Id);
                return (localConfiguration, rState);
            }
            
            var simulators = _simulators.ToDictionary(s => s.Name, s => s);
            // TODO: we should rather use the new type natively
            var simulationConfigurationWithRoutine = new SimulationConfigurationWithRoutine()
            {
                ExternalId = routineRev.ExternalId,
                Simulator = simulators[routineRev.SimulatorExternalId].Name,
                ModelName = routineRev.ModelExternalId,
                CalculationName = routineRev.RoutineExternalId,
                CalculationType = "UserDefined",
                CalcTypeUserDefined = routineRev.RoutineExternalId,
                Connector = routineRev.SimulatorIntegrationExternalId,
                Schedule = new ScheduleConfiguration()
                {
                    Enabled = routineRev.Configuration.Schedule.Enabled,
                    Start = routineRev.Configuration.Schedule.StartTime ?? 0, // TODO what's the default value here?
                    Repeat = routineRev.Configuration.Schedule.Repeat
                },
                InputConstants = routineRev.Configuration.InputConstants.Select(ic => new InputConstantConfiguration()
                {
                    Name = ic.Name,
                    Type = ic.ReferenceId,
                    Unit = ic.Unit,
                    UnitType = ic.UnitType,
                    Value = ic.Value,
                    SaveTimeseriesExternalId = ic.SaveTimeseriesExternalId
                }),
                InputTimeSeries = routineRev.Configuration.InputTimeseries.Select(its => new InputTimeSeriesConfiguration()
                {
                    Name = its.Name,
                    Type = its.ReferenceId,
                    Unit = its.Unit,
                    UnitType = its.UnitType,
                    SensorExternalId = its.SourceExternalId,
                    AggregateType = its.Aggregate,
                    SampleExternalId = its.SaveTimeseriesExternalId
                }),
                OutputTimeSeries = routineRev.Configuration.OutputTimeseries.Select(ots => new OutputTimeSeriesConfiguration()
                {
                    Name = ots.Name,
                    Type = ots.ReferenceId,
                    Unit = ots.Unit,
                    UnitType = ots.UnitType,
                    ExternalId = ots.SaveTimeseriesExternalId
                }),
                DataSampling = new DataSamplingConfiguration()
                {
                    ValidationWindow = routineRev.Configuration.DataSampling.ValidationWindow,
                    SamplingWindow = routineRev.Configuration.DataSampling.SamplingWindow,
                    Granularity = routineRev.Configuration.DataSampling.Granularity,
                    ValidationEndOffset = routineRev.Configuration.DataSampling.ValidationEndOffset
                },
                LogicalCheck = new LogicalCheckConfiguration()
                {
                    Enabled = routineRev.Configuration.LogicalCheck.Enabled,
                    ExternalId = routineRev.Configuration.LogicalCheck.TimeseriesExternalId,
                    AggregateType = routineRev.Configuration.LogicalCheck.Aggregate,
                    Check = routineRev.Configuration.LogicalCheck.Operator,
                    Value = routineRev.Configuration.LogicalCheck.Value ?? 0 // TODO what's the default value here?
                },
                SteadyStateDetection = new SteadyStateDetectionConfiguration()
                {
                    Enabled = routineRev.Configuration.SteadyStateDetection.Enabled,
                    ExternalId = routineRev.Configuration.SteadyStateDetection.TimeseriesExternalId,
                    AggregateType = routineRev.Configuration.SteadyStateDetection.Aggregate,
                    MinSectionSize = routineRev.Configuration.SteadyStateDetection.MinSectionSize ?? 0, // TODO what's the default value here?
                    VarThreshold = routineRev.Configuration.SteadyStateDetection.VarThreshold ?? 0, // TODO what's the default value here?
                    SlopeThreshold = routineRev.Configuration.SteadyStateDetection.SlopeThreshold ?? 0 // TODO what's the default value here?
                },
                UserEmail = "",
                Routine = routineRev.Script.Select((s, i) => new CalculationProcedure()
                {
                    Order = i,
                    Steps = s.Steps.Select((step, j) => new CalculationProcedureStep()
                    {
                        Step = j,
                        Type = step.StepType,
                        Arguments = step.Arguments
                    })
                }),
                CreatedTime = routineRev.CreatedTime
            };
            localConfiguration = LocalConfigurationFromRoutine(simulationConfigurationWithRoutine);
            SimulationConfigurations.Add(routineRev.Id.ToString(), localConfiguration);

            rState = StateFromRoutineRevision(routineRev);
            if (rState != null)
            {   
                var revisionId = routineRev.Id.ToString();
                if (!State.ContainsKey(revisionId))
                {
                    // If the revision does not exist locally, add it to the state store
                    State.Add(revisionId, rState);
                }
            }
            return (localConfiguration, rState);
        }

        private async Task ReadConfigurations(CancellationToken token)
        {
            var routineRevisionsRes = await CdfSimulatorResources.ListSimulatorRoutineRevisionsAsync(
                new SimulatorRoutineRevisionQuery()
                {
                    Filter = new SimulatorRoutineRevisionFilter()
                    {
                        // TODO filter by created time, simulatorIntegrationExternalIds
                        // CreatedTime = new CogniteSdk.TimeRange() {  Min = _libState.DestinationExtractedRange.Last.ToUnixTimeMilliseconds() + 1 },
                        SimulatorExternalIds = _simulators.Select(s => s.Name).ToList(),
                    }
                },
                token
            ).ConfigureAwait(false);

            // TODO: what do we do with the timerange now that we don't use FileLibrary?
            // TODO: we need our own _libState
            var routineRevisions = routineRevisionsRes.Items;

            foreach (var routineRev in routineRevisions)
            {
                if (!SimulationConfigurations.ContainsKey(routineRev.Id.ToString()))
                {
                    ReadAndSaveRoutineRevision(routineRev);
                }
            }
        }
    }



    /// <summary>
    /// Interface for libraries that can provide configuration information
    /// </summary>
    /// <typeparam name="T">Configuration state type</typeparam>
    /// <typeparam name="V">Configuration object type</typeparam>
    public interface IConfigurationProvider<T, V>
    {
        /// <summary>
        /// Dictionary of simulation configurations. The key is the file external ID
        /// </summary>
        Dictionary<string, V> SimulationConfigurations { get; }

        /// <summary>
        /// Get the simulator configuration state object with the given parameter
        /// </summary>
        /// <param name="routineRevisionExternalId">Routine revision external id</param>
        /// <returns>Simulation configuration state object</returns>
        T GetSimulationConfigurationState(
            string routineRevisionExternalId);
        
    
        /// <summary>
        /// Get the simulation configuration object with the given property
        /// </summary>
        /// <param name="routinerRevisionExternalId">Simulator name</param>
        /// <returns>Simulation configuration state object</returns>
        V GetSimulationConfiguration(
            string routinerRevisionExternalId);


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
        /// Simulation manual value inputs configuration
        /// </summary>
        public IEnumerable<InputConstantConfiguration> InputConstants { get; set; }

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
    public class OutputTimeSeriesConfiguration : TimeSeriesConfiguration
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
    /// Manually input the value into routine
    /// </summary>
    public class InputConstantConfiguration
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

        /// <summary>
        /// The value of the manual input
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// External ID to use when saving the input sample in CDF
        /// </summary>
        public string SaveTimeseriesExternalId { get; set; }
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
        /// External ID of the simulation configuration
        /// </summary>
        public string ExternalId { get; set; }

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
            ExternalId = ExternalId,
            Model = new SimulatorModelInfo
            {
                Name = ModelName,
                Simulator = Simulator
            },
            Name = CalculationName,
            Type = CalculationType,
            UserDefinedType = CalcTypeUserDefined
        };

        /// <summary>
        /// Created time
        /// </summary>
        public long CreatedTime { get; set; }
    }

    /// <summary>
    /// This base class represents the state of a simulation configuration file
    /// </summary>
    public class ConfigurationStateBase : FileState
    {
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
        /// Initialize this simulation configuration state using a data object from the state store
        /// </summary>
        /// <param name="poco">Data object</param>
        public override void Init(FileStatePoco poco)
        {
            base.Init(poco);
            if (poco is ConfigurationStateBasePoco statePoco)
            {
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
    }
}
