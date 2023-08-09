using Cognite.Extensions;
using Cognite.Extractor.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cognite.Extractor.Configuration;
using System.IO;

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
        private CogniteSdk.ExtPipe _pipeline;
        private bool _available;
        
        internal static string LastErrorMessage;

        /// <summary>
        /// Creates a new extraction pipeline object. It is not yet active, it needs to
        /// be initialized with <see cref="Init(SimulatorConfig, CancellationToken)"/> and
        /// then activated by running the <see cref="PipelineUpdate(CancellationToken)"/> task
        /// </summary>
        /// <param name="cdfConfig">CDF configuration</param>
        /// <param name="pipeConfig">Pipeline notification configuration</param>
        /// <param name="destination">CDF client</param>
        /// <param name="logger">Logger</param>
        public ExtractionPipeline(
            CogniteConfig cdfConfig,
            PipelineNotificationConfig pipeConfig,
            CogniteDestination destination,
            ILogger<ExtractionPipeline> logger)
        {
            _logger = logger;
            _cdf = destination;
            _cdfConfig = cdfConfig;
            _pipeConfig = pipeConfig;
        }
        
        /// <summary>
        /// Initialized the extraction pipeline, if configured. This method creates a new
        /// pipeline in CDF in case one does not exists. It uses the simulator name and 
        /// dataset information specified in <paramref name="simConfig"/> to create a new pipeline
        /// </summary>
        /// <param name="simConfig">Simulator configuration</param>
        /// <param name="token">Cancellation token</param>
        public async Task Init(
            SimulatorConfig simConfig,
            CancellationToken token)
        {
            if (simConfig == null)
            {
                throw new ArgumentNullException(nameof(simConfig));
            }
            if (_cdfConfig.ExtractionPipeline == null ||
                string.IsNullOrEmpty(_cdfConfig.ExtractionPipeline.PipelineId))
            {
                _logger.LogDebug("Extraction pipeline is not configured");
                _available = false;
                return;
            }
            try
            {
                var pipelineId = _cdfConfig.ExtractionPipeline.PipelineId;
                var pipelines = await _cdf.CogniteClient.ExtPipes.RetrieveAsync(
                    new[] { pipelineId }, 
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
                               DataSetId = simConfig.DataSetId,
                               ExternalId = pipelineId,
                               Name = $"{simConfig.Name} connector extraction pipeline",
                               Schedule = "Continuous",
                               Source = simConfig.Name,
                           }
                        }, token).ConfigureAwait(false);
                    _logger.LogDebug("Pipeline {Id} created successfully", pipelineId);
                }
                _pipeline = pipelines.First();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not retrieve or create extraction pipeline from CDF: {Message}", ex.Message);
                _available = false;
                return;
            }
            _available = true;
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
            if (!_available)
            {
                return;
            }
            try
            {
                await _cdf.CogniteClient.ExtPipes.CreateRunsAsync(new[]
                {
                    new CogniteSdk.ExtPipeRunCreate
                    {
                        ExternalId = _pipeline.ExternalId,
                        Message = message.Truncate(1000),
                        Status = status
                    }
                }, token).ConfigureAwait(false);
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
            if (!_available)
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
                _logger.LogDebug("Notifying extraction pipeline");
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
                    if (_available)
                    {
                        await NotifyPipeline(
                             CogniteSdk.ExtPipeRunStatus.failure,
                             $"Connector failed more than {_pipeConfig.MaxErrors} times in the last {_pipeConfig.MaxTime} minutes: {e.Message}\n{e.StackTrace}",
                             token).ConfigureAwait(false);
                    }
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
        /// Uses a minimum config that needs "cognite" and type: "specified",
        /// then uses that to either fetch remote Config Extraction Pipeline, or just adds
        /// the local one based on if "type" is remote or local.
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
            var localConfig = services.AddConfig<T>(path, version);
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
            );
        }
    }
}
