using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CogniteSdk.Alpha;
using Cognite.Extractor.Common;
using CogniteSdk;
using CogniteSdk.Resources.Alpha;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognite.Extensions;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Class containing extensions to the CDF Simulators resource with utility methods
    /// </summary>
    public static class SimulatorsExtensions
    {
        private static ILogger _logger = new NullLogger<Client>();

        /// <summary>
        /// Updates the logs in chunks for a simulator resource.
        /// </summary>
        /// <param name="cdfSimulators">The SimulatorsResource instance.</param>
        /// <param name="id">The ID of the simulator log.</param>
        /// <param name="items">The list of log data entries.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>The updated SimulatorsResource instance.</returns>
        public static async Task UpdateLogsBatch(
            this SimulatorsResource cdfSimulators,
            long id,
            List<SimulatorLogDataEntry> items,
            CancellationToken token = default
        )
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            var chunkSize = 1000;
            var logsByChunks = items
                .ChunkBy(chunkSize)
                .ToList();

            _logger.LogDebug("Updating logs. Number of log entries: {Number}. Number of chunks: {Chunks}", items.Count, logsByChunks.Count);
            var generators = logsByChunks
                .Select<IEnumerable<SimulatorLogDataEntry>, Func<Task>>(
                (chunk, idx) => async () =>
                {

                    var item = new SimulatorLogUpdateItem(id)
                    {
                        Update = new SimulatorLogUpdate
                        {
                            Data = new UpdateEnumerable<SimulatorLogDataEntry>(chunk, null)
                        }
                    };
                    await cdfSimulators
                        .UpdateSimulatorLogsAsync(new List<SimulatorLogUpdateItem> { item }, token)
                        .ConfigureAwait(false);
                });

            int taskNum = 0;
            await generators.RunThrottled(
                1,
                (_) =>
                {
                    if (logsByChunks.Count > 1)
                        _logger.LogDebug("{MethodName} completed {NumDone}/{TotalNum} tasks",
                            nameof(UpdateLogsBatch), ++taskNum, logsByChunks.Count);
                },
                CancellationToken.None
            ).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the simulation model revision status and status message in CDF.
        /// </summary>
        /// <param name="cdfSimulators">The SimulatorsResource instance.</param>
        /// <param name="id">The ID of the simulator model revision.</param>
        /// <param name="status">The status of the model revision.</param>
        /// <param name="statusMessage">The status message.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>The updated SimulatorsResource instance.</returns>
        public static async Task<SimulatorModelRevision> UpdateSimulatorModelRevisionParsingStatus(
            this SimulatorsResource cdfSimulators,
            long id,
            SimulatorModelRevisionStatus status,
            string statusMessage = null,
            CancellationToken token = default
        )
        {
            var modelRevisionPatch =
                new SimulatorModelRevisionUpdateItem(id)
                {
                    Update =
                        new SimulatorModelRevisionUpdate
                        {
                            Status = new Update<SimulatorModelRevisionStatus>(status),
                        }
                };

            if (statusMessage != null)
            {
                var msg = statusMessage.LimitUtf8ByteCount(255);
                modelRevisionPatch.Update.StatusMessage = new Update<string>(msg);
            }

            var res = await cdfSimulators.UpdateSimulatorModelRevisionsAsync(
                new List<SimulatorModelRevisionUpdateItem> { modelRevisionPatch },
                token).ConfigureAwait(false);

            return res.FirstOrDefault();
        }

    }
}