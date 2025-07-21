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
        private readonly ConcurrentDictionary<TKey, TaskInfo> _ongoingTasks;
        private volatile bool _disposed;

        /// <summary>
        /// Internal class to track task information including cancellation
        /// </summary>
        private class TaskInfo
        {
            public Task<TResult> Task { get; set; }
            public CancellationTokenSource CancellationSource { get; set; }
            public bool IsPriority { get; set; }
        }

        /// <summary>
        /// Creates a new <see cref="ConcurrentTaskManager{TKey, TResult}"/>.
        /// </summary>
        /// <param name="maxConcurrentTasks">Maximum number of tasks allowed to run concurrently.</param>
        public ConcurrentTaskManager(int maxConcurrentTasks = 5)
        {
            if (maxConcurrentTasks <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentTasks), "maxConcurrentTasks must be greater than 0.");
            _semaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
            _ongoingTasks = new ConcurrentDictionary<TKey, TaskInfo>();
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

        /// <summary>
        /// Convenience overload when the task factory does not require a cancellation token.
        /// </summary>
        public Task<TResult> ExecuteAsync(TKey key, Func<Task<TResult>> taskFactory, CancellationToken token = default)
        {
            if (taskFactory == null) throw new ArgumentNullException(nameof(taskFactory));
            return ExecuteAsync(key, _ => taskFactory(), token);
        }

        /// <summary>
        /// Convenience overload for priority execution when the task factory does not require a cancellation token.
        /// Note: Even existing priority tasks will be canceled to ensure the latest priority task takes precedence.
        /// Be aware that cancellation effectiveness depends on the task honoring the cancellation token.
        /// </summary>
        public Task<TResult> ExecuteAsyncPriority(TKey key, Func<Task<TResult>> taskFactory, CancellationToken token = default)
        {
            if (taskFactory == null) throw new ArgumentNullException(nameof(taskFactory));
            return ExecuteAsyncPriority(key, _ => taskFactory(), token);
        }

        private Task<TResult> ExecuteAsyncInternal(TKey key, Func<CancellationToken, Task<TResult>> taskFactory, CancellationToken token, bool isPriority)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (taskFactory == null) throw new ArgumentNullException(nameof(taskFactory));
            if (_disposed) throw new ObjectDisposedException(nameof(ConcurrentTaskManager<TKey, TResult>));

            TaskInfo taskInfo = null;
            var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);

            if (isPriority)
            {
                // For priority tasks, cancel any existing task with the same key
                if (_ongoingTasks.TryGetValue(key, out var existingTaskInfo))
                {
                    try
                    {
                        existingTaskInfo.CancellationSource?.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore if already disposed
                    }
                }
            }

            taskInfo = new TaskInfo
            {
                CancellationSource = cancellationSource,
                IsPriority = isPriority
            };

            // Use GetOrAdd for normal tasks, or force replace for priority tasks
            if (isPriority)
            {
                taskInfo.Task = CreateManagedTask(key, taskFactory, cancellationSource.Token);
                _ongoingTasks.AddOrUpdate(key, taskInfo, (k, existing) =>
                {
                    // Cancel the existing task unconditionally for priority tasks
                    try
                    {
                        existing.CancellationSource?.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore if already disposed
                    }
                    return taskInfo;
                });
            }
            else
            {
                var addedTaskInfo = _ongoingTasks.GetOrAdd(key, k =>
                {
                    taskInfo.Task = CreateManagedTask(k, taskFactory, cancellationSource.Token);
                    return taskInfo;
                });

                if (addedTaskInfo != taskInfo)
                {
                    // Another task was already present, dispose our cancellation source and return existing task
                    cancellationSource.Dispose();
                    return addedTaskInfo.Task;
                }
            }

            return taskInfo.Task;
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
                    try
                    {
                        _semaphore.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore if semaphore is disposed - this can happen during shutdown
                    }

                    // Clean up the task info
                    if (_ongoingTasks.TryRemove(key, out var removedTaskInfo))
                    {
                        removedTaskInfo.CancellationSource?.Dispose();
                    }
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

            // Cancel all ongoing tasks
            foreach (var taskInfo in _ongoingTasks.Values)
            {
                try
                {
                    taskInfo.CancellationSource?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if already disposed
                }
                taskInfo.CancellationSource?.Dispose();
            }
            _ongoingTasks.Clear();

            _semaphore?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}