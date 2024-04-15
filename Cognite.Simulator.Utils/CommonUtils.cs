using Cognite.DataProcessing;
using Cognite.Extractor.Common;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using CogniteSdk.Alpha;
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

        /// <summary>
        /// Function to convert list of connectors to externalIds
        /// </summary>
        /// <param name="simulators">object of simulator connectors</param>
        /// <param name="baseConnectorName">the base connector name to which prefix will be appended</param>
        public static List<string> ConnectorsToExternalIds(Dictionary<string, long> simulators, string baseConnectorName)
        {
            var connectorIdList = new List<string>();
            if (simulators != null) {
                foreach (var simulator in simulators.Select((value, i) => new { i, value }))
                {
                    var value = simulator.value;
                    if (simulator.i > 0){
                        connectorIdList.Add($"{value.Key}-{baseConnectorName}");
                    } else {
                        connectorIdList.Add(baseConnectorName);
                    }
                }
            } else {
                connectorIdList.Add(baseConnectorName);
            }
            return connectorIdList;
        }
        
    }

    /// <summary>
    /// Collects utility methods used by the connector
    /// </summary>
    public static class SimulationUtils
    {

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
            SimulatorRoutineRevisionConfiguration config,
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
            var logicalCheck = config.LogicalCheck.FirstOrDefault();
            if (logicalCheck != null && logicalCheck.Enabled)
            {
                var dps = await dataPoints.GetSample(
                    logicalCheck.TimeseriesExternalId,
                    logicalCheck.Aggregate.ToDataPointAggregate(),
                    config.DataSampling.Granularity,
                    validationRange,
                    token).ConfigureAwait(false);
                validationDps = dps.ToTimeSeriesData(
                    config.DataSampling.Granularity,
                    logicalCheck.Aggregate.ToDataPointAggregate());
                validationDps = LogicalCheckInternal(validationDps, validationRange, logicalCheck, config.DataSampling);
            }

            // Check for steady state, if enabled
            TimeSeriesData ssdMap = null;
            var steadyStateDetection = config.SteadyStateDetection.FirstOrDefault();
            if (steadyStateDetection != null && steadyStateDetection.Enabled)
            {
                var ssDps = await dataPoints.GetSample(
                    steadyStateDetection.TimeseriesExternalId,
                    steadyStateDetection.Aggregate.ToDataPointAggregate(),
                    config.DataSampling.Granularity,
                    validationRange,
                    token).ConfigureAwait(false);

                if (!steadyStateDetection.MinSectionSize.HasValue || !steadyStateDetection.VarThreshold.HasValue || !steadyStateDetection.SlopeThreshold.HasValue)
                {
                    throw new SimulationException("Steady state detection configuration is missing required parameters");
                }

                ssdMap = Detectors.SteadyStateDetector(
                    ssDps.ToTimeSeriesData(
                        config.DataSampling.Granularity,
                        steadyStateDetection.Aggregate.ToDataPointAggregate()),
                    steadyStateDetection.MinSectionSize.Value,
                    steadyStateDetection.VarThreshold.Value,
                    steadyStateDetection.SlopeThreshold.Value);
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

        private static TimeSeriesData LogicalCheckInternal(TimeSeriesData ts, TimeRange validationRange, SimulatorRoutineRevisionLogicalCheck lcConfig, SimulatorRoutineRevisionDataSampling sampling)
        {
            if (!Enum.TryParse(lcConfig.Operator, true, out DataSampling.LogicOperator op))
            {
                throw new ArgumentException($"Logical check operator not recognized: {lcConfig.Operator}", nameof(lcConfig));
            }
            if (!lcConfig.Value.HasValue)
            {
                throw new ArgumentException("Logical check value is missing", nameof(lcConfig));
            }

            return DataSampling.LogicalCheck(ts, lcConfig.Value.Value, op, validationRange.Max);
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
