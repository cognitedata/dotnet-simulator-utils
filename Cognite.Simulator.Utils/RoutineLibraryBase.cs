using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extensions;
using Cognite.Extractor.Common;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

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
        public ConcurrentDictionary<long, SimulatorRoutineRevisionInfo> RoutineRevisions { get; }

        private readonly ILogger _logger;
        private readonly RoutineLibraryConfig _config;

        private SimulatorCreate _simulatorDefinition;
        /// <inheritdoc/>
        protected CogniteSdk.Resources.Alpha.SimulatorsResource CdfSimulatorResources { get; private set; }

        /// <summary>
        ///  In memory extraction state for the library.
        ///  Keeps track of the time range of routine revisions that have been fetched.
        /// </summary>
        protected BaseExtractionState LibState { get; private set; }

        /// <summary>
        ///     Limit for pagination when fetching routine revisions from CDF.
        /// </summary>
        public int? PaginationLimit { get; set; } = 20;

        /// <summary>
        /// Creates a new instance of the library using the provided parameters
        /// </summary>
        /// <param name="config">Library configuration</param>
        /// <param name="simulatorDefinition">Simulator definition</param>
        /// <param name="cdf">CDF destination object</param>
        /// <param name="logger">Logger</param>
        public RoutineLibraryBase(
            RoutineLibraryConfig config,
            SimulatorCreate simulatorDefinition,
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
            RoutineRevisions = new ConcurrentDictionary<long, SimulatorRoutineRevisionInfo>();
            LibState = new BaseExtractionState("RoutineLibraryState");
            _simulatorDefinition = simulatorDefinition;
        }


        /// <summary>
        /// Initializes the routine library. Finds entities in CDF and caches them in memory.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Init(CancellationToken token)
        {
            await ReadRoutineRevisions(true, token).ConfigureAwait(false);
        }


        /// <summary>
        /// Fetches full routine revision information from CDF, given its external ID.
        /// </summary>
        public async Task<V> GetRoutineRevision(
            string routineRevisionExternalId
        )
        {
            _logger.LogDebug("Fetching routine revision {Id} from remote", routineRevisionExternalId);
            try
            {
                var routineRevisionRes = await CdfSimulatorResources.RetrieveSimulatorRoutineRevisionsAsync(
                    new List<CogniteSdk.Identity> { new CogniteSdk.Identity(routineRevisionExternalId) }
                ).ConfigureAwait(false);
                var routineRevision = routineRevisionRes.FirstOrDefault();
                if (routineRevision != null)
                {
                    return LocalConfigurationFromRoutine(routineRevision);
                }
            }
            catch (CogniteException e)
            {
                _logger.LogError(e, "Cannot find routine revision {Id} on remote", routineRevisionExternalId);
            }
            return null;
        }

        /// <summary>
        /// Verifies that the routine revision exists in CDF.
        /// In case it does not, should remove from memory.
        /// <param name="config">Configuration object</param>
        /// <param name="token">Cancellation token</param>
        /// <returns><c>true</c> in case the configuration exists in CDF, <c>false</c> otherwise</returns>
        /// </summary>
        public async Task<bool> VerifyInMemoryCache(
            SimulatorRoutineRevisionInfo config,
            CancellationToken token)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            bool exists = false;

            try
            {
                var revisionRes = await CdfSimulatorResources.RetrieveSimulatorRoutineRevisionsAsync(
                    new List<CogniteSdk.Identity> { new CogniteSdk.Identity(config.Id) },
                    token
                ).ConfigureAwait(false);
                exists = revisionRes.Count() == 1;
            }
            catch (CogniteException e)
            {
                _logger.LogError(e, "Cannot find routine revision {Id} on remote", config.Id);
            }

            if (!exists)
            {
                _logger.LogWarning("Removing {Model} - {Routine} routine revision, not found in CDF",
                    config.Model.ExternalId,
                    config.ExternalId);
                RoutineRevisions.TryRemove(config.Id, out _);
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


                await ReadRoutineRevisions(false, token).ConfigureAwait(false);

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
        protected virtual V LocalConfigurationFromRoutine(SimulatorRoutineRevision routineRevision)
        {
            return (V)routineRevision;
        }

        private void ReadAndSaveRoutineRevision(SimulatorRoutineRevision routineRev)
        {
            var newRevision = new SimulatorRoutineRevisionInfo(routineRev);

            RoutineRevisions.AddOrUpdate(routineRev.Id, newRevision, (key, oldValue) =>
            {
                if (newRevision.CreatedTime < oldValue.CreatedTime)
                {
                    return oldValue;
                }
                return newRevision;
            });
        }

        private async Task ReadRoutineRevisions(bool init, CancellationToken token)
        {

            if (init)
            {
                _logger.LogInformation("Updating routine library from scratch.");
            }
            else
            {
                string lastTimestamp = LibState.DestinationExtractedRange.IsEmpty ? "n/a" : LibState.DestinationExtractedRange.Last.ToString();
                _logger.LogDebug("Updating routine library. There are currently {Num} routine revisions. Extracted until: {LastTime}",
                    RoutineRevisions.Count,
                    lastTimestamp
                );
            }

            long createdAfter =
                !init && !LibState.DestinationExtractedRange.IsEmpty ?
                    LibState.DestinationExtractedRange.Last.ToUnixTimeMilliseconds() : 0;

            var routineRevisionsRes = ApiUtils.FollowCursor(
                new SimulatorRoutineRevisionQuery()
                {
                    Filter = new SimulatorRoutineRevisionFilter()
                    {
                        // TODO filter by simulatorIntegrationExternalIds
                        SimulatorExternalIds = [_simulatorDefinition.ExternalId],
                        CreatedTime = new CogniteSdk.TimeRange() { Min = createdAfter + 1 },
                    },
                    Limit = PaginationLimit,
                },
                CdfSimulatorResources.ListSimulatorRoutineRevisionsAsync,
                token);

            var routineRevisions = await routineRevisionsRes.ToListAsync(token).ConfigureAwait(false);

            if (routineRevisions.Any())
            {
                foreach (var routineRev in routineRevisions)
                {
                    ReadAndSaveRoutineRevision(routineRev);
                }

                var maxCreatedTimestamp = RoutineRevisions
                    .Select(s => s.Value.CreatedTime)
                    .Max();

                var maxCreatedDateTime = CogniteTime.FromUnixTimeMilliseconds(maxCreatedTimestamp);
                LibState.UpdateDestinationRange(
                    maxCreatedDateTime,
                    maxCreatedDateTime);
                _logger.LogDebug("Updated routine library with {Num} routine revisions. Extracted until: {MaxTime}",
                    routineRevisions.Count(),
                    maxCreatedDateTime
                );
            }
        }
    }


    /// <summary>
    /// A default instance of the routine library.
    /// </summary>
    public class DefaultRoutineLibrary<TAutomationConfig> :
        RoutineLibraryBase<SimulatorRoutineRevision>
        where TAutomationConfig : AutomationConfig, new()
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultRoutineLibrary{TAutomationConfig}"/> class.
        /// </summary>
        /// <param name="config">The default configuration.</param>
        /// <param name="simulatorDefinition">The simulator definition.</param>
        /// <param name="cdf">The CDF destination object.</param>
        /// <param name="logger">The logger.</param>
        public DefaultRoutineLibrary(
            DefaultConfig<TAutomationConfig> config,
            SimulatorCreate simulatorDefinition,
            CogniteDestination cdf,
            ILogger<DefaultRoutineLibrary<TAutomationConfig>> logger) :
            base(config?.Connector.RoutineLibrary, simulatorDefinition, cdf, logger)
        {
        }
    }



    /// <summary>
    /// Interface for library that can provide routine configuration information
    /// </summary>
    /// <typeparam name="V">Configuration object type</typeparam>
    public interface IRoutineProvider<V>
    {
        /// <summary>
        /// Dictionary of routine revisions info. The key is the routine revision ID.
        /// This only includes some basic information about the routine revision, and not larger fields such as script, configuration inputs or outputs.
        /// </summary>
        ConcurrentDictionary<long, SimulatorRoutineRevisionInfo> RoutineRevisions { get; }

        /// <summary>
        /// Initializes the library
        /// </summary>
        Task Init(CancellationToken token);

        /// <summary>
        /// Get the simulation configuration object with the given property
        /// </summary>
        /// <param name="routineRevisionExternalId">Simulator name</param>
        /// <returns>Simulation configuration state object</returns>
        Task<V> GetRoutineRevision(
            string routineRevisionExternalId);

        /// <summary>
        /// Get the tasks that are running in the library
        /// </summary>
        IEnumerable<Task> GetRunTasks(CancellationToken token);

        /// <summary>
        /// Verify that the routine revision exists in CDF.
        /// In case it does not, should remove from memory.
        /// </summary>
        /// <param name="routineRevision">Routine revision info object</param>
        /// <param name="token">Cancellation token</param>
        /// <returns><c>true</c> in case the configuration exists in CDF, <c>false</c> otherwise</returns>
        Task<bool> VerifyInMemoryCache(SimulatorRoutineRevisionInfo routineRevision, CancellationToken token);
    }
}
