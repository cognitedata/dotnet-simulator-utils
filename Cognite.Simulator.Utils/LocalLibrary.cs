using Cognite.Extractor.Common;
using Cognite.Extractor.StateStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents a local entity library.
    /// Can be used to persist a list of remote entities locally together with
    /// their state. Simpler version of the <see cref="FileLibrary{T, U}"/> class, that doesn't handle file download.
    /// </summary>
    /// <typeparam name="T">Type of the state object used in this library</typeparam>
    /// <typeparam name="U">Type of the data object used to serialize and deserialize state</typeparam>
    public abstract class LocalLibrary<T, U>
        where T : FileState
        where U : FileStatePoco
    {
        /// <summary>
        /// Dictionary holding the entity states. The keys are the external ids of the
        /// CDF resources and the values are the state objects of type <typeparamref name="T"/>
        /// </summary>
        public Dictionary<string, T> State { get; private set; }

        /// <summary>
        /// Logger object
        /// </summary>
        protected ILogger Logger { get; private set; }

        // Other injected services
        private readonly FileLibraryConfig _config;
        private readonly IExtractionStateStore _store;

        // Internal objects
        private readonly BaseExtractionState _libState;

        /// <summary>
        /// Creates a new instance of this library using the provided parameters.
        /// These parameters are intended to be injected by using a <see cref="IServiceCollection"/>
        /// </summary>
        /// <param name="config">Library configuration</param>
        /// <param name="logger">Logger</param>
        /// <param name="store">State store for files state</param>
        public LocalLibrary(
            FileLibraryConfig config,
            ILogger logger,
            IExtractionStateStore store = null)
        {
            _config = config;
            _store = store;
            Logger = logger;
            State = new Dictionary<string, T>();
            _libState = new BaseExtractionState(_config.LibraryId);
        }
        
        /// <summary>
        /// Initializes the local entity library. Finds entities in CDF and restores the state.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task Init(CancellationToken token)
        {

            if (_store != null)
            {
                await _store.RestoreExtractionState(
                    new Dictionary<string, BaseExtractionState>() { { _config.LibraryId, _libState } },
                    _config.LibraryTable,
                    true,
                    token).ConfigureAwait(false);
            }
            
            FetchRemoteState(token);

            if (_store != null)
            {
                await _store.RestoreExtractionState<U, T>(
                    State,
                    _config.FilesTable,
                    (state, poco) =>
                    {
                        state.Init(poco);
                    },
                    token).ConfigureAwait(false);
                if (_store is LiteDBStateStore ldbStore)
                {
                    HashSet<string> idsToKeep = new HashSet<string>(State.Select(s => s.Value.Id));
                    var col = ldbStore.Database.GetCollection<FileStatePoco>(_config.FilesTable);
                    var stateToDelete = col
                        .Find(s => !idsToKeep.Contains(s.Id))
                        .ToList();
                    if (stateToDelete.Any())
                    {
                        foreach(var state in stateToDelete)
                        {
                            col.Delete(state.Id);
                        }
                    }
                }
            }
            Logger.LogInformation("Local state store {Table} initiated. Tracking {Num} entities", _config.FilesTable, State.Count);
        }

        /// <summary>
        /// Remove the provided entity states from the state store
        /// </summary>
        /// <param name="states">States to remove</param>
        /// <param name="token">Cancellation token</param>
        protected async Task RemoveStates(
            IEnumerable<FileState> states,
            CancellationToken token)
        {
            if (states == null || !states.Any())
            {
                return;
            }
            await _store.DeleteExtractionState(states, _config.FilesTable, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a list of the tasks performed by this library.
        /// These include fetching the list of remote entities and saving state.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of tasks</returns>
        public IEnumerable<Task> GetRunTasks(CancellationToken token)
        {
            return new List<Task> { SaveStates(token), FetchAndProcessRemoteState(token) };
        }

        /// <summary>
        /// Save periodically the library state (first and last entity creation timestamp) and the 
        /// entity state
        /// </summary>
        /// <param name="token">Cancellation token</param>
        private async Task SaveStates(CancellationToken token)
        {
            if (_store == null)
            {
                return;
            }
            while (!token.IsCancellationRequested)
            {
                var waitTask = Task.Delay(TimeSpan.FromSeconds(_config.StateStoreInterval), token);
                var storeTask = StoreLibraryState(token);
                await Task.WhenAll(waitTask, storeTask).ConfigureAwait(false);
            }
        }

         /// <summary>
        /// Periodically searches for entities CDF, in case new ones are found, store locally.
        /// Entities are saved with the internal CDF id as name
        /// </summary>
        /// <param name="token">Cancellation token</param>
        private async Task FetchAndProcessRemoteState(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                string timeRange = _libState.DestinationExtractedRange.IsEmpty ? "Empty" : _libState.DestinationExtractedRange.ToString();
                Logger.LogDebug("Updating entity library. There are currently {Num} entities. Extracted range: {TimeRange}",
                    State.Count,
                    timeRange
                    );


                FetchRemoteState(token);

                if (State.Any())
                {
                    var maxUpdatedMs = State
                        .Select(s => s.Value.UpdatedTime)
                        .Max();
                    var maxUpdatedDt = CogniteTime.FromUnixTimeMilliseconds(maxUpdatedMs);
                    _libState.UpdateDestinationRange(
                        maxUpdatedDt,
                        maxUpdatedDt);
                }

                await Task
                    .Delay(TimeSpan.FromSeconds(_config.StateStoreInterval), token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fetches entities from CDF
        /// </summary>
        /// <param name="token">Cancellation token</param>
        protected abstract void FetchRemoteState(CancellationToken token);

        /// <summary>
        /// Persists the library state from memory to the store
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task StoreLibraryState(CancellationToken token)
        {
            if (_store == null)
            {
                return;
            }
            await _store.StoreExtractionState(
                new[] { _libState },
                _config.LibraryTable,
                token).ConfigureAwait(false);
            await _store.StoreExtractionState(
                State.Values,
                _config.FilesTable,
                (state) => state.GetPoco(),
                token).ConfigureAwait(false);
        }
    }
}
