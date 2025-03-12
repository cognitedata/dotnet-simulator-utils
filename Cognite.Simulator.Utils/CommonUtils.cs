using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cognite.DataProcessing;
using Cognite.Extractor.Common;
using Cognite.Simulator.Extensions;

using CogniteSdk;
using CogniteSdk.Alpha;
using CogniteSdk.Resources;

using TimeRange = CogniteSdk.TimeRange;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Common utility methods. May be useful when developing simulator connectors
    /// </summary>
    public static class CommonUtils
    {
        /// <summary>
        /// Returns the assembly version
        /// </summary>
        /// <returns></returns>
        public static string GetAssemblyVersion()
        {
            return Extractor.Metrics.Version.GetVersion(
                    Assembly.GetExecutingAssembly(),
                    "0.0.1");
        }

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
        /// Run logical check and steady state detection based on a simulation configuration.
        /// Runs only if data sampling is enabled
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
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (config.DataSampling == null)
                throw new ArgumentException("DataSampling configuration is missing", nameof(config));
            if (config.DataSampling.SamplingWindow == null)
                throw new ArgumentException("Sampling window is missing", nameof(config));
            if (config.DataSampling.Granularity == null)
                throw new ArgumentException("Granularity is missing", nameof(config));

            var validationWindow = config.DataSampling.ValidationWindow;
            var logicalCheck = config.LogicalCheck.FirstOrDefault();
            var logicalCheckEnabled = logicalCheck != null && logicalCheck.Enabled;
            var steadyStateDetection = config.SteadyStateDetection.FirstOrDefault();
            var steadyStateDetectionEnabled = steadyStateDetection != null && steadyStateDetection.Enabled;

            if ((logicalCheckEnabled || steadyStateDetectionEnabled) && validationWindow == null)
            {
                // Validation window only required if logical check or steady state detection is enabled
                // This is already validated on the API side
                throw new SimulationException("Validation window is required for logical check and steady state detection");
            }

            var validationStart = validationEnd - TimeSpan.FromMinutes(validationWindow ?? 0);
            var validationConfiguration = new SamplingConfiguration(
                end: validationEnd.ToUnixTimeMilliseconds(),
                start: validationStart.ToUnixTimeMilliseconds()
            );

            // Perform logical check, if enabled
            TimeSeriesData validationDps = null;
            if (logicalCheckEnabled)
            {
                var dps = await dataPoints.GetSample(
                    logicalCheck.TimeseriesExternalId,
                    logicalCheck.Aggregate.ToDataPointAggregate(),
                    config.DataSampling.Granularity.Value,
                    validationConfiguration,
                    token).ConfigureAwait(false);
                validationDps = dps.ToTimeSeriesData(
                    config.DataSampling.Granularity.Value,
                    logicalCheck.Aggregate.ToDataPointAggregate());
                validationDps = LogicalCheckInternal(validationDps, validationConfiguration, logicalCheck);
            }

            // Check for steady state, if enabled
            TimeSeriesData ssdMap = null;

            if (steadyStateDetectionEnabled)
            {
                var ssDps = await dataPoints.GetSample(
                    steadyStateDetection.TimeseriesExternalId,
                    steadyStateDetection.Aggregate.ToDataPointAggregate(),
                    config.DataSampling.Granularity.Value,
                    validationConfiguration,
                    token).ConfigureAwait(false);

                if (!steadyStateDetection.MinSectionSize.HasValue || !steadyStateDetection.VarThreshold.HasValue || !steadyStateDetection.SlopeThreshold.HasValue)
                {
                    throw new SimulationException("Steady state detection configuration is missing required parameters");
                }

                ssdMap = Detectors.SteadyStateDetector(
                    ssDps.ToTimeSeriesData(
                        config.DataSampling.Granularity.Value,
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
            long? samplingEnd = validationConfiguration.End;
            var samplingWindowMs = (long)TimeSpan.FromMinutes(config.DataSampling.SamplingWindow.Value).TotalMilliseconds;
            if (feasibleTimestamps != null)
            {
                samplingEnd = DataSampling.FindSamplingTime(feasibleTimestamps, samplingWindowMs);
                if (!samplingEnd.HasValue)
                {
                    throw new SimulationException("Could not find a timestamp that can be used to sample process data under steady state");
                }
            }

            var samplingStart = samplingEnd.Value - samplingWindowMs;
            return new TimeRange
            {
                Min = samplingStart,
                Max = samplingEnd
            };
        }

        private static TimeSeriesData LogicalCheckInternal(
            TimeSeriesData ts,
            SamplingConfiguration samplingConfiguration,
            SimulatorRoutineRevisionLogicalCheck lcConfig)
        {
            if (!Enum.TryParse(lcConfig.Operator, true, out DataSampling.LogicOperator op))
            {
                throw new ArgumentException($"Logical check operator not recognized: {lcConfig.Operator}", nameof(lcConfig));
            }
            if (!lcConfig.Value.HasValue)
            {
                throw new ArgumentException("Logical check value is missing", nameof(lcConfig));
            }

            return DataSampling.LogicalCheck(ts, lcConfig.Value.Value, op, samplingConfiguration.End);
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
                (int)granularity * 60_000,
                aggreagate == Extensions.DataPointAggregate.StepInterpolation);
        }

        /// <summary>
        /// Load the input time series data point for the given input configuration.
        /// </summary>
        public static async Task<SimulatorValueItem> LoadTimeseriesSimulationInput(
            this Client _cdf,
            SimulatorRoutineRevisionInput inputTs,
            SimulatorRoutineRevisionConfiguration routineConfiguration,
            SamplingConfiguration samplingConfiguration,
            CancellationToken token)
        {
            if (inputTs == null)
            {
                throw new ArgumentNullException(nameof(inputTs));
            }
            if (routineConfiguration == null)
            {
                throw new ArgumentNullException(nameof(routineConfiguration));
            }
            if (samplingConfiguration == null)
            {
                throw new ArgumentNullException(nameof(samplingConfiguration));
            }
            if (_cdf == null)
            {
                throw new ArgumentNullException(nameof(_cdf));
            }

            double sampledValue;

            if (routineConfiguration.DataSampling.Enabled)
            {
                if (routineConfiguration.DataSampling.Granularity == null)
                {
                    throw new ArgumentException("Granularity is missing in the configuration", nameof(routineConfiguration));
                }
                var dps = await _cdf.DataPoints.GetSample(
                    inputTs.SourceExternalId,
                    inputTs.Aggregate.ToDataPointAggregate(),
                    routineConfiguration.DataSampling.Granularity.Value,
                    samplingConfiguration,
                    token).ConfigureAwait(false);
                var inputDps = dps.ToTimeSeriesData(
                    routineConfiguration.DataSampling.Granularity.Value,
                    inputTs.Aggregate.ToDataPointAggregate());

                if (inputDps.Count == 0)
                {
                    throw new SimulationException($"Could not find data points in input timeseries {inputTs.SourceExternalId}");
                }

                // This assumes the unit specified in the configuration is the same as the time series unit
                // No unit conversion is made
                sampledValue = inputDps.GetAverage();
            }
            else
            {
                var dp = await _cdf.DataPoints.GetLatestValue(inputTs.SourceExternalId, samplingConfiguration, token).ConfigureAwait(false);
                sampledValue = dp.Value;
            }

            return new SimulatorValueItem()
            {
                Value = new SimulatorValue.Double(sampledValue),
                Unit = inputTs.Unit != null ? new SimulatorValueUnit()
                {
                    Name = inputTs.Unit.Name
                } : null,
                Overridden = false,
                ReferenceId = inputTs.ReferenceId,
                TimeseriesExternalId = inputTs.SaveTimeseriesExternalId,
                ValueType = SimulatorValueType.DOUBLE,
            };
        }
    }
}
