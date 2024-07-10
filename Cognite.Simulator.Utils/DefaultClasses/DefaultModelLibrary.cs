using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{

    public class DefaultModelLibrary<TAutomationConfig,TModelStateBase> :
    ModelLibraryBase<TAutomationConfig,TModelStateBase, DefaultModelFileStatePoco, ModelParsingInfo>
    where TAutomationConfig : AutomationConfig, new()
    where TModelStateBase: ModelStateBase
    {
        private ISimulatorClient<TModelStateBase, SimulatorRoutineRevision> __simulationClient;

        public DefaultModelLibrary(
            DefaultConfig<TAutomationConfig> config,
            CogniteDestination cdf,
            ILogger<DefaultModelLibrary<TAutomationConfig,TModelStateBase>> logger,
            FileStorageClient client,
            IServiceProvider serviceProvider,
            IExtractionStateStore store = null) :
            base(
                config.Connector.ModelLibrary,
                new List<SimulatorConfig> { config.Simulator },
                cdf,
                logger,
                client,
                store)
        {
            __simulationClient = serviceProvider.GetService<ISimulatorClient<TModelStateBase, SimulatorRoutineRevision>>() ;
        }

        protected override async Task ExtractModelInformation(
        TModelStateBase state,
        CancellationToken token)
        {

            if (__simulationClient != null) {
                __simulationClient.ExtractModelInformation(state, token);
            } else {
                state.CanRead = true;
                state.ParsingInfo.SetSuccess();
            }
        }

        protected override TModelStateBase StateFromModelRevision(SimulatorModelRevision modelRevision)
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
            return modelState as TModelStateBase;
        }

    }
}