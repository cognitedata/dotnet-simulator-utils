﻿using Cognite.DataProcessing;
using Cognite.Extractor.Common;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using CogniteSdk.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TimeRange = CogniteSdk.TimeRange;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Common utility methods. May be useful when developing simulator connectors
    /// </summary>
    public static class CommonUtils
    {
        /// <summary>
        /// Run all of the tasks in this enumeration. If any fail or is canceled, cancel the
        /// remaining tasks and return. The first found exception is thrown
        /// </summary>
        /// <param name="tasks">List of tasks to run</param>
        /// <param name="tokenSource">Source of cancellation tokens</param>
        public static async Task RunAll(this IEnumerable<Task> tasks, CancellationTokenSource tokenSource)
        {
            if (tokenSource == null)
            {
                throw new ArgumentNullException(nameof(tokenSource));
            }
            Exception ex = null;
            var taskList = tasks.ToList();
            while (taskList.Any())
            {
                // Wait for any of the tasks to finish or fail
                var task = await Task.WhenAny(taskList).ConfigureAwait(false);
                taskList.Remove(task);
                if (task.IsFaulted || task.IsCanceled)
                {
                    // If one of the tasks fail, cancel the token source, stopping the remaining tasks
                    tokenSource.Cancel();
                    if (task.Exception != null)
                    {
                        ex = task.Exception;
                    }
                }
            }
            if (ex != null)
            {
                throw ex;
            }
        }
    }

    /// <summary>
    /// Collects utility methods used by the connector
    /// </summary>
    public static class SimulationUtils
    {
        /// <summary>
        /// Parses strings in the format <c>number(w|d|h|m|s)</c> to a <see cref="TimeSpan"/>
        /// object
        /// </summary>
        /// <param name="time">Time string</param>
        public static TimeSpan ConfigurationTimeStringToTimeSpan(string time)
        {
            var repeatRegex = Regex.Match(
                time,
                @"((?<weeks>\d+)w)|((?<days>\d+)d)|((?<hours>\d+)h)|((?<minutes>\d+)m)|((?<seconds>\d+)s)", RegexOptions.Compiled);
            if (repeatRegex.Groups["weeks"].Success)
            {
                var hours = int.Parse(repeatRegex.Groups["weeks"].Value) * 168;
                return TimeSpan.FromHours(hours);
            }
            if (repeatRegex.Groups["days"].Success)
            {
                return TimeSpan.FromDays(int.Parse(repeatRegex.Groups["days"].Value));
            }
            if (repeatRegex.Groups["hours"].Success)
            {
                return TimeSpan.FromHours(int.Parse(repeatRegex.Groups["hours"].Value));
            }
            if (repeatRegex.Groups["minutes"].Success)
            {
                return TimeSpan.FromMinutes(int.Parse(repeatRegex.Groups["minutes"].Value));
            }
            if (repeatRegex.Groups["seconds"].Success)
            {
                return TimeSpan.FromSeconds(int.Parse(repeatRegex.Groups["seconds"].Value));
            }
            throw new ArgumentException("Cannot parse provided string to a TimeSpan", nameof(time));
        }

        /// <summary>
        /// Run logical check and steady state detection based on a simulation configuration. 
        /// </summary>
        /// <param name="dataPoints">CDF data points resource</param>
        /// <param name="config">Simulation configuration</param>
        /// <param name="validationEnd">Time of the end of the validation check</param>
        /// <param name="token">Cancellation token</param>
        /// <returns></returns>
        public static async Task<TimeRange> RunSteadyStateAndLogicalCheck(
            DataPointsResource dataPoints,
            SimulationConfigurationWithDataSampling config,
            DateTime validationEnd,
            CancellationToken token)
        {
            if (config == null || config.DataSampling == null)
            {
                throw new SimulationException("Data sampling configuration is missing");
            }
            var validationStart = validationEnd - TimeSpan.FromMinutes(config.DataSampling.ValidationWindow);
            var validationRange = new TimeRange
            {
                Min = validationStart.ToUnixTimeMilliseconds(),
                Max = validationEnd.ToUnixTimeMilliseconds()
            };

            // Check for sensor status, if enabled
            TimeSeriesData validationDps = null;
            if (config.LogicalCheck != null && config.LogicalCheck.Enabled)
            {
                var dps = await dataPoints.GetSample(
                    config.LogicalCheck.ExternalId,
                    config.LogicalCheck.AggregateType.ToDataPointAggregate(),
                    config.DataSampling.Granularity,
                    validationRange,
                    token).ConfigureAwait(false);
                validationDps = dps.ToTimeSeriesData(
                    config.DataSampling.Granularity,
                    config.LogicalCheck.AggregateType.ToDataPointAggregate());
                validationDps = LogicalCheckInternal(validationDps, validationRange, config.LogicalCheck, config.DataSampling);
            }

            // Check for steady state, if enabled
            TimeSeriesData ssdMap = null;
            if (config.SteadyStateDetection != null && config.SteadyStateDetection.Enabled)
            {
                var ssDps = await dataPoints.GetSample(
                    config.SteadyStateDetection.ExternalId,
                    config.SteadyStateDetection.AggregateType.ToDataPointAggregate(),
                    config.DataSampling.Granularity,
                    validationRange,
                    token).ConfigureAwait(false);

                ssdMap = Detectors.SteadyStateDetector(
                    ssDps.ToTimeSeriesData(
                        config.DataSampling.Granularity,
                        config.SteadyStateDetection.AggregateType.ToDataPointAggregate()),
                    config.SteadyStateDetection.MinSectionSize,
                    config.SteadyStateDetection.VarThreshold,
                    config.SteadyStateDetection.SlopeThreshold);
            }

            TimeSeriesData feasibleTimestamps;
            if (validationDps == null)
            {
                feasibleTimestamps = ssdMap;
            }
            else if (ssdMap == null)
            {
                feasibleTimestamps = validationDps;
            }
            else
            {
                // Find union between well status and steady state
                feasibleTimestamps = DataSampling.UnionLogicTimeSeries(validationDps, ssdMap);
            }

            // Find sampling start/end
            var samplingEnd = validationRange.Max;
            var samplingWindowMs = (long)TimeSpan.FromMinutes(config.DataSampling.SamplingWindow).TotalMilliseconds;
            if (feasibleTimestamps != null)
            {
                samplingEnd = DataSampling.FindSamplingTime(feasibleTimestamps, samplingWindowMs);
                if (!samplingEnd.HasValue)
                {
                    throw new SimulationException("Could not find a timestamp that can be used to sample process data under steady state");
                }
            }

            var samplingStart = samplingEnd - samplingWindowMs;
            return new TimeRange
            {
                Min = samplingStart,
                Max = samplingEnd
            };
        }

        private static TimeSeriesData LogicalCheckInternal(TimeSeriesData ts, TimeRange validationRange, LogicalCheckConfiguration lcConfig, DataSamplingConfiguration sampling)
        {
            if (!Enum.TryParse(lcConfig.Check, true, out DataSampling.LogicOperator op))
            {
                throw new ArgumentException($"Logical check operator not recognized: {lcConfig.Check}", nameof(lcConfig));
            }

            return DataSampling.LogicalCheck(ts, lcConfig.Value, op, validationRange.Max);
        }

        /// <summary>
        /// Convert data points sampled from CDF to time series data used by
        /// the <see cref="DataProcessing"/> library
        /// </summary>
        /// <param name="dataPoints">Input data points</param>
        /// <param name="granularity">Granularity in minutes</param>
        /// <param name="aggreagate">CDF aggregate type</param>
        /// <returns>Time series data</returns>
        public static TimeSeriesData ToTimeSeriesData(
            this (long[] Timestamps, double[] Values) dataPoints,
            int granularity,
            Extensions.DataPointAggregate aggreagate)
        {
            return new TimeSeriesData(
                dataPoints.Timestamps,
                dataPoints.Values,
                granularity * 60_000,
                aggreagate == Extensions.DataPointAggregate.StepInterpolation);
        }

    }
}
