using Xunit;
using Cognite.Simulator.Utils;

namespace Cognite.Simulator.Tests.UtilsTests;

public class DetectorsTest
{
    readonly double[] _data;
    readonly int _minDistance;
    private readonly TimeSeriesData _sensorSsd;
    private readonly TimeSeriesData _ssResults;

    public DetectorsTest()
    {
        // shared test inputs
        _data = new double[] { 1, 1, 1, 1, 10, 10, 10, 10, 10, 1, 1, 1, 1 };
        _minDistance = 1;
        
        var testData = new TestValues();
        _sensorSsd = new TimeSeriesData(
            time: testData.TimeSsd, data: testData.DataSsd, granularity: 60000, isStep: false);
        _ssResults = new TimeSeriesData(
            time: testData.SsTimeExpected, data: testData.SsDataExpected, granularity: 60000, isStep: false);
    }

    [Fact]
    public void TestEdPeltChangePointDetector()
    {
        // Call Method
        int[] changePoints = Detectors.EdPeltChangePointDetector(data: _data, minDistance: _minDistance);

        // Expected results
        int[] expected = new int[] { 4, 9 };

        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], changePoints[i]);
    }
    
    [Fact]
    public void TestSsd()
    {
        // Call Method
        TimeSeriesData ssMap = Detectors.SteadyStateDetector(
            ts: _sensorSsd, minDistance: 60, varThreshold: 1.0, slopeThreshold: -1.0);

        for (int i = 0; i < _ssResults.Time.Length; i++)
        {
            Assert.Equal(_ssResults.Time[i], ssMap.Time[i]);
            Assert.Equal(_ssResults.Data[i], ssMap.Data[i]);
        }
    }
}