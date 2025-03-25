using System.Collections.Generic;

using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Default simulation runner class.
    /// </summary>
    /// <typeparam name="TAutomationConfig">The type of the automation configuration.</typeparam>
    /// <typeparam name="TModelStateBase">The type of the model state base.</typeparam>
    /// <typeparam name="TModelStateBasePoco">The type of the model state base POCO.</typeparam>
    public class DefaultSimulationRunner<TAutomationConfig, TModelStateBase, TModelStateBasePoco> :
        RoutineRunnerBase<TAutomationConfig, TModelStateBase, SimulatorRoutineRevision>
         where TAutomationConfig : AutomationConfig, new()
         where TModelStateBase : ModelStateBase, new()
         where TModelStateBasePoco : ModelStateBasePoco
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultSimulationRunner{TAutomationConfig, TModelStateBase, TModelStateBasePoco}"/> class.
        /// </summary>
        /// <param name="config">The default configuration.</param>
        /// <param name="cdf">The Cognite destination.</param>
        /// <param name="modelLibrary">The default model library.</param>
        /// <param name="configLibrary">The default routine library.</param>
        /// <param name="simulatorDefinition">The simulator definition.</param>
        /// <param name="client">The simulator client.</param>
        /// <param name="logger">The logger.</param>
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