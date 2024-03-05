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

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Class containing extensions to the CDF Events resource with utility methods
    /// for simulator integrations
    /// </summary>
    public static class SimulatorsExtensions {
        private static ILogger _logger = new NullLogger<Client>();

        internal static void SetLogger(ILogger logger)
        {
            _logger = logger;
        }
        public static async Task<SimulatorsResource> UpdateLogsBatch(
            this SimulatorsResource cdfSimulators,
            long id,
            List<SimulatorLogDataEntry> items
        ){
            var chunkSize = 5;
            var logsByChunks = items
                .ChunkBy(chunkSize)
                .ToList();

            _logger.LogDebug("Updating time series. Number of time series: {Number}. Number of chunks: {Chunks}", items.Count, logsByChunks.Count);
            var generators = logsByChunks
                .Select<IEnumerable<SimulatorLogDataEntry>, Func<Task>>(
                (chunk, idx) => async () => {
                    
                    var item = new SimulatorLogUpdateItem(id){
                        Update = new SimulatorLogUpdate{
                            Data = new UpdateEnumerable<SimulatorLogDataEntry>(chunk, null)
                        }
                    };
                    await cdfSimulators
                        .UpdateSimulatorLogsAsync(new List<SimulatorLogUpdateItem> { item })
                        .ConfigureAwait(false);
                });
            
            int taskNum = 0;
            await generators.RunThrottled(
                1, // Max number of concurrent tasks, change later to read from config
                (_) => {
                    if (logsByChunks.Count > 1)
                        _logger.LogDebug("{MethodName} completed {NumDone}/{TotalNum} tasks",
                            nameof(UpdateLogsBatch), ++taskNum, logsByChunks.Count);
                },
                CancellationToken.None
            ).ConfigureAwait(false);

            
            return null;
        }
    }
}