
using System.Collections.Generic;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;


/// <summary>
/// A default instance of the routine library.
/// </summary>
public class DefaultRoutineLibrary<TAutomationConfig> :
        RoutineLibraryBase<SimulatorRoutineRevision>
        where TAutomationConfig : AutomationConfig, new()
    {
        public DefaultRoutineLibrary(
            DefaultConfig<TAutomationConfig> config,
            CogniteDestination cdf,
            ILogger<DefaultRoutineLibrary<TAutomationConfig>> logger) :
            base(config.Connector.RoutineLibrary, new List<SimulatorConfig> { config.Simulator }, cdf, logger)
        {
        }
    }