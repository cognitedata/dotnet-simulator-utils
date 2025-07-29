using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extensions;
using Cognite.Extractor.Configuration;
using Cognite.Extractor.Utils;

using CogniteSdk.Alpha;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// This class can be used to link the connector to a extraction pipeline in CDF
    /// </summary>
    public class ExtractionPipeline
    {
        private readonly ILogger<ExtractionPipeline> _logger;
        private readonly CogniteDestination _cdf;
        private readonly CogniteConfig _cdfConfig;
        private readonly PipelineNotificationConfig _pipeConfig;
        private readonly SimulatorCreate _simulatorDefinition;
        private ConnectorConfig _connectorConfig;
        private CogniteSdk.ExtPipe _pipeline;
        private bool _disabled;

        internal static string LastErrorMessage;

        /// <summary>
        /// Creates a new extraction pipeline object. It is not yet active, it needs to
        /// be initialized with <see cref="Init(ConnectorConfig, CancellationToken)"/> and
        /// then activated by running the <see cref="PipelineUpdate(CancellationToken)"/> task
        /// </summary>
        /// <param name="cdfConfig">CDF configuration</param>
        /// <param name="simulatorDefinition">Simulator definition</param>
        /// <param name="pipeConfig">Pipeline notification configuration</param>
        /// <param name="destination">CDF client</param>
        /// <param name="logger">Logger</param>
        public ExtractionPipeline(
            CogniteConfig cdfConfig,
            SimulatorCreate simulatorDefinition,
            PipelineNotificationConfig pipeConfig,
            CogniteDestination destination,
            ILogger<ExtractionPipeline> logger)
        {
            _logger = logger;
            _cdf = destination;
            _simulatorDefinition = simulatorDefinition;
            _cdfConfig = cdfConfig;
            _pipeConfig = pipeConfig;
        }

        /// <summary>
        /// Initialized the extraction pipeline, if configured. This method creates a new
        /// pipeline in CDF in case one does not exists. It uses the simulator name and 
        /// dataset information specified in <paramref name="connectorConfig"/> to create a new pipeline
        /// </summary>
        /// <param name="connectorConfig">Connector config</param>
        /// <param name="token">Cancellation token</param>
        public async Task Init(
            ConnectorConfig connectorConfig,
            CancellationToken token)
        {
            if (connectorConfig == null)
            {
                throw new ArgumentNullException(nameof(connectorConfig));
            }
            _connectorConfig = connectorConfig;
            _disabled = _cdfConfig.ExtractionPipeline == null || string.IsNullOrEmpty(_cdfConfig.ExtractionPipeline.PipelineId);
            if (_disabled)
            {
                _logger.LogDebug("Extraction pipeline is not configured");
                return;
            }
            await TryInitRemotePipeline(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Try to initialize the extraction pipeline by creating it in CDF (if it does not exist)
        /// </summary>
        /// <param name="token">Cancellation token</param>
        private async Task<bool> TryInitRemotePipeline(CancellationToken token)
        {
            try
            {
                var pipelineId = _cdfConfig.ExtractionPipeline.PipelineId;
                var pipelines = await _cdf.CogniteClient.ExtPipes.RetrieveAsync(
                    [pipelineId],
                    true,
                    token).ConfigureAwait(false);
                if (!pipelines.Any())
                {
                    _logger.LogWarning(
                        "Could not find an extraction pipeline with id {Id}, attempting to create one",
                        pipelineId);
                    pipelines = await _cdf.CogniteClient.ExtPipes.CreateAsync(
                        new List<CogniteSdk.ExtPipeCreate>
                        {
                           new CogniteSdk.ExtPipeCreate
                           {
                               DataSetId = _connectorConfig.DataSetId,
                               ExternalId = pipelineId,
                               Name = $"{_simulatorDefinition.Name} connector extraction pipeline",
                               Schedule = "Continuous",
                               Source = _simulatorDefinition.ExternalId,
                           }
                        }, token).ConfigureAwait(false);
                    _logger.LogDebug("Pipeline {Id} created successfully", pipelineId);
                }
                _pipeline = pipelines.First();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not retrieve or create extraction pipeline from CDF: {Exception}", ex);
            }
            return _pipeline != null;
        }

        /// <summary>
        /// Notify the pipeline with the given status change and message
        /// </summary>
        /// <param name="status">Pipeline run status</param>
        /// <param name="message">Message</param>
        /// <param name="token">Cancellation token</param>
        public async Task NotifyPipeline(
            CogniteSdk.ExtPipeRunStatus status,
            string message,
            CancellationToken token)
        {
            if (_disabled)
            {
                return;
            }

            var available = _pipeline != null;

            if (!available)
            {
                available = await TryInitRemotePipeline(token).ConfigureAwait(false);
                if (!available)
                {
                    return;
                }
            }

            _logger.LogDebug("Notifying extraction pipeline, status: {Status}", status);

            try
            {
                await _cdf.CogniteClient.ExtPipes.CreateRunsAsync(
                [
                    new CogniteSdk.ExtPipeRunCreate
                    {
                        ExternalId = _pipeline.ExternalId,
                        Message = message.Truncate(1000),
                        Status = status
                    }
                ], token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not report status to extraction pipeline: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Starts a notification loop that reports the connector status to the 
        /// pipeline with the configured frequency
        /// </summary>
        /// <param name="token">Cancellation token</param>
        public async Task PipelineUpdate(CancellationToken token)
        {
            if (_disabled)
            {
                return;
            }
            var startMessage = LastErrorMessage != null ?
                $"Connector restarted after error: {LastErrorMessage}" :
                "Connector started";
            await NotifyPipeline(
                CogniteSdk.ExtPipeRunStatus.success,
                startMessage,
                token).ConfigureAwait(false);

            var delay = _cdfConfig.ExtractionPipeline.Frequency;
            while (!token.IsCancellationRequested)
            {
                await NotifyPipeline(
                   CogniteSdk.ExtPipeRunStatus.seen,
                   "Connector available",
                   token).ConfigureAwait(false);
                await Task.Delay(
                    TimeSpan.FromSeconds(delay),
                    token).ConfigureAwait(false);
            }
        }

        private static DateTime? firstErrorOccured;
        private static int errorCount;

        /// <summary>
        /// Notify the extraction pipeline in case of errors only when the
        /// number of errors exceeds the configured limit within the configured
        /// time frame.
        /// </summary>
        public async Task NotifyError(
            Exception e,
            CancellationToken token)
        {
            if (e == null)
            {
                throw new ArgumentNullException(nameof(e));
            }
            errorCount++;
            if (!firstErrorOccured.HasValue)
            {
                firstErrorOccured = DateTime.UtcNow;
            }
            LastErrorMessage = $"{e.Message}\n{e.StackTrace}";
            bool errorCountLimitExceeded = errorCount >= _pipeConfig.MaxErrors;
            bool timeLimitExceeded = DateTime.UtcNow - firstErrorOccured >= TimeSpan.FromMinutes(_pipeConfig.MaxTime);

            if (errorCountLimitExceeded || timeLimitExceeded)
            {
                // Notify pipeline if the number of errors per time window is exceeded
                if (errorCountLimitExceeded)
                {
                    await NotifyPipeline(
                        CogniteSdk.ExtPipeRunStatus.failure,
                        $"Connector failed more than {_pipeConfig.MaxErrors} times in the last {_pipeConfig.MaxTime} minutes: {e.Message}\n{e.StackTrace}",
                        token).ConfigureAwait(false);
                }

                firstErrorOccured = null;
                errorCount = 0;
            }
        }

    }

    /// <summary>
    /// Extension methods for extraction pipelines
    /// </summary>
    public static class ExtractionPipelineExtensions
    {
        /// <summary>
        /// Adds a extraction pipeline to the service collection
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <param name="config">Connector configuration</param>
        public static void AddExtractionPipeline(this IServiceCollection services, ConnectorConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            services.AddSingleton(config.PipelineNotification);
            services.AddScoped<ExtractionPipeline>();
        }

        /// <summary>
        /// Use `type: remote` to fetch the config from Fusion, or use `type: local` to use the local file instead
        /// Example from config.yml using the remote config from Fusion
        /// type: remote # this is required
        /// cognite:
        ///   project: ...
        ///   host: ...
        ///   extraction-pipeline:
        ///       pipeline-id: ... # as well as this
        ///   idp-authentication:
        ///       ...
        /// </summary>
        /// <typeparam name="T">The complete config object to be parsed</typeparam>
        /// <param name="services">Service collection</param>
        /// <param name="path">Path to config file</param>
        /// <param name="types">Types to use for deserialization</param>
        /// <param name="appId">App ID for measuring network data</param>
        /// <param name="token">Cancellation token</param>
        /// <param name="version">Config version</param>
        /// <param name="acceptedConfigVersions">Accepted config versions</param>
        public static async Task<T> AddConfiguration<T>(
            this IServiceCollection services,
            string path,
            Type[] types,
            string appId,
            CancellationToken token,
            int version = 1,
            int[] acceptedConfigVersions = null) where T : BaseConfig
        {
            var configTypes = new[] {
                typeof(CogniteConfig),
                typeof(LoggerConfig),
                typeof(HighAvailabilityConfig),
                typeof(Extractor.Metrics.MetricsConfig),
                typeof(Extractor.StateStorage.StateStoreConfig),
                typeof(BaseConfig)
            };
            var localConfig = services.AddConfig<T>(path, configTypes, new[] { version });

            var remoteConfig = new RemoteConfig
            {
                Type = localConfig.Type,
                Cognite = localConfig.Cognite
            };

            return await services.AddRemoteConfig<T>(
                logger: null,
                path: path,
                types: types,
                appId: appId,
                userAgent: null,
                setDestination: true,
                bufferConfigFile: false,
                remoteConfig: remoteConfig,
                token: token,
                acceptedConfigVersions: acceptedConfigVersions
            ).ConfigureAwait(false);
        }
    }
}
