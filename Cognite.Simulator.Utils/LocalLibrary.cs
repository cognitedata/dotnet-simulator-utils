using Cognite.Extractor.Common;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Represents a local file library. The library can be configured to fetch files
    /// associated to a simulator and stored in a CDF dataset (see <seealso cref="SimulatorConfig"/>).
    /// The files are stored locally at the folder specified in the library configuration (<seealso cref="FileLibraryConfig"/>).
    /// Files are kept in sync with CDF, so that updates in the remote files are reflected on the local files.
    /// </summary>
    /// <typeparam name="T">Type of the state object used in this library</typeparam>
    /// <typeparam name="U">Type of the data object used to serialize and deserialize state</typeparam>
    public abstract class LocalLibrary<T, U>
        where T : FileState
        where U : FileStatePoco
    {
        /// <summary>
        /// Dictionary holding the file states. The keys are the external ids of the
        /// CDF files and the values are the state objects of type <typeparamref name="T"/>
        /// </summary>
        public Dictionary<string, T> State { get; private set; }

        /// <summary>
        /// Logger object
        /// </summary>
        protected ILogger Logger { get; private set; }

        // Other injected services
        private readonly FileLibraryConfig _config;
        // private readonly IList<SimulatorConfig> _simulators;
        private readonly IExtractionStateStore _store;
        // private readonly FileDownloadClient _downloadClient;

        // Internal objects
        private readonly BaseExtractionState _libState;
        // private readonly SimulatorDataType _resourceType;
        // private string _modelFolder;

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
        /// Initializes the local file library. Creates the folder, find the files in CDF and restore state.
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
            Logger.LogInformation("Local state store {Table} initiated. Tracking {Num} files", _config.FilesTable, State.Count);
        }

        /// <summary>
        /// Remove the provided file states from the state store
        /// </summary>
        /// <param name="states">States to remove</param>
        /// <param name="token">Cancellation token</param>
        protected async Task RemoveStates(
            IEnumerable<T> states,
            CancellationToken token)
        {
            if (states == null || !states.Any())
            {
                return;
            }
            await _store.DeleteExtractionState(states, _config.FilesTable, token)
                .ConfigureAwait(false);
            // await _store
            //     .RemoveFileStates(_config.FilesTable, states, token)
            //     .ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a list of the tasks performed by this library.
        /// These include searching and downloading files ans saving state.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of tasks</returns>
        public IEnumerable<Task> GetRunTasks(CancellationToken token)
        {
            return new List<Task> { SaveStates(token), FetchAndProcessRemoteState(token) };
        }

        /// <summary>
        /// Save periodically the library state (first and last file creation timestamp) and the 
        /// files state (path to the file stored locally)
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
        /// Periodically searches for new files in CDF, in case new ones are found, download them and store locally.
        /// Files are saved with the internal CDF id as name
        /// </summary>
        /// <param name="token">Cancellation token</param>
        private async Task FetchAndProcessRemoteState(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                string timeRange = _libState.DestinationExtractedRange.IsEmpty ? "Empty" : _libState.DestinationExtractedRange.ToString();
                Logger.LogDebug("Updating file library. There are currently {Num} files. Extracted range: {TimeRange}",
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
        /// Process files that have been downloaded. This method should open 
        /// the file in a simulator, verify that it is valid, extract relevant data
        /// and ingest it to CDF. 
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
