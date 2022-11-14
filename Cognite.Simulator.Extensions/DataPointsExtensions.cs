using CogniteSdk;
using CogniteSdk.Resources;
using Com.Cognite.V1.Timeseries.Proto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Extensions
{
    public static class DataPointsExtensions
    {
        public static async Task<(long[] Timestamps, double[] Values)> GetSample(
            this DataPointsResource dataPoints,
            string timeSeriesExternalId,
            DataPointAggregate aggregate,
            int granularity,
            SamplingRange timeRange,
            CancellationToken token
            )
        {
            if (timeRange == null)
            {
                throw new ArgumentNullException(nameof(timeRange));
            }
            var dps = await dataPoints.ListAsync(
                new DataPointsQuery
                {
                    Items = new[] { new DataPointsQueryItem {
                        ExternalId = timeSeriesExternalId,
                        Aggregates = new[] { aggregate.AsString() },
                        Start = $"{timeRange.Start.Value}",
                        End = $"{timeRange.End.Value + 1}", // Add 1 because end is exclusive
                        Granularity = MinutesToGranularity(granularity),
                        Limit = 10_000 // TODO: Functionality to make sure we get all data points
                    }},
                    IgnoreUnknownIds = true
                }, token
            ).ConfigureAwait(false);
            if (dps.Items.Any() && dps.Items.First().DatapointTypeCase == DataPointListItem.DatapointTypeOneofCase.AggregateDatapoints)
            {
                return dps.Items.First().ToTimeSeriesData(aggregate);
            }
            else if (aggregate == DataPointAggregate.StepInterpolation)
            {
                // If no data point is found before the time range,
                // search for the first data point forward in time.
                var firstDp = await dataPoints.ListAsync(
                    new DataPointsQuery
                    {
                        Items = new[] {
                                new DataPointsQueryItem {
                                    ExternalId = timeSeriesExternalId,
                                    Start = $"{timeRange.Start.Value}",
                                    Limit = 1
                                }
                        },
                        IgnoreUnknownIds = true
                    }, token
                ).ConfigureAwait(false);
                if (firstDp.Items.Any() && firstDp.Items.First().DatapointTypeCase == DataPointListItem.DatapointTypeOneofCase.NumericDatapoints)
                {
                    return firstDp.Items.First().ToTimeSeriesData(aggregate);
                }

            }
            throw new DataPointSampleNotFoundException($"No data points were found for time series '{timeSeriesExternalId}' in the sampling window");
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
                    return(
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.Max).ToArray());
                case DataPointAggregate.Min:
                    return (
                        values.Select(dp => dp.Timestamp).ToArray(),
                        values.Select(dp => dp.Min).ToArray());
                case DataPointAggregate.Count:
                    return(
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

    public enum DataPointAggregate
    {
        Average,
        Max,
        Min,
        Count,
        Sum,
        Interpolation,
        StepInterpolation,
        TotalVariation,
        ContinuousVariance,
        DiscreteVariance,
    }
}
