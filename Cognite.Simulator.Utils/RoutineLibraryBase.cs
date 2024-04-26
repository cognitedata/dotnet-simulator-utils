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
    /// </summary>
    /// <typeparam name="V">Routine revision object type.</typeparam>
    public abstract class RoutineLibraryBase<V> : IRoutineProvider<V>
        where V : SimulatorRoutineRevision
    {
        /// <inheritdoc/>
        public Dictionary<string, V> RoutineRevisions { get; }

        private readonly ILogger _logger;
        private readonly RoutineLibraryConfig _config;

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
        public RoutineLibraryBase(
            RoutineLibraryConfig config,
            IList<SimulatorConfig> simulators,
            CogniteDestination cdf,
            ILogger logger)
        {
            if (cdf == null)
            {
                throw new ArgumentNullException(nameof(cdf));
            }
            _logger = logger;
            _config = config;

            CdfSimulatorResources = cdf.CogniteClient.Alpha.Simulators;
            RoutineRevisions = new Dictionary<string, V>();
            _simulators = simulators;
        }
        

        /// <summary>
        /// Initializes the routine library. Finds entities in CDF and caches them in memory.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Init(CancellationToken token)
        {            
            await ReadRoutineRevisions(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Fetch routine revisions from CDF and store them in memory and state store
        /// This method is used to populate the local routine library with the latest routine revisions "on demand", i.e. right upon simulation run
        /// </summary>
        private async Task<V> TryReadRoutineRevisionFromCdf(string routineRevisionExternalId)
        {
            _logger.LogInformation("Local routine revision {Id} not found, attempting to fetch from remote", routineRevisionExternalId);
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
                _logger.LogError(e, "Cannot find routine revision {Id} on remote", routineRevisionExternalId);
            }
            return null;
        }

        /// <summary>
        /// Looks for the routine revision in the memory with the given external id
        /// </summary>
        public V GetRoutineRevision(
            string routineRevisionExternalId
        )
        {
            var revisions = RoutineRevisions.Values.Where(c => c.ExternalId == routineRevisionExternalId);
            if (revisions.Any())
            {
                return revisions.First();
            }

            V calcConfig = TryReadRoutineRevisionFromCdf(routineRevisionExternalId).GetAwaiter().GetResult();

            return calcConfig;
        }

        /// <summary>
        /// Verifies that the routine revision exists in CDF.
        /// In case it does not, should remove from memory.
        /// </summary>
        public async Task<bool> VerifyInMemoryCache(
            V config,
            CancellationToken token)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            var revisionRes = await CdfSimulatorResources.RetrieveSimulatorRoutineRevisionsAsync(
                new List<CogniteSdk.Identity> { new CogniteSdk.Identity(config.Id) },
                token
            ).ConfigureAwait(false);
            if (revisionRes.Count() == 1)
            {
                return true;
            }

            _logger.LogWarning("Removing {Model} - {Simulation} routine revision, not found in CDF",
                // state.ModelName,
                config.ModelExternalId,
                config.ExternalId);
            // State.Remove(state.Id);
            RoutineRevisions.Remove(config.Id.ToString());
            // await RemoveStates(new List<FileState> { state }, token).ConfigureAwait(false);
            return false;
        }

        // /// <summary>
        // /// Fetch routine revisions from CDF and store them in memory
        // /// </summary>
        // /// <param name="token">Cancellation token</param>
        // protected void FetchRemoteState(CancellationToken token)
        // {
        //     Task.Run(() => ReadConfigurations(token), token).Wait(token);
        // }

         /// <summary>
        /// Periodically searches for entities CDF, in case new ones are found, store locally.
        /// Entities are saved with the internal CDF id as name
        /// </summary>
        /// <param name="token">Cancellation token</param>
        private async Task FetchAndProcessRemoteState(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // string timeRange = _libState.DestinationExtractedRange.IsEmpty ? "Empty" : _libState.DestinationExtractedRange.ToString();
                // Logger.LogDebug("Updating entity library. There are currently {Num} entities. Extracted range: {TimeRange}",
                //     State.Count,
                //     timeRange
                //     );


                await ReadRoutineRevisions(token).ConfigureAwait(false);

                // if (State.Any())
                // {
                //     var maxUpdatedMs = State
                //         .Select(s => s.Value.UpdatedTime)
                //         .Max();
                //     var maxUpdatedDt = CogniteTime.FromUnixTimeMilliseconds(maxUpdatedMs);
                //     _libState.UpdateDestinationRange(
                //         maxUpdatedDt,
                //         maxUpdatedDt);
                // }

                await Task
                    .Delay(TimeSpan.FromSeconds(_config.LibraryUpdateInterval), token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates a list of the tasks performed by this library.
        /// These include fetching the list of remote entities and saving state.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of tasks</returns>
        public IEnumerable<Task> GetRunTasks(CancellationToken token)
        {
            return new List<Task> { FetchAndProcessRemoteState(token) };
        }


        // /// <summary>
        // /// Convert a routine revision to a state object of type <typeparamref name="T"/>
        // /// </summary>
        // protected virtual T StateFromRoutineRevision(SimulatorRoutineRevision routineRevision)
        // {
        //     throw new NotImplementedException();
        // }

        /// <summary>
        /// Convert a routine revision to a configuration object of type <typeparamref name="V"/>
        /// Generally not advised on overriding this method.
        /// </summary>
        protected virtual V LocalConfigurationFromRoutine(SimulatorRoutineRevision routineRevision) {
            return (V) routineRevision;
        }

        private V ReadAndSaveRoutineRevision(SimulatorRoutineRevision routineRev) {
            // V localConfiguration = null;
            // T rState = null;
            // if (routineRev.Script == null)
            // {
            //     _logger.LogWarning("Skipping routine revision {Id} because it has no routine", routineRev.Id);
            //     return localConfiguration;
            // }
            
            V localConfiguration = LocalConfigurationFromRoutine(routineRev);
            RoutineRevisions.Add(routineRev.Id.ToString(), localConfiguration);

            // rState = StateFromRoutineRevision(routineRev);
            // if (rState != null)
            // {   
            //     var revisionId = routineRev.Id.ToString();
            //     if (!State.ContainsKey(revisionId))
            //     {
            //         // If the revision does not exist locally, add it to the state store
            //         State.Add(revisionId, rState);
            //     }
            // }
            return localConfiguration;
        }

        private async Task ReadRoutineRevisions(CancellationToken token)
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
                if (!RoutineRevisions.ContainsKey(routineRev.Id.ToString()))
                {
                    ReadAndSaveRoutineRevision(routineRev);
                }
            }
        }

        // public Task StoreLibraryState(CancellationToken token)
        // {
        //     throw new NotImplementedException();
        // }
    }



    /// <summary>
    /// Interface for libraries that can provide routine configuration information
    /// </summary>
    /// <typeparam name="V">Configuration object type</typeparam>
    public interface IRoutineProvider<V>
    {
        /// <summary>
        /// Dictionary of simulation routines. The key is the routine revision id
        /// </summary>
        Dictionary<string, V> RoutineRevisions { get; }

        /// <summary>
        /// Initializes the library
        /// </summary>
        Task Init(CancellationToken token);

        // /// <summary>
        // /// Get the simulator configuration state object with the given parameter
        // /// </summary>
        // /// <param name="routineRevisionExternalId">Routine revision external id</param>
        // /// <returns>Simulation configuration state object</returns>
        // T GetSimulationConfigurationState(
        //     string routineRevisionExternalId);
        
    
        /// <summary>
        /// Get the simulation configuration object with the given property
        /// </summary>
        /// <param name="routinerRevisionExternalId">Simulator name</param>
        /// <returns>Simulation configuration state object</returns>
        V GetRoutineRevision(
            string routinerRevisionExternalId);

        /// <summary>
        /// Get the tasks that are running in the library
        /// </summary>
        IEnumerable<Task> GetRunTasks(CancellationToken token);


        // /// <summary>
        // /// Persists the configuration library state from memory to the store
        // /// </summary>
        // /// <param name="token">Cancellation token</param>
        // Task StoreLibraryState(CancellationToken token);

        /// <summary>
        /// Verify that the routine revision exists in CDF.
        /// In case it does not, should remove from memory.
        /// </summary>
        /// <param name="config">Configuration object</param>
        /// <param name="token">Cancellation token</param>
        /// <returns><c>true</c> in case the configuration exists in CDF, <c>false</c> otherwise</returns>
        Task<bool> VerifyInMemoryCache(V config, CancellationToken token);
    }
}
