using System;
using System.Collections.Generic;
using System.Linq;

namespace Cognite.DataProcessing
{
    /// <summary>
    /// Models a time series with `time` represented by a long array of milliseconds since epoch and `data` represented
    /// by a double array of data point values.
    /// </summary>
    public class TimeSeriesData
    {
        /// <summary>
        /// Array of timestamps in milliseconds since epoch.
        /// </summary>
        public long[] Time { get; private set; }
        /// <summary>
        /// Array of data point values.
        /// </summary>
        public double[] Data { get; private set; }
        /// <summary>
        /// Number of data points in the time series.
        /// </summary>
        public int Count { get; }
        /// <summary>
        /// Whether or not the time series is a step time series.
        /// </summary>
        public bool IsStep { get; }
        /// <summary>
        /// Granularity (in milliseconds) of the time series.
        /// </summary>
        public long Granularity { get; }
        /// <summary>
        /// Minimum timestamp in the time series.
        /// </summary>
        public long MinTime { get; }
        /// <summary>
        /// Maximum timestamp in the time series.
        /// </summary>
        public long MaxTime { get; }
        /// <summary>
        /// Whether or not the time series has gaps with missing data points.
        /// </summary>
        public bool HasGaps { get; }

        /// <summary>
        /// Models a time series with `time` represented by a long array of milliseconds since epoch and `data` represented
        /// by a double array of data point values.
        /// </summary>
        /// <param name="time">The time array of milliseconds since epoch</param>
        /// <param name="data">The data array of data point values</param>
        /// <param name="granularity">Defines the granularity (in milliseconds) of the time series</param>
        /// <param name="isStep">Defines if the time series is a step time series or not</param>
        public TimeSeriesData(long[] time, double[] data, long granularity, bool isStep = false)
        {
            if (data == null || data.Length == 0 || time == null || time.Length == 0)
                throw new ArgumentException("The input data is empty");
            if (time.Length != data.Length)
                throw new ArgumentException("Same number of timestamps and data points expected");

            (Time, Data, Granularity, IsStep) = (time, data, granularity, isStep);

            // Make sure the data is sorted by timestamp
            Array.Sort(Time, Data);
            MinTime = Time[0];
            MaxTime = Time[Time.Length - 1];

            RemoveDuplicates();
            Array.Sort(Time, Data);
            Count = Data.Length;

            // Evaluate if there are gaps in the data
            HasGaps = (MaxTime - MinTime) / Granularity + 1 != Count;
        }

        /// <summary>
        /// Removes duplicate timestamps from the time series.
        /// </summary>
        private void RemoveDuplicates()
        {
            List<long> timeUnique = new List<long>();
            List<double> dataUnique = new List<double>();

            for (int i = 0; i < Time.Length; i++)
            {
                if (!timeUnique.Contains(Time[i]))
                {
                    timeUnique.Add(Time[i]);
                    dataUnique.Add(Data[i]);
                }
            }
            Time = timeUnique.ToArray();
            Data = dataUnique.ToArray();
        }

        /// <summary>
        /// Resamples the time series into an equally spaced time series.
        /// </summary>
        /// <param name="startTime">Defines the start time of the resampled array.</param>
        /// <param name="endTime">Defines the end time of the resampled array.</param>
        /// <param name="granularity">Defines the granularity (in milliseconds) of the resampled array.</param>
        /// <returns>
        /// TimeSeriesData with resampled data.
        /// </returns>
        public TimeSeriesData EquallySpacedResampling(
            long? startTime = null, long? endTime = null, long? granularity = null)
        {
            // if no startTime is specified, we use the minimum time
            startTime = startTime ?? MinTime;
            // if no endTime is specified, we use the maximum time
            endTime = endTime ?? MaxTime;
            // if no granularity is specified, we use the original granularity
            granularity = granularity ?? Granularity;

            long[] timeResampled;
            double[] dataResampled;

            // it is only possible to extrapolate beyond the last data point for step time series
            if (!IsStep && (endTime.Value > MaxTime))
                throw new ArgumentException(
                    "The given endTime would result in extrapolation which is only allowed for step time series.",
                    nameof(endTime));

            // It is not possible to extrapolate beyond the first data point for any time series
            if (startTime.Value < MinTime)
                throw new ArgumentException("The given startTime is smaller than the minimum time.", nameof(startTime));

            // if there are no gaps in the time array and there is no need to extrapolate, we can skip the resampling
            if ((!HasGaps) && (endTime.Value == MaxTime) && (startTime.Value == MinTime))
            {
                timeResampled = Time;
                dataResampled = Data;
            }
            else
            {
                timeResampled = TimeSeriesUtils.GenerateLinearSpacedLongArray(
                    start: startTime.Value, end: endTime.Value, step: granularity.Value
                );
                dataResampled = new double[timeResampled.Length];

                // counter for original array index
                int i = 0;
                // counter for resampled array index
                int j = 0;
                do
                {
                    // the new point is positioned after the current point in the original array
                    if (timeResampled[j] > Time[i])
                    {
                        dataResampled[j] = Resample(i, timeResampled[j]);
                        // we cannot increase i past the end of the original array
                        i = Math.Min(i + 1, Time.Length - 1);
                    }
                    // the new point is positioned exactly on top of the current point in the original array
                    else if (timeResampled[j] == Time[i])
                    {
                        dataResampled[j] = Data[i];
                        // we cannot increase i past the end of the original array
                        i = Math.Min(i + 1, Time.Length - 1);
                    }
                    // the new point is positioned before the current point in the original array
                    else if (timeResampled[j] < Time[i])
                    {
                        dataResampled[j] = Resample(i - 1, timeResampled[j]);
                    }
                    j++;

                } while (j < timeResampled.Length);
            }
            return new TimeSeriesData(timeResampled, dataResampled, Granularity, IsStep);
        }

        /// <summary>
        /// Resamples the data at the given time.
        /// </summary>
        /// <param name="idx"> The index of the data point in the original array.</param>
        /// <param name="xp"> The time of the new point.</param>
        /// <returns>
        /// The resampled value.
        /// </returns>
        private double Resample(int idx, long xp)
        {
            if (IsStep)
                return Data[idx];

            return TimeSeriesUtils.ExecuteLinearInterpolation(
                x0: Time[idx], y0: Data[idx],
                x1: Time[idx + 1], y1: Data[idx + 1],
                xp: xp
            );
        }

        /// <summary>
        /// Slices the time series according to the provided boundaries.
        /// </summary>
        public TimeSeriesData Slice(long startTime, long endTime)
        {
            int i0 = Array.IndexOf(Time, startTime);
            int i1 = Array.IndexOf(Time, endTime);

            // check if the provided times do exist
            if ((i0 == -1) || (i1 == -1))
                throw new ArgumentOutOfRangeException("startTime or endTime", "The specified start time or end time was not found in the time series.");

            i1++; // include the endTime
            return new TimeSeriesData(
                Time.Skip(i0).Take(i1 - i0).ToArray(), //Time[i0..i1],
                Data.Skip(i0).Take(i1 - i0).ToArray(), //Data[i0..i1],
                Granularity, IsStep);
        }

        /// <summary>
        /// Returns the average value of the data points in the time series.
        /// </summary>
        public double GetAverage()
        {
            // resample the time series (if necessary)
            TimeSeriesData resampled = EquallySpacedResampling();

            return resampled.Data.Sum() / resampled.Data.Length;
        }
    }

    /// <summary>
    /// Class containing utility routines for handling time series data
    /// </summary>
    public static class TimeSeriesUtils
    {
        /// <summary>
        /// Performs linear interpolation to predict an intermediary datapoint.
        /// </summary>
        internal static double ExecuteLinearInterpolation(long x0, double y0, long x1, double y1, long xp)
        {
            double x0d = Convert.ToDouble(x0);
            double x1d = Convert.ToDouble(x1);
            double xpd = Convert.ToDouble(xp);

            return y0 + (y1 - y0) / (x1d - x0d) * (xpd - x0d);
        }

        /// <summary>
        /// Generates a linearly spaced array with longs.
        /// </summary>
        internal static long[] GenerateLinearSpacedLongArray(long start, long end, long step)
        {
            // calculate the number of elements
            long n = (end - start) / step + 1;

            long[] result = new long[n];
            for (int i = 0; i < n; i++)
                result[i] = start + step * i;

            return result;
        }

        /// <summary>
        /// Aligns two time series according to their timestamps.
        /// </summary>
        public static Tuple<TimeSeriesData, TimeSeriesData> ExecuteTimeSeriesAlignment(
            TimeSeriesData ts1, TimeSeriesData ts2)
        {
            if (ts1 == null)
                throw new ArgumentNullException(nameof(ts1), "The input data is empty");
            if (ts2 == null)
                throw new ArgumentNullException(nameof(ts2), "The input data is empty");

            // check if the given time series contain enough data points. If only one data point is available on both
            // time series, the alignment is not possible.
            if ((ts1.Count == 1) && (ts2.Count == 1))
            {
                throw new ArgumentException("Not enough data to perform alignment.");
            }

            // find the smaller granularity (ideally they should be equal)
            var granularity = Math.Min(ts1.Granularity, ts2.Granularity);

            // find the maximum min time
            var minTime = Math.Max(ts1.MinTime, ts2.MinTime);

            long maxTime;
            // if both time series are step, we take the largest MaxTime and extrapolate to it
            if (ts1.IsStep && ts2.IsStep)
                maxTime = Math.Max(ts1.MaxTime, ts2.MaxTime);
            else
            {
                // find the minimum max time (we dis-consider step time series as they can be extrapolated)
                var maxTimeList = new List<long>();

                if (!ts1.IsStep)
                    maxTimeList.Add(ts1.MaxTime);

                if (!ts2.IsStep)
                    maxTimeList.Add(ts2.MaxTime);

                maxTime = maxTimeList.Min();
            }

            if (maxTime < minTime)
                throw new ArgumentException("It's not possible to align the given TimeSeries.");

            // time series 1
            var tsResampled1 = ts1.EquallySpacedResampling(minTime, maxTime, granularity);

            // time series 2
            var tsResampled2 = ts2.EquallySpacedResampling(minTime, maxTime, granularity);

            return new Tuple<TimeSeriesData, TimeSeriesData>(tsResampled1, tsResampled2);
        }

        /// <summary>
        /// Constrains a value to not exceed a maximum and minimum value.
        /// </summary>
        /// <param name="value">The value to constrain.</param>
        /// <param name="min">The minimum limit.</param>
        /// <param name="max">The maximum limit.</param>
        /// <returns>
        /// Value within the specified limits.
        /// </returns>
        public static double Constrain(double value, double min = 1.0e-4, double max = 1.0e6)
        {
            if (value > max)
                return max;

            if (value < min)
                return min;

            return value;
        }
    }
}