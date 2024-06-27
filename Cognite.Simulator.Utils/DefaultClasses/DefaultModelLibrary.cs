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
    /// <summary>
    /// A default version of the model library
    /// Should be used as a base class for anyone implementing their own connector
    /// </summary>
    /// <typeparam name="TAutomationConfig"></typeparam>
    public class DefaultModelLibrary<TAutomationConfig> :
    ModelLibraryBase<ModelStateBase, DefaultModelFileStatePoco, ModelParsingInfo>
    where TAutomationConfig : AutomationConfig, new()
    {
        private ISimulatorClient<ModelStateBase, SimulatorRoutineRevision> __simulationClient;

        /// <summary>
        /// Construct a default model library
        /// </summary>
        /// <param name="config"></param>
        /// <param name="cdf"></param>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        /// <param name="simulationClient"></param>
        /// <param name="store"></param>
        public DefaultModelLibrary(
            DefaultConfig<TAutomationConfig> config,
            CogniteDestination cdf,
            ILogger<DefaultModelLibrary<TAutomationConfig>> logger,
            FileStorageClient client,
            ISimulatorClient<ModelStateBase, SimulatorRoutineRevision> simulationClient,
            IExtractionStateStore store = null) :
            base(
                config?.Connector.ModelLibrary,
                new List<SimulatorConfig> { config.Simulator },
                cdf,
                logger,
                client,
                simulationClient,
                store)
        {
            __simulationClient = simulationClient;
        }

        /// <summary>
        /// Extract the model information, should be implemented on the instance of the ISimulatorClient
        /// </summary>
        /// <param name="state"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        protected override async Task ExtractModelInformation(
        ModelStateBase state,
        CancellationToken token)
        {

            if (__simulationClient != null) {
                await __simulationClient.ExtractModelInformation(state, token).ConfigureAwait(false);
            } else {
                state.CanRead = true;
                state.ParsingInfo.SetSuccess();
            }
        }

        /// <summary>
        /// Helps map the local state.db from the API data
        /// </summary>
        /// <param name="modelRevision"></param>
        /// <returns></returns>
        protected override ModelStateBase StateFromModelRevision(SimulatorModelRevision modelRevision)
        {
            var modelState = new DefaultModelFilestate(modelRevision?.Id.ToString())
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