﻿using Cognite.Extractor.Common;
using Cognite.Extractor.Utils;
using CogniteSdk;
using System;
using System.Collections.Generic;
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
    public class StagingArea
    {
        private readonly CogniteDestination _cdf;
        private readonly StagingConfig _config;

        public StagingArea(
            CogniteDestination cdf,
            StagingConfig config)
        {
            _cdf = cdf;
            _config = config;
        }

        private async Task UpdateModelParsingInfo(
            ModelStateBase modelState,
            ModelParsingInfo info,
            CancellationToken token)
        {
            info.LastUpdatedTime = DateTime.UtcNow.ToUnixTimeMilliseconds();
            await _cdf.CogniteClient.Raw.CreateRowsAsync(
                _config.Database,
                _config.ModelParsingLogTable,
                new List<RawRowCreate<ModelParsingInfo>>()
                {
                        new RawRowCreate<ModelParsingInfo>
                        {
                            Key = modelState.Id,
                            Columns = info
                        }
                },
                token: token).ConfigureAwait(false);
        }
    }

    public class ModelParsingInfo
    {
        public string ModelName { get; set; }
        public string Simulator { get; set; }
        public int ModelVersion { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ParsingStatus Status { get; set; }
        public bool Parsed { get; set; }
        public bool Error { get; set; }
        public long LastUpdatedTime { get; set; }
        public List<ParsingLog> Log { get; set; } = new List<ParsingLog>();
    }
    
    public enum ParsingStatus
    {
        ready,
        running,
        retrying,
        success,
        failure
    }

    public class ParsingLog
    {
        public ParsingLog(ParsingLogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ParsingLogLevel Level { get; set; }
        public string Message { get; set; }

    }

    public enum ParsingLogLevel
    {
        INFORMATION,
        WARNING,
        ERROR
    }

    public static class ModelParsingExtensions
    {
        public static void LogInfo(this ModelParsingInfo mpi, string message)
        {
            mpi.Log(ParsingLogLevel.INFORMATION, message);
        }

        public static void LogWarning(this ModelParsingInfo mpi, string message)
        {
            mpi.Log(ParsingLogLevel.WARNING, message);
        }

        public static void LogError(this ModelParsingInfo mpi, string message)
        {
            mpi.Log(ParsingLogLevel.ERROR, message);
        }

        public static void Log(this ModelParsingInfo mpi, ParsingLogLevel level, string message)
        {
            mpi.Log.Add(
                new ParsingLog(
                    level,
                    message));
        }
    }
}
