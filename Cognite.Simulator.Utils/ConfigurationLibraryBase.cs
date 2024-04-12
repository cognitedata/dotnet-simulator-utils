using Cognite.Extractor.Common;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
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
        where T : FileState
        where U : FileStatePoco
        where V : SimulatorRoutineRevision
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
            FileState state,
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

            Logger.LogWarning("Removing {Model} - {Simulation} calculation configuration, not found in CDF",
                state.ModelName,
                config.RoutineExternalId);
            State.Remove(state.Id);
            SimulationConfigurations.Remove(state.Id);
            await RemoveStates(new List<FileState> { state }, token).ConfigureAwait(false);
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

        /// <summary>
        /// Convert a routine revision to a state object of type <typeparamref name="T"/>
        /// </summary>
        protected virtual T StateFromRoutineRevision(SimulatorRoutineRevision routineRevision)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Convert a routine revision to a configuration object of type <typeparamref name="V"/>
        /// Generally not advised on overriding this method.
        /// </summary>
        protected virtual V LocalConfigurationFromRoutine(SimulatorRoutineRevision routineRevision) {
            return (V) routineRevision;
        }

        private (V, T) ReadAndSaveRoutineRevision(SimulatorRoutineRevision routineRev) {
            V localConfiguration = null;
            T rState = null;
            if (routineRev.Script == null)
            {
                Logger.LogWarning("Skipping routine revision {Id} because it has no routine", routineRev.Id);
                return (localConfiguration, rState);
            }
            
            localConfiguration = LocalConfigurationFromRoutine(routineRev);
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
        Task<bool> VerifyLocalConfigurationState(FileState state, V config, CancellationToken token);
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
    }
}
