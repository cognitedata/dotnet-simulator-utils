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
            var revisions = RoutineRevisions.Values.Where(c => c.ExternalId == routineRevisionExternalId).OrderByDescending(c => c.CreatedTime);
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

            bool exists = false;

            try {
                var revisionRes = await CdfSimulatorResources.RetrieveSimulatorRoutineRevisionsAsync(
                    new List<CogniteSdk.Identity> { new CogniteSdk.Identity(config.Id) },
                    token
                ).ConfigureAwait(false);
                exists = revisionRes.Count() == 1;
            } catch (CogniteException e) {
                _logger.LogError(e, "Cannot find routine revision {Id} on remote", config.Id);
            }

            if (!exists)
            {
                _logger.LogWarning("Removing {Model} - {Routine} routine revision, not found in CDF",
                    config.ModelExternalId,
                    config.ExternalId);
                RoutineRevisions.Remove(config.Id.ToString());
            }
            return exists;
        }

        /// <summary>
        /// Periodically searches for routine revisions CDF, in case new ones are found, store locally.
        /// Entities are saved with the internal CDF id as name
        /// </summary>
        /// <param name="token">Cancellation token</param>
        private async Task FetchAndProcessRemoteRoutines(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                _logger.LogDebug("Updating routine revisions library. There are currently {Num} entities.",
                    RoutineRevisions.Count);


                await ReadRoutineRevisions(token).ConfigureAwait(false);

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
            return new List<Task> { FetchAndProcessRemoteRoutines(token) };
        }

        /// <summary>
        /// Convert a routine revision to a configuration object of type <typeparamref name="V"/>
        /// Generally not advised on overriding this method.
        /// </summary>
        protected virtual V LocalConfigurationFromRoutine(SimulatorRoutineRevision routineRevision) {
            return (V) routineRevision;
        }

        private V ReadAndSaveRoutineRevision(SimulatorRoutineRevision routineRev) {
            
            V localConfiguration = LocalConfigurationFromRoutine(routineRev);
            RoutineRevisions.Add(routineRev.Id.ToString(), localConfiguration);

            return localConfiguration;
        }

        private async Task ReadRoutineRevisions(CancellationToken token)
        {
            var routineRevisionsRes = await CdfSimulatorResources.ListSimulatorRoutineRevisionsAsync(
                new SimulatorRoutineRevisionQuery()
                {
                    // TODO we should only fetch latest revisions here
                    Filter = new SimulatorRoutineRevisionFilter()
                    {
                        // TODO filter by simulatorIntegrationExternalIds
                        SimulatorExternalIds = _simulators.Select(s => s.Name).ToList(),
                    }
                },
                token
            ).ConfigureAwait(false);

            var routineRevisions = routineRevisionsRes.Items;

            foreach (var routineRev in routineRevisions)
            {
                if (!RoutineRevisions.ContainsKey(routineRev.Id.ToString()))
                {
                    ReadAndSaveRoutineRevision(routineRev);
                }
            }
        }
    }



    /// <summary>
    /// Interface for library that can provide routine configuration information
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
