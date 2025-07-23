using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// A generic class to manage concurrent execution of tasks keyed by <typeparamref name="TKey"/>.
    /// Only one task per key is executed at a time and the total number of concurrently executing
    /// tasks is limited by the constructor parameter.
    /// </summary>
    public class ConcurrentTaskManager<TKey, TResult> : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentDictionary<TKey, (Task<TResult> Task, CancellationTokenSource Cts)> _ongoingTasks;
        private volatile bool _disposed;

        /// <summary>
        /// Creates a new <see cref="ConcurrentTaskManager{TKey, TResult}"/>.
        /// </summary>
        /// <param name="maxConcurrentTasks">Maximum number of tasks allowed to run concurrently.</param>
        public ConcurrentTaskManager(int maxConcurrentTasks = 5)
        {
            if (maxConcurrentTasks <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentTasks), "maxConcurrentTasks must be greater than 0.");
            _semaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
            _ongoingTasks = new ConcurrentDictionary<TKey, (Task<TResult>, CancellationTokenSource)>();
        }

        /// <summary>
        /// Executes a task for a given key, ensuring deduplication and respecting the concurrency limit.
        /// </summary>
        /// <param name="key">Unique key identifying the task.</param>
        /// <param name="taskFactory">Function creating the task. It receives the <paramref name="token"/> so it can honour cancellation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The task result.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
        public Task<TResult> ExecuteAsync(TKey key, Func<CancellationToken, Task<TResult>> taskFactory, CancellationToken token = default)
        {
            return ExecuteAsyncInternal(key, taskFactory, token, isPriority: false);
        }

        /// <summary>
        /// Executes a task for a given key with high priority, canceling any existing task with the same key.
        /// Note: Even existing priority tasks will be canceled to ensure the latest priority task takes precedence.
        /// Be aware that cancellation effectiveness depends on the task honoring the cancellation token; tasks that ignore
        /// the token may continue to execute, potentially wasting resources.
        /// </summary>
        /// <param name="key">Unique key identifying the task.</param>
        /// <param name="taskFactory">Function creating the task. It receives the <paramref name="token"/> so it can honour cancellation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The task result.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
        public Task<TResult> ExecuteAsyncPriority(TKey key, Func<CancellationToken, Task<TResult>> taskFactory, CancellationToken token = default)
        {
            return ExecuteAsyncInternal(key, taskFactory, token, isPriority: true);
        }

        private Task<TResult> ExecuteAsyncInternal(TKey key, Func<CancellationToken, Task<TResult>> taskFactory, CancellationToken token, bool isPriority)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (taskFactory == null) throw new ArgumentNullException(nameof(taskFactory));
            if (_disposed) throw new ObjectDisposedException(nameof(ConcurrentTaskManager<TKey, TResult>));

            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task<TResult> task = null;

            if (isPriority && _ongoingTasks.TryGetValue(key, out var existing))
            {
                try { existing.Cts?.Cancel(); } catch (ObjectDisposedException) { /* Ignore: CTS may be disposed during shutdown or concurrent cancellation */ }
            }

            var entry = _ongoingTasks.GetOrAdd(key, k =>
            {
                task = CreateManagedTask(k, taskFactory, cts.Token);
                return (task, cts);
            });

            if (task == null)
            {
                if (isPriority)
                {
                    try { entry.Cts?.Cancel(); } catch (ObjectDisposedException) { /* Ignore: CTS may be disposed if task completed or during shutdown */ }
                    task = CreateManagedTask(key, taskFactory, cts.Token);
                    _ongoingTasks.AddOrUpdate(key, (task, cts), (k, e) =>
                    {
                        try { e.Cts?.Cancel(); } catch (ObjectDisposedException) { /* Ignore: CTS may be disposed during concurrent operations or shutdown */ }
                        return (task, cts);
                    });
                }
                else
                {
                    cts.Dispose();
                    return entry.Task;
                }
            }

            return task;
        }

        private Task<TResult> CreateManagedTask(TKey key, Func<CancellationToken, Task<TResult>> taskFactory, CancellationToken token)
        {
            return Task.Run(async () =>
            {
                await _semaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    return await taskFactory(token).ConfigureAwait(false);
                }
                finally
                {
                    try { _semaphore.Release(); } catch (ObjectDisposedException) { /* Ignore: Semaphore may be disposed during shutdown */ }
                    if (_ongoingTasks.TryRemove(key, out var removed))
                        removed.Cts?.Dispose();
                }
            }, token);
        }

        /// <summary>
        /// Disposes the task manager and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _ongoingTasks.Values)
            {
                try { entry.Cts?.Cancel(); } catch (ObjectDisposedException) { /* Ignore: CTS may be disposed if already canceled or during concurrent operations */ }
                entry.Cts?.Dispose();
            }
            _ongoingTasks.Clear();
            _semaphore?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}