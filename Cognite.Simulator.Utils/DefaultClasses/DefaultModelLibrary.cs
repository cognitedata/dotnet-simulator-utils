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

        protected override async Task ExtractModelInformation(
            TModelStateBase state,
            CancellationToken token
        )
        {
            if (simulatorClient != null)
            {
                await simulatorClient.ExtractModelInformation(state, token).ConfigureAwait(false);
            }
            else
            {
                state.CanRead = true;
                state.ParsingInfo.SetSuccess();
            }
        }

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