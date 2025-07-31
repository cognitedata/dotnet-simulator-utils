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

    /// <summary>
    /// Default implementation of a model library for a simulator
    /// </summary>
    /// <typeparam name="TAutomationConfig">Type of the automation configuration</typeparam>
    /// <typeparam name="TModelStateBase">Type of the model state</typeparam>
    /// <typeparam name="TModelStateBasePoco">Type of the model state POCO</typeparam>
    public class DefaultModelLibrary<TAutomationConfig, TModelStateBase, TModelStateBasePoco> :
    ModelLibraryBase<TAutomationConfig, TModelStateBase, TModelStateBasePoco, ModelParsingInfo>
    where TAutomationConfig : AutomationConfig, new()
    where TModelStateBase : ModelStateBase, new()
    where TModelStateBasePoco : ModelStateBasePoco
    {
        private ISimulatorClient<TModelStateBase, SimulatorRoutineRevision> simulatorClient;

        /// <summary>
        /// Creates an instance of the model library
        /// </summary>
        public DefaultModelLibrary(
            DefaultConfig<TAutomationConfig> config,
            CogniteDestination cdf,
            ILogger<DefaultModelLibrary<TAutomationConfig, TModelStateBase, TModelStateBasePoco>> logger,
            ISimulatorClient<TModelStateBase, SimulatorRoutineRevision> simulatorClient,
            SimulatorCreate simulatorDefinition,
            FileStorageClient client,
            IExtractionStateStore store = null) :
            base(
                config?.Connector.ModelLibrary,
                simulatorDefinition,
                cdf,
                logger,
                client,
                store)
        {
            this.simulatorClient = simulatorClient;
        }

        /// <summary>
        /// This method should open the model versions in the simulator, extract the required information and
        /// ingest it to CDF. 
        /// </summary>
        /// <param name="state">Model file states</param>
        /// <param name="token">Cancellation token</param>
        protected override async Task ExtractModelInformation(
            TModelStateBase state,
            CancellationToken token
        )
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            await simulatorClient.ExtractModelInformation(state, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a state object of type <typeparamref name="TModelStateBase"/> from a
        /// CDF Simulator model revision passed as parameter
        /// </summary>
        /// <param name="modelRevision">CDF Simulator model revision</param>
        /// <returns>File state object</returns>
        protected override TModelStateBase StateFromModelRevision(SimulatorModelRevision modelRevision)
        {
            if (modelRevision == null)
            {
                throw new ArgumentNullException(nameof(modelRevision));
            }
            var output = new TModelStateBase
            {
                Id = modelRevision.Id.ToString(),
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
            return output;
        }

    }
}