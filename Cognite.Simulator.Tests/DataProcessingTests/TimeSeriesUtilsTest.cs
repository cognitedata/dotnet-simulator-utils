using System;

using Cognite.DataProcessing;

using Xunit;

namespace Cognite.Simulator.Tests.DataProcessingTests;

public class TimeSeriesUtilsTest
{
    // shared test inputs
    readonly TimeSeriesData _tsTest1;
    readonly TimeSeriesData _tsTestStep1;
    readonly TimeSeriesData _tsTest2;
    readonly TimeSeriesData _tsTestStep2;

    public TimeSeriesUtilsTest()
    {
        // Defined inputs
        long[] timeArray1 = new long[] { 1, 2, 5, 6, 8 };
        double[] dataArray1 = new double[] { 1, 2, 5, 6, 8 };
        _tsTest1 = new TimeSeriesData(timeArray1, dataArray1, 1);
        _tsTestStep1 = new TimeSeriesData(timeArray1, dataArray1, 1, true);

        long[] timeArray2 = new long[] { 2, 4, 5, 8, 8, 10, 11, 12, 14, 15, 17, 18 };
        double[] dataArray2 = new double[] { 1.0, 1.0, 2.0, 3.0, 5.0, 6.0, 5.0, 5.0, 3.5, 3.2, 3.1, 1.0 };
        _tsTest2 = new TimeSeriesData(timeArray2, dataArray2, 1);
        _tsTestStep2 = new TimeSeriesData(timeArray2, dataArray2, 1, true);
    }

    [Fact]
    public void TestMinMax()
    {
        // Expected results
        const long minTime = 1;
        const long maxTime = 8;

        Assert.Equal(minTime, _tsTest1.MinTime);
        Assert.Equal(maxTime, _tsTest1.MaxTime);
    }

    [Fact]
    public void TestTimeSeriesInterpolation1()
    {
        // Call Method
        TimeSeriesData tsResampled = _tsTest1.EquallySpacedResampling();

        // Expected results
        long[] timeArrayResampled = new long[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        double[] dataArrayResampled = new double[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        for (int i = 0; i < timeArrayResampled.Length; i++)
        {
            Assert.Equal(timeArrayResampled[i], tsResampled.Time[i]);
            Assert.Equal(dataArrayResampled[i], tsResampled.Data[i]);
        }
    }

    [Fact]
    public void TestTimeSeriesInterpolation2()
    {
        // Call Method
        TimeSeriesData tsResampled = _tsTest2.EquallySpacedResampling();

        // Expected results
        long[] timeArrayResampled = new long[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
        double[] dataArrayResampled = new double[] { 1, 1, 1, 2, 2.33333, 2.66666, 3, 4.5, 6, 5, 5, 4.25, 3.5, 3.2, 3.15, 3.1, 1 };

        for (int i = 0; i < timeArrayResampled.Length; i++)
        {
            Assert.Equal(timeArrayResampled[i], tsResampled.Time[i]);
            Assert.Equal(dataArrayResampled[i], tsResampled.Data[i], 4);
        }
    }

    [Fact]
    public void TestTimeSeriesStepInterpolation1()
    {
        // Call Method
        TimeSeriesData tsResampled = _tsTestStep1.EquallySpacedResampling();

        // Expected results
        long[] timeArrayResampled = new long[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        double[] dataArrayResampled = new double[] { 1, 2, 2, 2, 5, 6, 6, 8 };

        for (int i = 0; i < timeArrayResampled.Length; i++)
        {
            Assert.Equal(timeArrayResampled[i], tsResampled.Time[i]);
            Assert.Equal(dataArrayResampled[i], tsResampled.Data[i], 4);
        }
    }

    [Fact]
    public void TestTimeSeriesStepInterpolation2()
    {
        // Call Method
        TimeSeriesData tsResampled = _tsTestStep2.EquallySpacedResampling();

        // Expected results
        long[] timeArrayResampled = new long[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
        double[] dataArrayResampled = new double[] { 1, 1, 1, 2, 2, 2, 3, 3, 6, 5, 5, 5, 3.5, 3.2, 3.2, 3.1, 1 };

        for (int i = 0; i < timeArrayResampled.Length; i++)
        {
            Assert.Equal(timeArrayResampled[i], tsResampled.Time[i]);
            Assert.Equal(dataArrayResampled[i], tsResampled.Data[i], 4);
        }
    }

    [Fact]
    public void TestTimeSeriesStepInterpolationExtrapolate()
    {
        // Call Method
        TimeSeriesData tsResampled = _tsTestStep1.EquallySpacedResampling(endTime: 10);

        // Expected results
        long[] timeArrayResampled = new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        double[] dataArrayResampled = new double[] { 1, 2, 2, 2, 5, 6, 6, 8, 8, 8 };

        for (int i = 0; i < timeArrayResampled.Length; i++)
        {
            Assert.Equal(timeArrayResampled[i], tsResampled.Time[i]);
            Assert.Equal(dataArrayResampled[i], tsResampled.Data[i]);
        }
    }

    [Fact]
    public void TestTimeSeriesInterpolationExtrapolate()
    {
        // Try to interpolate a non step time series beyond the last value
        Assert.Throws<ArgumentException>(() => _tsTest1.EquallySpacedResampling(endTime: 10));
    }

    [Fact]
    public void TestSlice()
    {
        // Call Method
        TimeSeriesData tsSliced = _tsTest1.Slice(2, 6);

        // Expected results
        long[] timeArraySliced = new long[] { 2, 5, 6 };
        double[] dataArraySliced = new double[] { 2, 5, 6 };

        for (int i = 0; i < timeArraySliced.Length; i++)
        {
            Assert.Equal(timeArraySliced[i], tsSliced.Time[i]);
            Assert.Equal(dataArraySliced[i], tsSliced.Data[i]);
        }

    }

    [Fact]
    public void TestGetAverage()
    {
        // Call Method
        double calculatedAverage = _tsTest1.GetAverage();

        // Expected results
        double expectedAverage = 4.5;

        Assert.Equal(expectedAverage, calculatedAverage);

    }


    [Fact]
    public void TestTimeSeriesAlignment()
    {
        // Defined inputs
        long[] timeArray1 = new long[] { 1, 2, 5, 6, 8 };
        double[] dataArray1 = new double[] { 1, 2, 5, 6, 8 };
        TimeSeriesData ts1 = new(timeArray1, dataArray1, 1);

        long[] timeArray2 = new long[] { 2, 5, 6, 7, 8, 9 };
        double[] dataArray2 = new double[] { 4, 10, 12, 14, 16, 18 };
        TimeSeriesData ts2 = new(timeArray2, dataArray2, 1);

        // Call Method
        Tuple<TimeSeriesData, TimeSeriesData> alignedTs = TimeSeriesUtils.ExecuteTimeSeriesAlignment(ts1, ts2);

        // Expected results
        TimeSeriesData alignedTs1 = new(
            new long[] { 2, 3, 4, 5, 6, 7, 8 }, new double[] { 2, 3, 4, 5, 6, 7, 8 }, 1
        );
        TimeSeriesData alignedTs2 = new(
            new long[] { 2, 3, 4, 5, 6, 7, 8 }, new double[] { 4, 6, 8, 10, 12, 14, 16 }, 1
        );

        for (int i = 0; i < alignedTs1.Time.Length; i++)
        {
            Assert.Equal(alignedTs1.Time[i], alignedTs.Item1.Time[i]);
            Assert.Equal(alignedTs1.Data[i], alignedTs.Item1.Data[i]);
            Assert.Equal(alignedTs2.Time[i], alignedTs.Item2.Time[i]);
            Assert.Equal(alignedTs2.Data[i], alignedTs.Item2.Data[i]);
        }
    }

    [Fact]
    public void TestDuplicatesRemoval()
    {
        // Defined inputs
        long[] timeArrayWithDuplicates = new long[] { 1, 2, 2, 5, 6, 6, 8 };
        double[] dataArrayWithDuplicates = new double[] { 1, 2, 2, 5, 6, 7, 8 };
        TimeSeriesData tsDuplicates = new(timeArrayWithDuplicates, dataArrayWithDuplicates, 1);

        // Expected results
        long[] timeArrayDistinct = new long[] { 1, 2, 5, 6, 8 };
        double[] dataArrayDistinct = new double[] { 1, 2, 5, 6, 8 };

        for (int i = 0; i < timeArrayDistinct.Length; i++)
        {
            Assert.Equal(timeArrayDistinct[i], tsDuplicates.Time[i]);
            Assert.Equal(dataArrayDistinct[i], tsDuplicates.Data[i]);
        }
    }

    [Fact]
    public void TestConstraint()
    {
        // Defined inputs
        double min = 1.0e-4;
        double max = 1.0e6;
        double valueMin = 0.0;
        double valueMax = 1.0e8;
        double valueMiddle = 100.0;

        // Call method
        double resultMin = TimeSeriesUtils.Constrain(value: valueMin, min: min, max: max);
        double resultMax = TimeSeriesUtils.Constrain(value: valueMax, min: min, max: max);
        double resultMiddle = TimeSeriesUtils.Constrain(value: valueMiddle, min: min, max: max);

        // Asserts
        Assert.Equal(min, resultMin);
        Assert.Equal(max, resultMax);
        Assert.Equal(valueMiddle, resultMiddle);
    }
}
