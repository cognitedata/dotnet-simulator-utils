using System;
using System.Collections.Generic;
using System.Linq;

using MathNet.Numerics;
using MathNet.Numerics.Statistics;

namespace Cognite.DataProcessing
{
    /// <summary>
    /// Class containing routines for time series detection
    /// </summary>
    public static class Detectors
    {
        /// <summary>
        /// The ED-PELT algorithm for change point detection.
        /// 
        /// For given array of `double` values, detects locations of change points that splits original series of values
        /// into "statistically homogeneous" segments. Such points correspond to moments when statistical properties of
        /// the distribution are changing.
        ///
        /// This method supports nonparametric distributions and has O(N*log(N)) algorithmic complexity.
        /// </summary>
        /// <param name="data">An array of double values</param>
        /// <param name="minDistance">Minimum distance between change points</param>
        /// <returns>
        /// Returns an `int[]` array with 1-based indexes of change points. Change points correspond to the end of the
        /// detected segments. For example, change points for { 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2 }
        /// are { 6, 12 }.
        /// 
        /// Original implementation from (c) 2019 Andrey Akinshin
        /// Licensed under The MIT License https://opensource.org/licenses/MIT
        /// </returns>
        public static int[] EdPeltChangePointDetector(double[] data, int minDistance = 1)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "The input data is empty");

            // We will use `n` as the number of elements in the `data` array
            int n = data.Length;

            // Checking corner cases
            if (n <= 2)
                return Array.Empty<int>();
            if (minDistance < 1 || minDistance > n)
                throw new ArgumentOutOfRangeException(
                    nameof(minDistance), $"{minDistance} should be in range from 1 to data.Length");

            // The penalty which we add to the final cost for each additional change point
            // Here we use the Modified Bayesian Information Criterion
            double penalty = 3 * Math.Log(n);

            // `k` is the number of quantiles that we use to approximate an integral during the segment cost evaluation
            // We use `k=Ceiling(4*log(n))` as suggested in the Section 4.3 "Choice of K in ED-PELT" in [Haynes2017]
            // `k` can't be greater than `n`, so we should always use the `Min` function here (important for n <= 8)
            int k = Math.Min(n, (int)Math.Ceiling(4 * Math.Log(n)));

            // We should precalculate sums for empirical CDF, it will allow fast evaluating of the segment cost
            int[,] partialSums = GetPartialSums(data, k);

            // Since we use the same values of `partialSums`, `k`, `n` all the time,
            // we introduce a shortcut `Cost(tau1, tau2)` for segment cost evaluation.
            // Hereinafter, we use `tau` to name variables that are change point candidates.
            double Cost(int tau1, int tau2) => GetSegmentCost(partialSums, tau1, tau2, k, n);

            // We will use dynamic programming to find the best solution; `bestCost` is the cost array.
            // `bestCost[i]` is the cost for subarray `data[0..i-1]`.
            // It's a 1-based array (`data[0]`..`data[n-1]` correspond to `bestCost[1]`..`bestCost[n]`)
            double[] bestCost = new double[n + 1];
            bestCost[0] = -penalty;
            for (int currentTau = minDistance; currentTau < 2 * minDistance; currentTau++)
                bestCost[currentTau] = Cost(0, currentTau);

            // `previousChangePointIndex` is an array of references to previous change points. If the current segment
            // ends at the position `i`, the previous segment ends at the position `previousChangePointIndex[i]`. It's a
            // 1-based array (`data[0]`..`data[n-1]` correspond to the `previousChangePointIndex[1]`..
            // `previousChangePointIndex[n]`)
            int[] previousChangePointIndex = new int[n + 1];

            // We use PELT (Pruned Exact Linear Time) approach which means that instead of enumerating all possible
            // previous tau values, we use a whitelist of "good" tau values that can be used in the optimal solution. If
            // we are 100% sure that some of the tau values will not help us to form the optimal solution, such values
            // should be removed. See [Killick2012] for details.
            var previousTaus = new List<int>(n + 1) { 0, minDistance };
            var costForPreviousTau = new List<double>(n + 1);

            // Following the dynamic programming approach, we enumerate all tau positions. For each `currentTau`, we
            // pretend that it's the end of the last segment and trying to find the end of the previous segment.
            for (int currentTau = 2 * minDistance; currentTau < n + 1; currentTau++)
            {
                // For each previous tau, we should calculate the cost of taking this tau as the end of the previous
                // segment. This cost equals the cost for the `previousTau` plus cost of the new segment (from
                // `previousTau` to `currentTau`) plus penalty for the new change point.
                costForPreviousTau.Clear();
                foreach (int previousTau in previousTaus)
                    costForPreviousTau.Add(bestCost[previousTau] + Cost(previousTau, currentTau) + penalty);

                // Now we should choose the tau that provides the minimum possible cost.
                int bestPreviousTauIndex = WhichMin(costForPreviousTau);
                bestCost[currentTau] = costForPreviousTau[bestPreviousTauIndex];
                previousChangePointIndex[currentTau] = previousTaus[bestPreviousTauIndex];

                // Prune phase: we remove "useless" tau values that will not help to achieve minimum cost in the future
                double currentBestCost = bestCost[currentTau];
                int newPreviousTausSize = 0;
                for (int i = 0; i < previousTaus.Count; i++)
                    if (costForPreviousTau[i] < currentBestCost + penalty)
                        previousTaus[newPreviousTausSize++] = previousTaus[i];
                previousTaus.RemoveRange(newPreviousTausSize, previousTaus.Count - newPreviousTausSize);

                // We add a new tau value that is located on the `minDistance` distance from the next `currentTau` value
                previousTaus.Add(currentTau - (minDistance - 1));
            }

            // Here we collect the result list of change point indexes `changePointIndexes` using
            // `previousChangePointIndex`
            var changePointIndexes = new List<int>();
            int currentIndex = previousChangePointIndex[n]; // The index of the end of the last segment is `n`
            while (currentIndex != 0)
            {
                changePointIndexes.Add(currentIndex);
                currentIndex = previousChangePointIndex[currentIndex];
            }

            int[] result = changePointIndexes.ToArray();

            // Sort the changePointIndexes
            Array.Sort(result);

            return result;
        }

        /// <summary>
        /// Partial sums for empirical CDF (formula (2.1) from Section 2.1 "Model" in [Haynes2017])
        /// <code>
        /// partialSums[i, tau] = (count(data[j] &lt; t) * 2 + count(data[j] == t) * 1) for j=0..tau-1 where t is the
        /// i-th quantile value (see Section 3.1 "Discrete approximation" in [Haynes2017] for details)
        /// </code>
        /// <remarks>
        /// <list type="bullet">
        /// <item>
        /// We use doubled sum values in order to use <c>int[,]</c> instead of <c>double[,]</c> (it provides noticeable
        /// performance boost). Thus, multipliers for <c>count(data[j] &lt; t)</c> and <c>count(data[j] == t)</c> are
        /// 2 and 1 instead of 1 and 0.5 from the [Haynes2017].
        /// </item>
        /// <item>
        /// Note that these quantiles are not uniformly distributed: tails of the <c>data</c> distribution contain more
        /// quantile values than the center of the distribution
        /// </item>
        /// </list>
        /// </remarks>
        /// </summary>
        private static int[,] GetPartialSums(double[] data, int k)
        {
            int n = data.Length;
            int[,] partialSums = new int[k, n + 1];
            double[] sortedData = data.OrderBy(it => it).ToArray();

            for (int i = 0; i < k; i++)
            {
                double z = -1 + (2 * i + 1.0) / k; // Values from (-1+1/k) to (1-1/k) with step = 2/k
                double p = 1.0 / (1 + Math.Pow(2 * n - 1, -z)); // Values from 0.0 to 1.0
                double t = sortedData[(int)Math.Truncate((n - 1) * p)]; // Quantile value, formula (2.1) in [Haynes2017]

                for (int tau = 1; tau <= n; tau++)
                {
                    partialSums[i, tau] = partialSums[i, tau - 1];
                    if (data[tau - 1] < t)
                        partialSums[i, tau] += 2; // We use doubled value (2) instead of original 1.0
                    if (data[tau - 1] == t)
                        partialSums[i, tau] += 1; // We use doubled value (1) instead of original 0.5
                }
            }
            return partialSums;
        }

        /// <summary>
        /// Calculates the cost of the (tau1; tau2] segment.
        /// </summary>
        private static double GetSegmentCost(int[,] partialSums, int tau1, int tau2, int k, int n)
        {
            double sum = 0;
            for (int i = 0; i < k; i++)
            {
                // actualSum is (count(data[j] < t) * 2 + count(data[j] == t) * 1) for j=tau1..tau2-1
                int actualSum = partialSums[i, tau2] - partialSums[i, tau1];

                // We skip these two cases (correspond to fit = 0 or fit = 1) because of invalid Math.Log values
                if (actualSum != 0 && actualSum != (tau2 - tau1) * 2)
                {
                    // Empirical CDF $\hat{F}_i(t)$ (Section 2.1 "Model" in [Haynes2017])
                    double fit = actualSum * 0.5 / (tau2 - tau1);
                    // Segment cost $\mathcal{L}_{np}$ (Section 2.2 "Nonparametric maximum likelihood" in [Haynes2017])
                    double lnp = (tau2 - tau1) * (fit * Math.Log(fit) + (1 - fit) * Math.Log(1 - fit));
                    sum += lnp;
                }
            }
            double c = -Math.Log(2 * n - 1); // Constant from Lemma 3.1 in [Haynes2017]
            return 2.0 * c / k * sum; // See Section 3.1 "Discrete approximation" in [Haynes2017]
        }

        /// <summary>
        /// Returns the index of the minimum element.
        /// In case if there are several minimum elements in the given list, the index of the first one will be
        /// returned.
        /// </summary>
        private static int WhichMin(IList<double> values)
        {
            if (values.Count == 0)
                throw new InvalidOperationException("Array should contain elements");

            double minValue = values[0];
            int minIndex = 0;
            for (int i = 1; i < values.Count; i++)
                if (values[i] < minValue)
                {
                    minValue = values[i];
                    minIndex = i;
                }

            return minIndex;
        }

        /// <summary>
        /// Steady State Detection (based on Change Point Detection).
        /// 
        /// Evaluates the given time series with respect to steady behavior.First the time series is split into
        /// "statistically homogeneous" segments using the ED Pelt change point detection algorithm. Then each segment
        /// is tested with regards to a normalized standard deviation and the slope of the line of best fit to determine
        /// if the segment can be considered a steady or transient region.
        /// </summary>
        /// <param name="ts">A TimeSeriesData object</param>
        /// <param name="minDistance">Minimum segment distance. Specifies the minimum distance for each segment that
        /// will be considered in the Change Point Detection algorithm.</param>
        /// <param name="varThreshold">Variance threshold. Specifies the variance threshold.If the normalized variance
        /// calculated for a given segment is greater than the threshold, the segment will be labeled as transient
        /// (value = 0).</param>
        /// <param name="slopeThreshold">Slope threshold. Specifies the slope threshold. If the slope of a line fitted
        /// to the data of a given segment is greater than 10 to the power of the threshold value, the segment will be
        /// labeled as transient (value = 0).</param>
        /// <returns>
        /// TimeSeriesData object with the steady state condition (0: transient region, 1: steady region) for all timestamps.
        /// </returns>
        public static TimeSeriesData SteadyStateDetector(
            TimeSeriesData ts, int minDistance = 15, double varThreshold = 5.0, double slopeThreshold = -3.0)
        {
            if (ts == null)
                throw new ArgumentNullException(nameof(ts), "The input data is empty");

            // resamples the given time series so that it contains equally spaced elements
            TimeSeriesData resampledTs = ts.EquallySpacedResampling();

            // store locally the x and y arrays
            long[] x = resampledTs.Time;
            double[] y = resampledTs.Data;

            // the maximum allowable distance is half the number of data points so we override the minDistance value if
            // the current value is not valid
            int maxDistance = x.Length / 2;
            minDistance = (minDistance > maxDistance) ? maxDistance : minDistance;

            // instantiate the array that will store the results
            double[] ssMap = new double[x.Length];

            // compute the change points
            int[] changePoints = EdPeltChangePointDetector(data: y, minDistance: minDistance);

            // Add zero and the last index to the change points list
            List<int> changePointsList = changePoints.ToList();
            changePointsList.Add(0);
            changePointsList.Add(y.Length);
            changePoints = changePointsList.ToArray();
            // Sort the changePoints
            Array.Sort(changePoints);

            // compute the mean value of the input vector
            double avg = y.Mean();

            // constrains the mean of the data into predefined limits
            // this will prevent generating infinite values on the var calculation below
            double divisor = TimeSeriesUtils.Constrain(value: avg, min: 1.0e-4, max: 1.0e6);

            for (int i = 1; i < changePoints.Length; i++)
            {
                int i0 = changePoints[i - 1];
                int i1 = changePoints[i];
                long[] xi = x.Skip(i0).Take(i1 - i0).ToArray(); //x[i0..i1]
                double[] xid = Array.ConvertAll<long, double>(xi, item => item);
                double[] yi = y.Skip(i0).Take(i1 - i0).ToArray(); //y[i0..i1]

                double std = yi.StandardDeviation() / (i1 - i0);
                double stdNormalised = 1.0e5 * std / divisor;

                // We consider a region as transient unless it passed the subsequent logical tests
                double ssRegion = 0.0;

                // First check if the variance criteria is met
                if (Math.Abs(stdNormalised) < varThreshold)
                {
                    // Only fit a line if the first criteria is met
                    (double _, double slope) = Fit.Line(xid, yi);

                    if (Math.Abs(slope) < Math.Pow(10.0, slopeThreshold))
                    {
                        ssRegion = 1.0;
                    }
                }

                // Assigns the Steady State map flag to all timestamps of the current region
                for (int j = i0; j < i1; j++)
                {
                    ssMap[j] = ssRegion;
                }
            }

            return new TimeSeriesData(time: x, data: ssMap, ts.Granularity, isStep: false);
        }
    }
}