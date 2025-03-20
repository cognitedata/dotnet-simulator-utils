using System.Collections.Generic;

using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{
    public class DefaultSimulationRunner<TAutomationConfig, TModelStateBase, TModelStateBasePoco> :
        RoutineRunnerBase<TAutomationConfig, TModelStateBase, SimulatorRoutineRevision>
         where TAutomationConfig : AutomationConfig, new()
         where TModelStateBase : ModelStateBase, new()
         where TModelStateBasePoco : ModelStateBasePoco
    {

        public DefaultSimulationRunner(
            DefaultConfig<TAutomationConfig> config,
            CogniteDestination cdf,
            DefaultModelLibrary<TAutomationConfig, TModelStateBase, TModelStateBasePoco> modelLibrary,
            DefaultRoutineLibrary<TAutomationConfig> configLibrary,
            SimulatorCreate simulatorDefinition,
            ISimulatorClient<TModelStateBase, SimulatorRoutineRevision> client,
            ILogger<DefaultSimulationRunner<TAutomationConfig, TModelStateBase, TModelStateBasePoco>> logger) :
            base(config?.Connector,
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