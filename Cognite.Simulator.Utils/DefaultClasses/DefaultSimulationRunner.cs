using System.Collections.Generic;


using Microsoft.Extensions.Logging;

using Cognite.Extractor.Utils;

using CogniteSdk.Alpha;


using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils.Automation;

namespace Cognite.Simulator.Utils
{
    public class DefaultSimulationRunner<TAutomationConfig,TModelStateBase,TModelStateBasePoco> : 
        RoutineRunnerBase<TAutomationConfig,TModelStateBase, SimulatorRoutineRevision>
         where TAutomationConfig : AutomationConfig, new()
         where TModelStateBase: ModelStateBase, new()
         where TModelStateBasePoco: ModelStateBasePoco
    {

        public DefaultSimulationRunner(
            DefaultConfig<TAutomationConfig> config,
            CogniteDestination cdf, 
            DefaultModelLibrary<TAutomationConfig, TModelStateBase,TModelStateBasePoco> modelLibrary, 
            DefaultRoutineLibrary<TAutomationConfig> configLibrary,
            SimulatorCreate simulatorDefinition,
            ISimulatorClient<TModelStateBase, SimulatorRoutineRevision> client,
            ILogger<DefaultSimulationRunner<TAutomationConfig,TModelStateBase,TModelStateBasePoco>> logger) : 
            base(config.Connector,
                simulatorDefinition, 
                cdf, 
                modelLibrary, 
                configLibrary, 
                client, 
                logger
            )
        {
        }
    }
}