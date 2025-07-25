using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// A generic class that provides task deduplication and serialized execution of tasks.
    /// Only one task can be executing at a time across all keys. For each key, subsequent calls while
    /// a task is in progress will return the result of the ongoing task rather than starting a new one.
    /// 
    /// This serialized execution is provided by a semaphore that is held for the entire duration of each task,
    /// ensuring only one task runs at a time regardless of key. This makes the class suitable for operations
    /// that need to be serialized, such as model library tasks that should not run concurrently.
    /// 
    /// This is a best-effort implementation intended for slow or infrequent operations.
    /// It may exhibit race conditions under high concurrency of short-lived tasks or rapid task creation/completion cycles.
    /// </summary>
    public class ModelLibraryTaskHolder<TKey, TResult> : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentDictionary<TKey, (Task<TResult> Task, CancellationTokenSource Cts)> _ongoingTasks;
        private volatile bool _disposed;

        /// <summary>
        /// Creates a new <see cref="ModelLibraryTaskHolder{TKey, TResult}"/>.
        /// </summary>
        public ModelLibraryTaskHolder()
        {
            _semaphore = new SemaphoreSlim(1, 1);
            _ongoingTasks = new ConcurrentDictionary<TKey, (Task<TResult>, CancellationTokenSource)>();
        }

        /// <summary>
        /// Executes a task for a given key in a serialized manner, ensuring only one task executes at a time
        /// across all keys. If a task for any key is currently executing, new tasks will wait for their turn.
        /// For a given key, if a task is already in progress, subsequent calls will return the result of
        /// that ongoing task rather than starting a new one.
        /// </summary>
        /// <remarks>
        /// The provided <paramref name="taskFactory"/> must not call back into <see cref="ExecuteAsync"/> on the same instance, as this will cause a deadlock.
        /// </remarks>
        /// <param name="key">Unique key identifying the task.</param>
        /// <param name="taskFactory">Function creating the task. It receives the <paramref name="token"/> so it can honour cancellation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The task result. Note that the underlying task execution is serialized - only one task runs at a time.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
        public async Task<TResult> ExecuteAsync(TKey key, Func<CancellationToken, Task<TResult>> taskFactory, CancellationToken token = default)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (taskFactory == null) throw new ArgumentNullException(nameof(taskFactory));
            if (_disposed) throw new ObjectDisposedException(nameof(ModelLibraryTaskHolder<TKey, TResult>));

            // Fast path - return existing task if available
            if (_ongoingTasks.TryGetValue(key, out var existingEntry))
            {
                return await existingEntry.Task.ConfigureAwait(false);
            }

            await _semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring lock
                if (_ongoingTasks.TryGetValue(key, out existingEntry))
                {
                    return await existingEntry.Task.ConfigureAwait(false);
                }

                var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var task = Task.Run(async () =>
                {
                    try
                    {
                        return await taskFactory(cts.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (_ongoingTasks.TryRemove(key, out var removed))
                        {
                            removed.Cts?.Dispose();
                        }
                    }
                });

                _ongoingTasks.TryAdd(key, (task, cts));
                return await task.ConfigureAwait(false);
            }
            finally
            {
                try { _semaphore.Release(); } catch (ObjectDisposedException) { /* Ignore: Semaphore may be disposed during shutdown */ }
            }
        }

        /// <summary>
        /// Disposes the task manager and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Take a snapshot of all ongoing tasks to prevent race conditions.
            var tasksToCleanUp = _ongoingTasks.ToArray();
            _ongoingTasks.Clear();

            foreach (var kvp in tasksToCleanUp)
            {
                var entry = kvp.Value;
                try { entry.Cts?.Cancel(); } catch (ObjectDisposedException) { /* Ignore: CTS may be disposed if already canceled or during concurrent operations */ }
                entry.Cts?.Dispose();
            }

            _semaphore?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
