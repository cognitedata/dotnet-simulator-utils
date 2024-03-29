﻿using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using CogniteSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// This class represents a staging area for storing simulator related
    /// data in CDF. This is to be used for information outside the main 
    /// simulation data generated by connectors. Often the staged data is stored
    /// in CDF Raw and has to be parsed by an external process to be useful
    /// </summary>
    public class StagingArea<T> where T : StagingData
    {
        /// <summary>
        /// Indicates whether or not CDF RAW can be used
        /// </summary>
        public bool HasRawCapabilities { get; private set; }

        private readonly CogniteDestination _cdf;
        private readonly StagingConfig _config;
        private readonly CogniteConfig _cdfConfig;
        private readonly ILogger<StagingArea<T>> _logger;
        private bool _initialized;

        /// <summary>
        /// Initialize the staging area according to the given configuration
        /// </summary>
        /// <param name="cdf">DEF destination</param>
        /// <param name="config">Staging configuration</param>
        /// <param name="cdfConfig">CDF configuration</param>
        /// <param name="logger">Logger</param>
        public StagingArea(
            CogniteDestination cdf,
            StagingConfig config,
            CogniteConfig cdfConfig,
            ILogger<StagingArea<T>> logger)
        {
            _cdf = cdf;
            _config = config;
            _cdfConfig = cdfConfig;
            _logger = logger;
        }

        /// <summary>
        /// Update the staging entry with the given id with the contents of the object <paramref name="info"/>
        /// </summary>
        /// <param name="modelExternalId">Entry ID</param>
        /// <param name="info">Data to add to the entry</param>
        /// <param name="token">Cancellation token</param>
        /// <returns></returns>
        public async Task UpdateEntry(
            string modelExternalId,
            T info,
            CancellationToken token)
        {
            if (info == null || !_config.Enabled || string.IsNullOrEmpty(_config.Table))
            {
                return;
            }
            if (!_initialized)
            {
                await Init(token).ConfigureAwait(false);
            }
            if (!HasRawCapabilities)
            {
                return;
            }

            info.LastUpdatedTime = DateTime.UtcNow.ToUnixTimeMilliseconds();
            await _cdf.CogniteClient.Raw.CreateRowsAsync(
                _config.Database,
                _config.Table,
                new List<RawRowCreate<T>>()
                {
                        new RawRowCreate<T>
                        {
                            Key = modelExternalId,
                            Columns = info
                        }
                },
                ensureParent: true,
                token: token).ConfigureAwait(false);
        }
    
        private async Task Init(CancellationToken token)
        {
            try
            {
                var inspect = await _cdf.CogniteClient.Token
                    .InspectAsync(token)
                    .ConfigureAwait(false);
                var capabilities = inspect.Capabilities.ToList();
                var hasCapabilities = capabilities
                    .Where(c => c is RawAcl && c.All != null && c.ProjectsScope.Contains(_cdfConfig.Project))
                    .Where(c => c.Actions.Contains("READ") && c.Actions.Contains("LIST") && c.Actions.Contains("WRITE"))
                    .Any();
                if (hasCapabilities && _config.Enabled)
                {
                    await InitRawDatabase(token).ConfigureAwait(false);
                    HasRawCapabilities = true;
                }

                if (!hasCapabilities)
                {
                    _logger.LogError("Capabilities required by the connector are missing: rawAcl:READ, rawAcl:WRITE and rawAcl:LIST");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to inspect CDF capabilities. Requires projectsAcl:LIST and groupsAcl:LIST");
            }
            _initialized = true;
        }

        private async Task InitRawDatabase(
            CancellationToken token)
        {
            try
            {
                await _cdf.CogniteClient.Raw.CreateTablesAsync(
                    _config.Database,
                    new List<RawTable>()
                    {
                        new RawTable
                        {
                            Name = _config.Table
                        }
                    }, 
                    true, // Ensure database exists
                    token).ConfigureAwait(false);
            }
            catch (ResponseException ex) when (ex.Code == 400 && ex.Message.Contains("already created"))
            {
                _logger.LogDebug("Raw table already exists: {Name}", _config.Table);
            }
        }

        /// <summary>
        /// Retrieves the entry of type <typeparamref name="T"/> and ID <paramref name="id"/> from the staging area,
        /// if it exists
        /// </summary>
        /// <param name="id">Entry ID</param>
        /// <param name="token">Cancellation token</param>
        /// <returns></returns>
        public async Task<T> GetEntry(
            string id,
            CancellationToken token)
        {
            try
            {
                RawRow<T> row =  await _cdf.CogniteClient.Raw
                    .GetRowAsync<T>(_config.Database, _config.Table, id, token: token)
                    .ConfigureAwait(false);
                return row.Columns;
            }
            catch (ResponseException re) when (re.Code == 404)
            {
                return default;
            }
            catch (Exception e)
            {
                _logger.LogError("Could not fetch data from CDF RAW", e);
                return default;
            }
        }
    }

    /// <summary>
    /// Base class for staging data
    /// </summary>
    public class StagingData
    {
        /// <summary>
        /// Timestamp of when this entry was updated last
        /// </summary>
        public long LastUpdatedTime { get; set; }
    }

    /// <summary>
    /// Staging data representing the information produced during model parsing.
    /// That is, when a model is downloaded, the connector will try to open the model
    /// with the simulator, extract information from the model and update that information in CDF
    /// (if necessary). The parsing info should capture information about this process (logs, status, etc) 
    /// </summary>
    public class ModelParsingInfo : StagingData
    {
        /// <summary>
        /// Model name
        /// </summary>
        public string ModelName { get; set; }
        
        /// <summary>
        /// Simulator name
        /// </summary>
        public string Simulator { get; set; }
        
        /// <summary>
        /// Model version
        /// </summary>
        public int ModelVersion { get; set; }

        /// <summary>
        /// Model parsing status
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ParsingStatus Status { get; set; }
        
        /// <summary>
        /// Whether or not the model was parsed
        /// </summary>
        public bool Parsed { get; set; }
        
        /// <summary>
        /// If there were any errors during parsing
        /// </summary>
        public bool Error { get; set; }
        
        /// <summary>
        /// Model parsing log
        /// </summary>
        public List<ParsingLog> Log { get; set; } = new List<ParsingLog>();
    }
    
    /// <summary>
    /// Model parsing status
    /// </summary>
    public enum ParsingStatus
    {
        /// <summary>
        /// Model is ready to be parsed
        /// </summary>
        ready,
        /// <summary>
        /// Model parsing is in progress
        /// </summary>
        running,
        /// <summary>
        /// Parsing failed, but it is possible to retry
        /// </summary>
        retrying,
        /// <summary>
        /// Model successfully parsed
        /// </summary>
        success,
        /// <summary>
        /// Failed to parse the model
        /// </summary>
        failure
    }

    /// <summary>
    /// Parsing log entry
    /// </summary>
    public class ParsingLog
    {
        /// <summary>
        /// Creates a new parsing log entry
        /// </summary>
        /// <param name="level">Log level</param>
        /// <param name="message">Message</param>
        public ParsingLog(ParsingLogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        /// <summary>
        /// Log level
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ParsingLogLevel Level { get; set; }
        
        /// <summary>
        /// Log message
        /// </summary>
        public string Message { get; set; }

    }

    /// <summary>
    /// Parsing log level
    /// </summary>
    public enum ParsingLogLevel
    {
        /// <summary>
        /// Information level
        /// </summary>
        INFORMATION,
        /// <summary>
        /// Warning level
        /// </summary>
        WARNING,
        /// <summary>
        /// Error level
        /// </summary>
        ERROR
    }

    /// <summary>
    /// Extension utilities for the model parsing info
    /// </summary>
    public static class ModelParsingExtensions
    {
        /// <summary>
        /// Add information log to the model parsing info
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        /// <param name="message">Information message</param>
        public static void AddInfo(this ModelParsingInfo mpi, string message)
        {
            mpi.Add(ParsingLogLevel.INFORMATION, message);
        }

        /// <summary>
        /// Add warning log to the model parsing info
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        /// <param name="message">Information message</param>
        public static void AddWarning(this ModelParsingInfo mpi, string message)
        {
            mpi.Add(ParsingLogLevel.WARNING, message);
        }

        /// <summary>
        /// Add error log to the model parsing info
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        /// <param name="message">Information message</param>
        public static void AddError(this ModelParsingInfo mpi, string message)
        {
            mpi.Add(ParsingLogLevel.ERROR, message);
        }

        /// <summary>
        /// Update the model info status to success
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        /// <param name="message">Information message to add to the logs</param>
        public static void SetSuccess(this ModelParsingInfo mpi, string message = null)
        {
            mpi.SetStatus(ParsingStatus.success, true, false, ParsingLogLevel.INFORMATION, message);
        }

        /// <summary>
        /// Update the model info status to retrying
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        /// <param name="message">Warning message to add to the logs</param>
        public static void SetRetrying(this ModelParsingInfo mpi, string message = null)
        {
            mpi.SetStatus(ParsingStatus.retrying, false, true, ParsingLogLevel.WARNING, message);
        }

        /// <summary>
        /// Update the model info status to failure
        /// </summary>
        /// <param name="mpi">Model parsing info object</param>
        /// <param name="message">Error message to add to the logs</param>
        public static void SetFailure(this ModelParsingInfo mpi, string message = null)
        {
            mpi.SetStatus(ParsingStatus.failure, true, true, ParsingLogLevel.ERROR, message);
        }

        private static void SetStatus(this ModelParsingInfo mpi, ParsingStatus status, bool isParsed, bool isError, ParsingLogLevel level, string message = null)
        {
            mpi.Status = status;
            mpi.Parsed = isParsed;
            mpi.Error = isError;
            if (!string.IsNullOrEmpty(message))
            {
                mpi.Add(level, message);
            }
        }

        private static void Add(this ModelParsingInfo mpi, ParsingLogLevel level, string message)
        {
            mpi.Log.Add(
                new ParsingLog(
                    level,
                    message));
        }
    }

    /// <summary>
    /// Extensions to the Staging Area
    /// </summary>
    public static class StagingAreaExtensions
    {
        /// <summary>
        /// Adds a staging area to the service collection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="services">Service collection</param>
        /// <param name="config">Staging area configuration</param>
        public static void AddStagingArea<T>(this IServiceCollection services, StagingConfig config) where T : StagingData
        {
            services.AddSingleton<StagingConfig>(config);
            services.AddScoped<StagingArea<T>>();
        }
    }
}
