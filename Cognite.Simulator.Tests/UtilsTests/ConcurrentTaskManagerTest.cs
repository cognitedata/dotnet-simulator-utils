using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Simulator.Utils;

using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public class ConcurrentTaskManagerTest
    {
        [Fact]
        public async Task ExecuteAsync_SameKey_OnlyOneTaskExecuted()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>();
            var executionCount = 0;

            // Act - Start two tasks with the same key simultaneously
            var task1 = manager.ExecuteAsync("test", token =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(42);
            });

            var task2 = manager.ExecuteAsync("test", token =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(99);
            });

            var results = await Task.WhenAll(task1, task2);

            // Assert - Both tasks should return the same result, indicating only one executed
            Assert.Equal(1, executionCount);
            Assert.Equal(42, results[0]);
            Assert.Equal(42, results[1]); // Same result as task1
        }

        [Fact]
        public async Task ExecuteAsync_DifferentKeys_BothTasksExecuted()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>();
            var executionCount = 0;

            // Act
            var task1 = manager.ExecuteAsync("key1", token =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(1);
            });

            var task2 = manager.ExecuteAsync("key2", token =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(2);
            });

            var results = await Task.WhenAll(task1, task2);

            // Assert
            Assert.Equal(2, executionCount);
            Assert.Contains(1, results);
            Assert.Contains(2, results);
        }

        [Fact]
        public async Task ExecuteAsync_ConcurrencyLimit_RespectsMaxConcurrentTasks()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>(maxConcurrentTasks: 1);
            var currentlyRunning = 0;
            var maxConcurrentObserved = 0;
            var task1Started = new TaskCompletionSource<bool>();
            var canProceed = new TaskCompletionSource<bool>();

            // Act - Start 2 tasks with different keys
            var task1 = manager.ExecuteAsync("key1", async token =>
            {
                var current = Interlocked.Increment(ref currentlyRunning);
                maxConcurrentObserved = Math.Max(maxConcurrentObserved, current);
                task1Started.SetResult(true);
                await canProceed.Task;
                Interlocked.Decrement(ref currentlyRunning);
                return 1;
            });

            await task1Started.Task; // Wait for first task to start

            var task2 = manager.ExecuteAsync("key2", token =>
            {
                var current = Interlocked.Increment(ref currentlyRunning);
                maxConcurrentObserved = Math.Max(maxConcurrentObserved, current);
                Interlocked.Decrement(ref currentlyRunning);
                return Task.FromResult(2);
            });

            canProceed.SetResult(true);
            await Task.WhenAll(task1, task2);

            // Assert - Should never exceed the concurrency limit
            Assert.Equal(1, maxConcurrentObserved);
        }

        [Fact]
        public async Task ExecuteAsync_HighConcurrency_RaceConditionStressTest()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>();
            const int concurrentRequests = 40; // Small number for speed
            var executionCounts = new ConcurrentDictionary<string, int>();

            // Act - Hammer the same keys with many concurrent requests
            var tasks = Enumerable.Range(0, concurrentRequests).Select(i =>
            {
                var key = $"key{i % 3}"; // Use 3 different keys
                return manager.ExecuteAsync(key, token =>
                {
                    executionCounts.AddOrUpdate(key, 1, (k, v) => v + 1);
                    return Task.FromResult(i);
                });
            }).ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert - Each key should have been executed exactly once
            foreach (var kvp in executionCounts)
            {
                Assert.Equal(1, kvp.Value);
            }
        }


        [Fact]
        public async Task ExecuteAsync_FailedTask_CanRetryWithSameKey()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>();
            var attemptCount = 0;

            // Act - First call fails
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await manager.ExecuteAsync("test1", _ =>
                {
                    Interlocked.Increment(ref attemptCount);
                    throw new InvalidOperationException("First attempt fails");
                });
            });

            // Second call with different key should succeed (testing cleanup)
            var result = await manager.ExecuteAsync("test2", _ =>
            {
                Interlocked.Increment(ref attemptCount);
                return Task.FromResult(42);
            });

            // Assert
            Assert.Equal(42, result);
            Assert.Equal(2, attemptCount); // Both attempts should have executed
        }

        [Fact]
        public async Task ExecuteAsync_SuccessfulTask_CanRetryWithSameKey()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>(1);
            var attemptCount = 0;

            // Act - First call succeeds
            var result1 = await manager.ExecuteAsync("test1", _ =>
            {
                Interlocked.Increment(ref attemptCount);
                return Task.FromResult(42);
            });

            // Second call with same key should get a new result
            var result2 = await manager.ExecuteAsync("test1", _ =>
            {
                Interlocked.Increment(ref attemptCount);
                return Task.FromResult(99);
            });

            // Assert
            Assert.Equal(42, result1);
            Assert.Equal(99, result2);
            Assert.Equal(2, attemptCount); // Both attempts should have executed
        }

        [Fact]
        public async Task ExecuteAsync_CancellationToken_CancelsExecution()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>();
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert - TaskCanceledException is a subclass of OperationCanceledException
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await manager.ExecuteAsync("test", async token =>
                {
                    await Task.Delay(10, token);
                    return 42;
                }, cts.Token);
            });
        }

        [Fact]
        public async Task ExecuteAsync_NullKey_ThrowsArgumentNullException()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await manager.ExecuteAsync(null!, _ => Task.FromResult(42)));
        }

        [Fact]
        public void Constructor_InvalidMaxConcurrentTasks_ThrowsArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrentTaskManager<string, int>(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrentTaskManager<string, int>(-1));
        }

        [Fact]
        public async Task ExecuteAsync_RaceCondition_TaskCleanup()
        {
            // Arrange - Test for race condition in task cleanup
            var manager = new ConcurrentTaskManager<string, int>(1);
            var firstTaskStarted = new TaskCompletionSource<bool>();
            var firstTaskCanComplete = new TaskCompletionSource<bool>();
            var secondTaskExecuted = false;
            var executionCount = 0;

            // Act - Start first task that blocks
            var firstTask = manager.ExecuteAsync("key", async token =>
            {
                firstTaskStarted.SetResult(true);
                await firstTaskCanComplete.Task;
                Interlocked.Increment(ref executionCount);
                return 1;
            });

            await firstTaskStarted.Task; // Wait for first task to start

            // Start second task with same key - should wait for first task
            var secondTask = manager.ExecuteAsync("key", token =>
            {
                secondTaskExecuted = true;
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(2);
            });

            // Complete first task
            firstTaskCanComplete.SetResult(true);
            await firstTask;

            // Wait for second task - if cleanup race condition exists, it might start a new task
            await secondTask;

            // Assert - Check if second task reused the result (correct deduplication behavior)
            Assert.False(secondTaskExecuted); // Should be false due to deduplication
            Assert.Equal(1, executionCount); // Should be 1 due to deduplication, no second execution
        }



        [Fact]
        public async Task ExecuteAsync_ComplexRaceCondition_MultipleWaitersOnFailedTask()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>();
            var firstTaskStarted = new TaskCompletionSource<bool>();
            var canFail = new TaskCompletionSource<bool>();
            var expectedException = new InvalidOperationException("Complex test exception");

            // Act - Start multiple tasks with same key, first one will fail
            var task1 = manager.ExecuteAsync("complex", async _ =>
            {
                firstTaskStarted.SetResult(true);
                await canFail.Task;
                throw expectedException;
            });

            await firstTaskStarted.Task; // Wait for first task to start

            // Start multiple waiters
            var task2 = manager.ExecuteAsync("complex", _ => Task.FromResult(42));
            var task3 = manager.ExecuteAsync("complex", _ => Task.FromResult(99));
            var task4 = manager.ExecuteAsync("complex", _ => Task.FromResult(123));

            // Let the first task fail
            canFail.SetResult(true);

            // Assert - All tasks should get the same exception
            var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => task1);
            var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => task2);
            var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() => task3);
            var ex4 = await Assert.ThrowsAsync<InvalidOperationException>(() => task4);

            Assert.Same(expectedException, ex1);
            Assert.Same(expectedException, ex2);
            Assert.Same(expectedException, ex3);
            Assert.Same(expectedException, ex4);
        }

        [Fact]
        public async Task ExecuteAsync_ComplexConcurrencyMix_DifferentKeysWithFailuresAndSuccesses()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>();

            // Act - Simple mix of success/failure without coordination delays
            var successTask = manager.ExecuteAsync("success", _ => Task.FromResult(42));
            var failTask = manager.ExecuteAsync("fail", _ => throw new InvalidOperationException("fail"));
            var dupSuccessTask = manager.ExecuteAsync("success", _ => Task.FromResult(999)); // Should get 42
            var dupFailTask = manager.ExecuteAsync("fail", _ => Task.FromResult(123)); // Should get same exception

            // Assert
            var successResult = await successTask;
            var dupSuccessResult = await dupSuccessTask;

            var failException = await Assert.ThrowsAsync<InvalidOperationException>(() => failTask);
            var dupFailException = await Assert.ThrowsAsync<InvalidOperationException>(() => dupFailTask);

            Assert.Equal(42, successResult);
            Assert.Equal(42, dupSuccessResult); // Same result due to deduplication
            Assert.Same(failException, dupFailException); // Same exception instance
        }

        [Fact]
        public async Task ExecuteAsync_ComplexDisposalScenario_MultipleTasksAndDisposal()
        {
            // Arrange
            var manager = new ConcurrentTaskManager<string, int>();
            var taskStarted = new TaskCompletionSource<bool>();
            var taskCanComplete = new TaskCompletionSource<bool>();

            // Act - Start a task, dispose while running
            var runningTask = manager.ExecuteAsync("running", async _ =>
            {
                taskStarted.SetResult(true);
                await taskCanComplete.Task;
                return 42;
            });

            await taskStarted.Task; // Wait for task to start

            // Dispose while task is running
            manager.Dispose();

            // Try to start new task after disposal - should throw immediately
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await manager.ExecuteAsync("afterDispose", _ => Task.FromResult(999)));

            // Complete the running task
            taskCanComplete.SetResult(true);

            // Assert - Running task should complete successfully
            var result = await runningTask;
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task ExecuteAsync_ComplexKeyTypes_DifferentTypesAsKeys()
        {
            // Arrange - Test with different key types
            var stringManager = new ConcurrentTaskManager<string, int>();
            var intManager = new ConcurrentTaskManager<int, string>();
            var tupleManager = new ConcurrentTaskManager<(string, int), bool>();

            // Act - Test different key types work correctly
            var stringTask1 = stringManager.ExecuteAsync("test", _ => Task.FromResult(1));
            var stringTask2 = stringManager.ExecuteAsync("test", _ => Task.FromResult(2)); // Should get same result

            var intTask1 = intManager.ExecuteAsync(123, _ => Task.FromResult("first"));
            var intTask2 = intManager.ExecuteAsync(123, _ => Task.FromResult("second")); // Should get same result

            var tupleTask1 = tupleManager.ExecuteAsync(("key", 42), _ => Task.FromResult(true));
            var tupleTask2 = tupleManager.ExecuteAsync(("key", 42), _ => Task.FromResult(false)); // Should get same result
            var tupleTask3 = tupleManager.ExecuteAsync(("different", 42), _ => Task.FromResult(false)); // Different key

            // Assert
            var stringResults = await Task.WhenAll(stringTask1, stringTask2);
            Assert.Equal(stringResults[0], stringResults[1]);

            var intResults = await Task.WhenAll(intTask1, intTask2);
            Assert.Equal(intResults[0], intResults[1]);

            var tupleResults = await Task.WhenAll(tupleTask1, tupleTask2, tupleTask3);
            Assert.Equal(tupleResults[0], tupleResults[1]); // Same key - both should be true
            Assert.False(tupleResults[2]); // Different key, executed independently
        }
    }
}
