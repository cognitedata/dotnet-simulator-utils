using System;

namespace Cognite.DataProcessing
{
    /// <summary>
    /// Class containing routines for data sampling
    /// </summary>
    public static class DataSampling
    {
        /// <summary>
        /// Finds the union of two logical mapping time series (which contains values of 0 and 1).
        /// </summary>
        /// <param name="ts1">A TimeSeriesData object</param>
        /// <param name="ts2">A TimeSeriesData object</param>
        /// <returns>
        /// TimeSeriesData with the union of the provided time series.
        /// </returns>
        public static TimeSeriesData UnionLogicTimeSeries(TimeSeriesData ts1, TimeSeriesData ts2)
        {
            // align the provided time series
            var (ts1Resampled, ts2Resampled) = TimeSeriesUtils.ExecuteTimeSeriesAlignment(ts1, ts2);

            // check if the time series are of the same length
            if (ts1Resampled.Time.Length != ts2Resampled.Time.Length)
                throw new ArgumentException("It was not possible to align the given time series.");

            // extract the data from the time series
            long[] x = ts1Resampled.Time;
            double[] y1 = ts1Resampled.Data;
            double[] y2 = ts2Resampled.Data;
            double[] y3 = new double[x.Length];

            for (int i = 0; i < x.Length; i++)
            {
                // the input time series contain values of 1 and 0. The resulting time series will only be 1 if both
                // input time series have a value of 1.
                if (y1[i] == 1.0 && y2[i] == 1.0)
                    y3[i] = 1.0;
                else
                    y3[i] = 0.0;
            }
            return new TimeSeriesData(time: x, data: y3, granularity: ts1Resampled.Granularity, isStep: true);
        }

        /// <summary>
        /// Checks the most recent timestamp that can be used to sample process data under valid conditions.
        /// </summary>
        /// <param name="logicMap">A TimeSeriesData object containing a logical map</param>
        /// <param name="minWindow">Minimum continuous window</param>
        /// <returns>
        /// Long with the timestamp (milliseconds since epoch) for the most recent point in time that can be used to
        /// sample process data under valid conditions.
        /// </returns>
        public static long? FindSamplingTime(TimeSeriesData logicMap, long minWindow)
        {
            if (logicMap == null)
                throw new ArgumentNullException(nameof(logicMap), "The input data is empty");

            // store locally the time and data arrays
            long[] x = logicMap.Time;
            double[] y = logicMap.Data;

            // initialize the end timestamp
            long t1 = x[x.Length - 1];

            // iterate over all ssMap values starting from the end
            for (int i = x.Length - 2; i >= 0; i--)
            {
                // check for SS condition
                if (y[i] == 1.0)
                {
                    // update the start timestamp
                    long t0 = x[i];
                    // calculate the window size
                    long windowSize = t1 - t0;

                    // check if the current window versus the minimum window size
                    if (windowSize >= minWindow)
                    {
                        // return the end timestamp
                        return t1;
                    }
                }
                else
                {
                    // if transient conditions are found, update end timestamp
                    t1 = x[i];
                }
            }

            // if no value is found return null
            return null;
        }

        /// <summary>
        /// Executes a logical check for the given time series.
        /// </summary>
        /// <param name="ts">The time series to evaluate</param>
        /// <param name="threshold">The threshold to use for the logical check</param>
        /// <param name="check">The logical check to use</param>
        /// <param name="endTime">End time in milliseconds since Epoch</param>
        /// <returns>
        /// Time series with the logical check status (0: conditions not met, 1: conditions met) for all timestamps.
        /// </returns>
        public static TimeSeriesData LogicalCheck(
            TimeSeriesData ts, double threshold, LogicOperator check, long? endTime = null)
        {
            if (ts == null)
                throw new ArgumentNullException(nameof(ts), "The input data is empty");

            // We can only resample until the endTime if the time series is of type `step`. If the user specifies an
            // endTime for a non `step` time series we override it.
            // TODO: Add logging to notify the user that the endTime is being set to null
            if (!ts.IsStep)
                endTime = null;
            TimeSeriesData resampledTs = ts.EquallySpacedResampling(endTime: endTime);

            // store locally the x and y arrays
            long[] x = resampledTs.Time;
            double[] y = resampledTs.Data;

            // create an array to store the binary values
            double[] yRes = new double[y.Length];

            // run the logical check for each timestamp
            for (int i = 0; i < x.Length; i++)
            {
                switch (check)
                {
                    case LogicOperator.Eq:
                        yRes[i] = (y[i] == threshold) ? 1.0 : 0.0;
                        break;
                    case LogicOperator.Ne:
                        yRes[i] = (y[i] != threshold) ? 1.0 : 0.0;
                        break;
                    case LogicOperator.Gt:
                        yRes[i] = (y[i] > threshold) ? 1.0 : 0.0;
                        break;
                    case LogicOperator.Ge:
                        yRes[i] = (y[i] >= threshold) ? 1.0 : 0.0;
                        break;
                    case LogicOperator.Lt:
                        yRes[i] = (y[i] < threshold) ? 1.0 : 0.0;
                        break;
                    case LogicOperator.Le:
                        yRes[i] = (y[i] <= threshold) ? 1.0 : 0.0;
                        break;
                    default:
                        throw new ArgumentException($"Unknown operator {check}", nameof(check));
                }
            }

            return new TimeSeriesData(time: x, data: yRes, granularity: ts.Granularity, isStep: ts.IsStep);
        }
        
        /// <summary>
        /// Supported logical operations
        /// </summary>
        public enum LogicOperator
        {
            /// <summary>
            /// Equal
            /// </summary>
            Eq,
            /// <summary>
            /// Not equal
            /// </summary>
            Ne,
            /// <summary>
            /// Greater
            /// </summary>
            Gt,
            /// <summary>
            /// Greater or equal
            /// </summary>
            Ge,
            /// <summary>
            /// Less
            /// </summary>
            Lt,
            /// <summary>
            /// Less or equal
            /// </summary>
            Le
        }
    }
}