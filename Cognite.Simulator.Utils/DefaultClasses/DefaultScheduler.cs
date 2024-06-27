
using System.Collections.Generic;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

/// <summary>
/// A default class for the Simulation scheduler
/// </summary>
/// <typeparam name="TAutomationConfig"></typeparam>
public class DefaultSimulationScheduler<TAutomationConfig> : SimulationSchedulerBase<SimulatorRoutineRevision>
    where TAutomationConfig : AutomationConfig, new()
    {
        /// <summary>
        /// Constructing the Scheduler, define all the injected dependencies here
        /// </summary>
        /// <param name="config"></param>
        /// <param name="configLib"></param>
        /// <param name="logger"></param>
        /// <param name="simulatorConfigs"></param>
        /// <param name="cdf"></param>
        public DefaultSimulationScheduler(
            DefaultConfig<TAutomationConfig> config,
            DefaultRoutineLibrary<TAutomationConfig> configLib,
            ILogger<DefaultSimulationScheduler<TAutomationConfig>> logger,
            IEnumerable<SimulatorConfig> simulatorConfigs,
            CogniteDestination cdf) 
            : base(config.Connector, configLib, logger, simulatorConfigs, cdf)
        {
        }
    }