using System;

using Cognite.DataProcessing;

using Xunit;

namespace Cognite.Simulator.Tests.DataProcessingTests;

public class DataSamplingTest
{
    private readonly TimeSeriesData _sensorLogic;
    private readonly TimeSeriesData _ssResults;
    private readonly TimeSeriesData _unionResults;
    private readonly TimeSeriesData _simpleSensor;
    private readonly TimeSeriesData _oneDataPoint;
    private readonly TimeSeriesData _oneDataPointStep;
    private readonly TimeSeriesData _twoDataPoints;
    private readonly TimeSeriesData _twoDataPointsStep;
    private readonly TimeSeriesData _sensorData;
    private readonly TimeSeriesData _logicalCheck1;
    private readonly DataSampling.LogicOperator _check1;
    private readonly double _threshold1;

    public DataSamplingTest()
    {
        var testData = new TestValues();
        _sensorLogic = new TimeSeriesData(
            time: testData.TimeLogic, data: testData.DataLogic, granularity: 60000, isStep: true);
        _ssResults = new TimeSeriesData(
            time: testData.SsTimeExpected, data: testData.SsDataExpected, granularity: 60000, isStep: false);
        _unionResults = new TimeSeriesData(
            time: testData.UnionTimeExpected, data: testData.UnionDataExpected, granularity: 60000, isStep: false);
        _simpleSensor = new TimeSeriesData(
            time: new long[] { 2, 4, 5, 8, 8, 10, 11, 12, 14, 15, 17, 18 },
            data: new double[] { 1, 1, 2, 3, 5, 6, 5, 5, 3.5, 3.2, 3.1, 1 },
            granularity: 1,
            isStep: false);
        _oneDataPoint = new TimeSeriesData(
            time: new long[] { 1 }, data: new double[] { 1 }, granularity: 1, isStep: false);
        _oneDataPointStep = new TimeSeriesData(
            time: new long[] { 1 }, data: new double[] { 1 }, granularity: 1, isStep: true);
        _twoDataPoints = new TimeSeriesData(
            time: new long[] { 1, 10 }, data: new double[] { 1, 0 }, granularity: 1, isStep: false);
        _twoDataPointsStep = new TimeSeriesData(
            time: new long[] { 1, 10 }, data: new double[] { 1, 0 }, granularity: 1, isStep: true);
        _sensorData = new TimeSeriesData(
            time: testData.TimeSsd, data: testData.DataSsd, granularity: 60000, isStep: false);
        _logicalCheck1 = new TimeSeriesData(
            time: testData.LogicalCheckTime, data: testData.LogicalCheckData1, granularity: 60000, isStep: false);
        _check1 = testData.Check1;
        _threshold1 = testData.Threshold1;
    }

    [Fact]
    public void TestUnionLogicTimeSeries()
    {
        // Call Method
        TimeSeriesData unionTs = DataSampling.UnionLogicTimeSeries(ts1: _ssResults, ts2: _sensorLogic);

        for (int i = 0; i < unionTs.Time.Length; i++)
        {
            Assert.Equal(_unionResults.Time[i], unionTs.Time[i]);
            Assert.Equal(_unionResults.Data[i], unionTs.Data[i]);
        }
    }

    [Fact]
    public void TestUnionLogicTimeSeries1()
    {
        // No union is possible with the given TimeSeries
        // 1) IsStep: false { x }
        // 2) IsStep: false      { x  x  x  x  ... }
        Assert.Throws<ArgumentException>(() => DataSampling.UnionLogicTimeSeries(
            ts1: _simpleSensor, ts2: _oneDataPoint));
    }

    [Fact]
    public void TestUnionLogicTimeSeries2()
    {
        // as the time series with one datapoint is configured as step, we can extrapolate and a feasible union is found
        // 1) IsStep: true  { x  o  o  o  o  o  ... }
        // 2) IsStep: false       { x  x  x  x  ... }

        // Call Method
        TimeSeriesData unionTs = DataSampling.UnionLogicTimeSeries(ts1: _simpleSensor, ts2: _oneDataPointStep);

        // Expected
        TimeSeriesData expectedTs = new(
            time: new long[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 },
            data: new double[] { 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 },
            granularity: 1,
            isStep: false);

        for (int i = 0; i < unionTs.Time.Length; i++)
        {
            Assert.Equal(expectedTs.Time[i], unionTs.Time[i]);
            Assert.Equal(expectedTs.Data[i], unionTs.Data[i]);
        }
    }

    [Fact]
    public void TestUnionLogicTimeSeries3()
    {
        // Call Method
        TimeSeriesData unionTs = DataSampling.UnionLogicTimeSeries(ts1: _simpleSensor, ts2: _twoDataPoints);

        // Expected
        TimeSeriesData expectedTs = new(
            time: new long[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            data: new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            granularity: 1,
            isStep: false);

        for (int i = 0; i < unionTs.Time.Length; i++)
        {
            Assert.Equal(expectedTs.Time[i], unionTs.Time[i]);
            Assert.Equal(expectedTs.Data[i], unionTs.Data[i]);
        }
    }

    [Fact]
    public void TestUnionLogicTimeSeries4()
    {
        // Call Method
        TimeSeriesData unionTs = DataSampling.UnionLogicTimeSeries(ts1: _simpleSensor, ts2: _twoDataPointsStep);

        // Expected
        TimeSeriesData expectedTs = new(
            time: new long[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 },
            data: new double[] { 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            granularity: 1,
            isStep: false);

        for (int i = 0; i < unionTs.Time.Length; i++)
        {
            Assert.Equal(expectedTs.Time[i], unionTs.Time[i]);
            Assert.Equal(expectedTs.Data[i], unionTs.Data[i]);
        }
    }

    [Fact]
    public void TestFindSamplingTime()
    {
        // MinWindow = 60 min
        long minWindow = 3600000;

        // Call Method
        long? sampleTime = DataSampling.FindSamplingTime(logicMap: _unionResults, minWindow: minWindow);

        // Expected result
        long expectedSampleTime = 1631296740000;

        if (sampleTime != null) Assert.Equal(expectedSampleTime, sampleTime.Value);
    }

    [Fact]
    public void TestLogicalCheck()
    {
        // Call Method
        TimeSeriesData logicResult = DataSampling.LogicalCheck(
            ts: _sensorData, threshold: _threshold1, check: _check1);

        for (int i = 0; i < logicResult.Time.Length; i++)
        {
            Assert.Equal(_logicalCheck1.Time[i], logicResult.Time[i]);
            Assert.Equal(_logicalCheck1.Data[i], logicResult.Data[i]);
        }
    }

    [Fact]
    public void TestLogicalCheckExtrapolate()
    {
        // input data
        TimeSeriesData input = new TimeSeriesData(
            time: new long[] { 1, 2, 3 }, data: new double[] { 5, 0, 3 }, granularity: 1, isStep: true);

        TimeSeriesData output = new TimeSeriesData(
            time: new long[] { 1, 2, 3, 4, 5 }, data: new double[] { 1, 0, 1, 1, 1 }, granularity: 1, isStep: true);

        // Call Method
        TimeSeriesData logicResult = DataSampling.LogicalCheck(
            ts: input, threshold: 2.5, check: DataSampling.LogicOperator.Ge, endTime: 5);

        for (int i = 0; i < logicResult.Time.Length; i++)
        {
            Assert.Equal(output.Time[i], logicResult.Time[i]);
            Assert.Equal(output.Data[i], logicResult.Data[i]);
        }
    }

    [Fact]
    public void TestLogicalCheckTryExtrapolate()
    {
        // input data
        TimeSeriesData input = new TimeSeriesData(
            time: new long[] { 1, 2, 3 }, data: new double[] { 5, 0, 3 }, granularity: 1, isStep: false);

        TimeSeriesData output = new TimeSeriesData(
            time: new long[] { 1, 2, 3 }, data: new double[] { 1, 0, 1 }, granularity: 1, isStep: false);

        // Call Method
        TimeSeriesData logicResult = DataSampling.LogicalCheck(
            ts: input, threshold: 2.5, check: DataSampling.LogicOperator.Ge, endTime: 5);

        for (int i = 0; i < logicResult.Time.Length; i++)
        {
            Assert.Equal(output.Time[i], logicResult.Time[i]);
            Assert.Equal(output.Data[i], logicResult.Data[i]);
        }
    }
}