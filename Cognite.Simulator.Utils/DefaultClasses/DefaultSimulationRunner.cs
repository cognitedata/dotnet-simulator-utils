using System.Collections.Generic;


using Microsoft.Extensions.Logging;

using Cognite.Extractor.Utils;

using CogniteSdk.Alpha;


using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils.Automation;

namespace Cognite.Simulator.Utils
{
    public class DefaultSimulationRunner<TAutomationConfig> : 
        RoutineRunnerBase<ModelStateBase, SimulatorRoutineRevision>
         where TAutomationConfig : AutomationConfig, new()
    {

        public DefaultSimulationRunner(
            DefaultConfig<TAutomationConfig> config,
            CogniteDestination cdf, 
            DefaultModelLibrary<TAutomationConfig> modelLibrary, 
            DefaultRoutineLibrary<TAutomationConfig> configLibrary,
            ISimulatorClient<ModelStateBase, SimulatorRoutineRevision> client,
            ILogger<DefaultSimulationRunner<TAutomationConfig>> logger) : 
            base(config.Connector, new List<SimulatorConfig>() { config.Simulator }, 
            cdf, 
            modelLibrary, 
            configLibrary, 
            client, 
            logger)
        {
        }
    }
}