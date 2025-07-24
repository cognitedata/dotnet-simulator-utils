using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Simulator.Utils;

using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public class ModelLibraryTaskHolderTest
    {


        [Fact]
        public async Task ExecuteAsync_DifferentKeys_BothTasksExecuted()
        {
            // Arrange
            var manager = new ModelLibraryTaskHolder<string, int>();
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
            var manager = new ModelLibraryTaskHolder<string, int>();
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
        public async Task ExecuteAsync_SuccessfulTask_CanRetryWithSameKey()
        {
            // Arrange
            var manager = new ModelLibraryTaskHolder<string, int>();
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
            var manager = new ModelLibraryTaskHolder<string, int>();
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
            var manager = new ModelLibraryTaskHolder<string, int>();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await manager.ExecuteAsync(null!, _ => Task.FromResult(42)));
        }

        [Fact]
        public async Task ExecuteAsync_RaceCondition_TaskCleanup()
        {
            // Arrange - Test for race condition in task cleanup
            var manager = new ModelLibraryTaskHolder<string, int>();
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
        public async Task ExecuteAsync_ConcurrentFailure_DuplicateTasksGetSameException()
        {
            // Arrange
            var manager = new ModelLibraryTaskHolder<string, int>();
            var taskStarted = new TaskCompletionSource<bool>();
            var taskCanFail = new TaskCompletionSource<bool>();
            var expectedException = new InvalidOperationException("Simulated failure");

            // Start the original failing task that we'll control
            var originalTask = manager.ExecuteAsync("fail", async _ =>
            {
                // Signal that we've started
                taskStarted.SetResult(true);
                // Wait until we're told to fail
                await taskCanFail.Task;
                throw expectedException;
            });

            // Wait for original task to start
            await taskStarted.Task;

            // Start duplicate task while original is still running
            var duplicateTask = manager.ExecuteAsync("fail", _ => Task.FromResult(123));

            // Now let the original task fail
            taskCanFail.SetResult(true);

            // Verify both tasks fail with the same exception
            var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => originalTask);
            var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => duplicateTask);

            Assert.Same(expectedException, ex1);
            Assert.Same(expectedException, ex2);
        }

        [Fact]
        public async Task ExecuteAsync_ComplexDisposalScenario_MultipleTasksAndDisposal()
        {
            // Arrange
            var manager = new ModelLibraryTaskHolder<string, int>();
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
            var stringManager = new ModelLibraryTaskHolder<string, int>();
            var intManager = new ModelLibraryTaskHolder<int, string>();
            var tupleManager = new ModelLibraryTaskHolder<(string, int), bool>();

            // Act - Test different key types work correctly with controlled timing
            var stringStarted = new TaskCompletionSource<bool>();
            var intStarted = new TaskCompletionSource<bool>();
            var tupleStarted = new TaskCompletionSource<bool>();

            // Start first tasks with delays to ensure they're running when duplicates start
            var stringTask1 = stringManager.ExecuteAsync("test", async _ =>
            {
                stringStarted.SetResult(true);
                await Task.Delay(50);
                return 1;
            });

            var intTask1 = intManager.ExecuteAsync(123, async _ =>
            {
                intStarted.SetResult(true);
                await Task.Delay(50);
                return "first";
            });

            var tupleTask1 = tupleManager.ExecuteAsync(("key", 42), async _ =>
            {
                tupleStarted.SetResult(true);
                await Task.Delay(50);
                return true;
            });

            // Wait for first tasks to start
            await Task.WhenAll(stringStarted.Task, intStarted.Task, tupleStarted.Task);

            // Start duplicate tasks while first ones are still running
            var stringTask2 = stringManager.ExecuteAsync("test", _ => Task.FromResult(2));
            var intTask2 = intManager.ExecuteAsync(123, _ => Task.FromResult("second"));
            var tupleTask2 = tupleManager.ExecuteAsync(("key", 42), _ => Task.FromResult(false));
            var tupleTask3 = tupleManager.ExecuteAsync(("different", 42), _ => Task.FromResult(false));

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
