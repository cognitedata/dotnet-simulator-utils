using Cognite.Simulator.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Cognite.Simulator.Tests.ExtensionsTests
{
    public class DataPointsTest
    {
        [Theory]
        [InlineData(1000, 2000, 1500)]
        [InlineData(1648026240000, 1648029840000, 1648028040000)]
        [InlineData(1648026240000, 1648029840001, 1648028040000)]
        public void TestSamplingRange(long start, long end, long mid)
        {
            SamplingRange range = new CogniteSdk.TimeRange { Min = start, Max = end };
            Assert.Equal(start, range.Start);
            Assert.Equal(end, range.End);
            Assert.Equal(mid, range.CalcTime);
        }

        [Theory]
        [InlineData(120, "120m")]
        [InlineData(240, "4h")]
        [InlineData(290, "4h")]
        [InlineData(6000000, "100000h")]
        [InlineData(7000000, "4861d")]
        public void TestMinutesToGranularity(int minutes, string granularity)
        {
            string granularityString = DataPointsExtensions.MinutesToGranularity(minutes);
            Assert.Equal(granularity, granularityString);
        }

        [Fact]
        public void TestMinutesToGranularityException()
        {
            Assert.Throws<ArgumentException>(() => DataPointsExtensions.MinutesToGranularity(144001440));
        }

    }
}
