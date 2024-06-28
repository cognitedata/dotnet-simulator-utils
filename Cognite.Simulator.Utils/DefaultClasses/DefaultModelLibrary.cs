using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{

    public class DefaultModelLibrary<TAutomationConfig> :
    ModelLibraryBase<ModelStateBase, DefaultModelFileStatePoco, ModelParsingInfo>
    where TAutomationConfig : AutomationConfig, new()
    {
        private ISimulatorClient<ModelStateBase, SimulatorRoutineRevision> __simulationClient;

        public DefaultModelLibrary(
            DefaultConfig<TAutomationConfig> config,
            CogniteDestination cdf,
            ILogger<DefaultModelLibrary<TAutomationConfig>> logger,
            FileStorageClient client,
            ISimulatorClient<ModelStateBase, SimulatorRoutineRevision> simulationClient,
            IExtractionStateStore store = null) :
            base(
                config.Connector.ModelLibrary,
                new List<SimulatorConfig> { config.Simulator },
                cdf,
                logger,
                client,
                store)
        {
            __simulationClient = simulationClient;
        }

        protected override async Task ExtractModelInformation(
        ModelStateBase state,
        CancellationToken token)
        {

            if (__simulationClient != null) {
                __simulationClient.ExtractModelInformation(state, token);
            } else {
                state.CanRead = true;
                state.ParsingInfo.SetSuccess();
            }
        }

        protected override ModelStateBase StateFromModelRevision(SimulatorModelRevision modelRevision)
        {
            var modelState = new DefaultModelFilestate(modelRevision.Id.ToString())
            {
                UpdatedTime = modelRevision.LastUpdatedTime,
                ModelExternalId = modelRevision.ModelExternalId,
                Source = modelRevision.SimulatorExternalId,
                DataSetId = modelRevision.DataSetId,
                CreatedTime = modelRevision.CreatedTime,
                CdfId = modelRevision.FileId,
                LogId = modelRevision.LogId,
                Version = modelRevision.VersionNumber,
                ExternalId = modelRevision.ExternalId,
            };
            return modelState;
        }

    }
}