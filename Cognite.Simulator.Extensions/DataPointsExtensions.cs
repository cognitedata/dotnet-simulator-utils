using CogniteSdk;
using CogniteSdk.Resources;
using Com.Cognite.V1.Timeseries.Proto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Class containing extensions to the CDF Data points resource with utility methods for simulator integrations
    /// </summary>
    public static class DataPointsExtensions
    {
        /// <summary>
        /// Sample a time series data points with the given time samplingConfiguration, granularity and aggregation method 
        /// </summary>
        /// <param name="dataPoints">CDF data points resource</param>
        /// <param name="timeSeriesExternalId">Time series external id</param>
        /// <param name="aggregate">Aggregation method</param>
        /// <param name="granularity">Time granularity in minutes</param>
        /// <param name="samplingConfiguration">Sampling configuration (start and end sampling time)</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>An array with the timestamps and one with the values</returns>
        /// <exception cref="ArgumentNullException">Thrown when the time samplingConfiguration is null</exception>
        /// <exception cref="DataPointSampleNotFoundException">Thrown when no data points where found based on samplingConfiguration</exception>
        public static async Task<(long[] Timestamps, double[] Values)> GetSample(
            this DataPointsResource dataPoints,
            string timeSeriesExternalId,
            DataPointAggregate aggregate,
            int granularity,
            SamplingConfiguration samplingConfiguration,
            CancellationToken token
            )
        {
            if (samplingConfiguration == null)
            {
                throw new ArgumentNullException(nameof(samplingConfiguration));
            }
            // If the start time is specified, we sample data points with aggregates
            if (samplingConfiguration.Start == null)
            {
                throw new ArgumentException("Start time must be specified", nameof(samplingConfiguration));
            }
            var dps = await dataPoints.ListAsync(
                new DataPointsQuery
                {
                    Items = new[]
                    {
                        new DataPointsQueryItem
                        {
                            ExternalId = timeSeriesExternalId,
                            Aggregates = new[] {aggregate.AsString()},
                            Start = $"{samplingConfiguration.Start.Value}",
                            End = $"{samplingConfiguration.End + 1}", // Add 1 because end is exclusive
                            Granularity = MinutesToGranularity((int) granularity),
                            Limit = 10_000 // TODO: Functionality to make sure we get all data points
                        }
                    },
                    IgnoreUnknownIds = true
                }, token
            ).ConfigureAwait(false);
            if (dps.Items.Any() && dps.Items.First().DatapointTypeCase ==
            DataPointListItem.DatapointTypeOneofCase.AggregateDatapoints)
            {
                return dps.Items.First().ToTimeSeriesData(aggregate);
            }

            if (aggregate == DataPointAggregate.StepInterpolation)
            {
                // If no data point is found before the time samplingConfiguration,
                // search for the first data point forward in time.
                var firstDp = await dataPoints.ListAsync(
                    new DataPointsQuery
                    {
                        Items = new[]
                        {
                            new DataPointsQueryItem
                            {
                                ExternalId = timeSeriesExternalId,
                                Start = $"{samplingConfiguration.Start.Value}",
                                Limit = 1
                            }
                        },
                        IgnoreUnknownIds = true
                    }, token
                ).ConfigureAwait(false);
                if (firstDp.Items.Any() && firstDp.Items.First().DatapointTypeCase ==
                DataPointListItem.DatapointTypeOneofCase.NumericDatapoints)
                {
                    return firstDp.Items.First().ToTimeSeriesData(aggregate);
                }
            }
            throw new DataPointSampleNotFoundException(
                $"No data points were found for time series '{timeSeriesExternalId}' in the sampling window");
        }

        /// <summary>
        /// Get the latest value of a time series before a given time
        /// </summary>
        /// <param name="dataPoints">CDF data points resource</param>
        /// <param name="timeSeriesExternalId">Time series external id</param>
        /// <param name="samplingConfiguration">Sampling configuration (start and end sampling time)</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>An array with the timestamps and one with the values</returns>
        /// <exception cref="ArgumentNullException">Thrown when the time samplingConfiguration is null</exception>
        /// <exception cref="DataPointSampleNotFoundException">Thrown when no data points where found based on samplingConfiguration</exception>
        public static async Task<(long Timestamp, double Value)> GetLatestValue(
            this DataPointsResource dataPoints,
            string timeSeriesExternalId,
            SamplingConfiguration samplingConfiguration,
            CancellationToken token
            )
        {
            if (samplingConfiguration == null)
            {
                throw new ArgumentNullException(nameof(samplingConfiguration));
            }

            var dps = await dataPoints.LatestAsync(
                new DataPointsLatestQuery
                {
                    Items = new List<IdentityWithBefore>
                    {
                        new IdentityWithBefore(
                            externalId: timeSeriesExternalId,
                            before: $"{samplingConfiguration.End}"
                        )
                    }
                }, token
            ).ConfigureAwait(false);
            if (dps.Any() && dps.First().IsString == false)
            {
                var dp = dps.First().DataPoints.First();
                if (dp.Value is MultiValue.Double doubleValue)
                {
                    return (dp.Timestamp, doubleValue.Value);
                }
            }
            throw new DataPointSampleNotFoundException(
                $"No numerical data points were found for time series '{timeSeriesExternalId}' before {new DateTime(samplingConfiguration.End)}");
        }

        private static (long[] Timestamps, double[] Values) ToTimeSeriesData(this DataPointListItem dps, DataPointAggregate aggregate)
        {
            if (aggregate == DataPointAggregate.StepInterpolation && dps.DatapointTypeCase == DataPointListItem.DatapointTypeOneofCase.NumericDatapoints)
            {
                var numericDps = dps.NumericDatapoints.Datapoints;
                return (
                    numericDps.Select(ndp => ndp.Timestamp).ToArray(),
                    numericDps.Select(ndp => ndp.Value).ToArray());
            }
            if (dps.DatapointTypeCase != DataPointListItem.DatapointTypeOneofCase.AggregateDatapoints)
            {
                throw new ArgumentException("Expected a data point aggregate list", nameof(dps));
            }
            var values = dps.AggregateDatapoints.Datapoints;
            switch (aggregate)
            {
                case DataPointAggregate.Average:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.Average).ToArray());
                case DataPointAggregate.Max:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.Max).ToArray());
                case DataPointAggregate.Min:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.Min).ToArray());
                case DataPointAggregate.Count:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.Count).ToArray());
                case DataPointAggregate.Sum:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.Sum).ToArray());
                case DataPointAggregate.Interpolation:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.Interpolation).ToArray());
                case DataPointAggregate.StepInterpolation:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.StepInterpolation).ToArray()); // defined as step time series
                case DataPointAggregate.TotalVariation:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.TotalVariation).ToArray());
                case DataPointAggregate.ContinuousVariance:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.ContinuousVariance).ToArray());
                case DataPointAggregate.DiscreteVariance:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.DiscreteVariance).ToArray());
                default:
                    throw new ArgumentException($"Invalid aggregate: {aggregate}", nameof(aggregate));
            }
        }

        internal static string AsString(this DataPointAggregate aggregate)
        {
            switch (aggregate)
            {
                case DataPointAggregate.Average: return "average";
                case DataPointAggregate.Max: return "max";
                case DataPointAggregate.Min: return "min";
                case DataPointAggregate.Count: return "count";
                case DataPointAggregate.Sum: return "sum";
                case DataPointAggregate.Interpolation: return "interpolation";
                case DataPointAggregate.StepInterpolation: return "stepInterpolation";
                case DataPointAggregate.TotalVariation: return "totalVariation";
                case DataPointAggregate.ContinuousVariance: return "continuousVariance";
                case DataPointAggregate.DiscreteVariance: return "discreteVariance";
                default:
                    throw new ArgumentException($"Invalid aggregate type: {aggregate.AsString()}", nameof(aggregate));

            }
        }

        /// <summary>
        /// Attempts to convert and string to a data point aggregate method
        /// </summary>
        /// <param name="aggregate">Aggregate method</param>
        /// <returns>Aggregate enum value</returns>
        /// <exception cref="ArgumentException">Thrown when the string cannot be converted</exception>
        public static DataPointAggregate ToDataPointAggregate(this string aggregate)
        {
            switch (aggregate)
            {
                case "average": return DataPointAggregate.Average;
                case "max": return DataPointAggregate.Max;
                case "min": return DataPointAggregate.Min;
                case "count": return DataPointAggregate.Count;
                case "sum": return DataPointAggregate.Sum;
                case "interpolation": return DataPointAggregate.Interpolation;
                case "stepInterpolation": return DataPointAggregate.StepInterpolation;
                case "totalVariation": return DataPointAggregate.TotalVariation;
                case "continuousVariance": return DataPointAggregate.ContinuousVariance;
                case "discreteVariance": return DataPointAggregate.DiscreteVariance;
                default:
                    throw new ArgumentException($"Invalid aggregate type: {aggregate}", nameof(aggregate));

            }

        }

        /// <summary>
        /// Convert data sampling granularity specified in minutes to a string format
        /// accepted by CDF, and conforming to its limits.
        /// Fractional granularity is not allowed, and the values are cast to int
        /// </summary>
        public static string MinutesToGranularity(int minutes)
        {
            if (minutes <= 120)
            {
                return $"{minutes}m";
            }
            var timespan = TimeSpan.FromMinutes(minutes);
            int hours = (int)timespan.TotalHours;
            if (hours <= 100_000)
            {
                return $"{hours}h";
            }
            int days = (int)timespan.TotalDays;
            if (days <= 100_000)
            {
                return $"{days}d";
            }
            throw new ArgumentException($"Granularity of {minutes} minutes exceeds the limit of 100.000 days");
        }
    }

    /// <summary>
    /// Represent errors related to reading data point samples from CDF
    /// </summary>
    public class DataPointSampleNotFoundException : Exception
    {
        /// <summary>
        /// Creates a new exception containing the provided <paramref name="message"/>
        /// </summary>
        public DataPointSampleNotFoundException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Represents a data point aggregate method.
    /// See <see href="https://docs.cognite.com/dev/concepts/aggregation/">Aggregation documentation</see>
    /// </summary>
    public enum DataPointAggregate
    {
        /// <summary>
        /// Average
        /// </summary>
        Average,

        /// <summary>
        /// Maximum
        /// </summary>
        Max,

        /// <summary>
        /// Minimum
        /// </summary>
        Min,

        /// <summary>
        /// Count
        /// </summary>
        Count,

        /// <summary>
        /// Sum
        /// </summary>
        Sum,

        /// <summary>
        /// Continuous interpolation
        /// </summary>
        Interpolation,

        /// <summary>
        /// Stepwise interpolation
        /// </summary>
        StepInterpolation,

        /// <summary>
        /// Total variance
        /// </summary>
        TotalVariation,

        /// <summary>
        /// Continuous variance
        /// </summary>
        ContinuousVariance,

        /// <summary>
        /// Discrete variance
        /// </summary>
        DiscreteVariance,
    }
}
