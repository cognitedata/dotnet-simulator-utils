using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Common utility methods. May be useful when developing simulator connectors
    /// </summary>
    public static class CommonUtils
    {
        /// <summary>
        /// Run all of the tasks in this enumeration. If any fail or is canceled, cancel the
        /// remaining tasks and return. The first found exception is thrown
        /// </summary>
        /// <param name="tasks">List of tasks to run</param>
        /// <param name="tokenSource">Source of cancellation tokens</param>
        public static async Task RunAll(this IEnumerable<Task> tasks, CancellationTokenSource tokenSource)
        {
            if (tokenSource == null)
            {
                throw new ArgumentNullException(nameof(tokenSource));
            }
            Exception ex = null;
            var taskList = tasks.ToList();
            while (taskList.Any())
            {
                // Wait for any of the tasks to finish or fail
                var task = await Task.WhenAny(taskList).ConfigureAwait(false);
                taskList.Remove(task);
                if (task.IsFaulted || task.IsCanceled)
                {
                    // If one of the tasks fail, cancel the token source, stopping the remaining tasks
                    tokenSource.Cancel();
                    if (task.Exception != null)
                    {
                        ex = task.Exception;
                    }
                }
            }
            if (ex != null)
            {
                throw ex;
            }
        }
    }
}
