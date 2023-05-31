using Cognite.Extensions;
using Cognite.Extractor.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    public class ExtractionPipeline
    {
        private readonly ILogger<ExtractionPipeline> _logger;
        private readonly CogniteDestination _cdf;
        private readonly CogniteConfig _cdfConfig;
        private readonly PipelineNotificationConfig _pipeConfig;
        private CogniteSdk.ExtPipe _pipeline;
        private bool _available;
        
        internal static string LastErrorMessage;

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
        public async Task Init(
            SimulatorConfig simConfig,
            CancellationToken token)
        {
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
                    _logger.LogWarning("Could not find an extraction pipeline with id {Id}", pipelineId);
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
                   "SimConnect available", 
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
}
