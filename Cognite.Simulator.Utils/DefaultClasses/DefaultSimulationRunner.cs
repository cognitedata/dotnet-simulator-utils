using System.Collections.Generic;


using Microsoft.Extensions.Logging;

using Cognite.Extractor.Utils;

using CogniteSdk.Alpha;


using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils.Automation;

namespace Cognite.Simulator.Utils
{
    public class DefaultSimulationRunner<TAutomationConfig,TModelStateBase> : 
        RoutineRunnerBase<TAutomationConfig,TModelStateBase, SimulatorRoutineRevision>
         where TAutomationConfig : AutomationConfig, new()
         where TModelStateBase: ModelStateBase
    {

        public DefaultSimulationRunner(
            DefaultConfig<TAutomationConfig> config,
            CogniteDestination cdf, 
            DefaultModelLibrary<TAutomationConfig, TModelStateBase> modelLibrary, 
            DefaultRoutineLibrary<TAutomationConfig> configLibrary,
            ISimulatorClient<TModelStateBase, SimulatorRoutineRevision> client,
            ILogger<DefaultSimulationRunner<TAutomationConfig,TModelStateBase>> logger) : 
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