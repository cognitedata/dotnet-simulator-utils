using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CogniteSdk;
using CogniteSdk.Alpha;

namespace Cognite.Simulator.Tests {
    public class TestHelpers {
        public static async Task<SimulationRun> WaitUntilFinalStatus(Client cdf, long simulationTaskId) {
            IEnumerable<SimulationRun> runsRes = null;
            SimulationRun? run = null;
            while (run == null || (run.Status != SimulationRunStatus.success && run.Status != SimulationRunStatus.failure)) {
                await Task.Delay(1000).ConfigureAwait(false);
                runsRes = await cdf.Alpha.Simulators.RetrieveSimulationRunsAsync(
                    new [] { simulationTaskId }
                ).ConfigureAwait(false);
                run = runsRes.First();
            }
            return runsRes.First();
        }
    }


}
