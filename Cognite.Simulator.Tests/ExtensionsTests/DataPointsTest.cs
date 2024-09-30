using Cognite.Extractor.Common;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
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
            var samplingConfiguration = new SamplingConfiguration(start: start, end: end, samplingPosition: SamplingPosition.Midpoint);
            Assert.Equal(start, samplingConfiguration.Start);
            Assert.Equal(end, samplingConfiguration.End);
            Assert.Equal(mid, samplingConfiguration.SimulationTime);
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

        [Fact]
        public async Task TestGetSample()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddSingleton<DefaultConfig<AutomationConfig>>();
            //services.AddSingleton<ScopedRemoteApiSink<AutomationConfig>>();
            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var dataPoints = cdf.DataPoints;

            // Tests assume the given time series exists in the test project and
            // that it contains data points in this time range
            var samplingConfiguration = new SamplingConfiguration(start: 1631294940000, end: 1631296740000);
            
            // Test max aggregation. A single data point should be returned
            var (timestamps, values) = await dataPoints.GetSample(
                "SimConnect-IntegrationTests-OnOffValues",
                Extensions.DataPointAggregate.Max,
                800,
                samplingConfiguration,
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(timestamps);
            Assert.NotNull(values);
            Assert.Single(values);
            Assert.Equal(1, values[0]);

            var range2 = new SamplingConfiguration(start: 0, end: 0);

            // No data points in the range. Throw exception
            _ = Assert.ThrowsAsync<DataPointSampleNotFoundException>(
                async () => await dataPoints.GetSample(
                  "SimConnect-IntegrationTests-OnOffValues",
                  Extensions.DataPointAggregate.Max,
                  1,
                  range2,
                  System.Threading.CancellationToken.None)
                .ConfigureAwait(false));

            // No data points in the range, and before the range. Since aggregation
            // is step interpolation should search forward in time for any data points
            var (timestamps2, values2) = await dataPoints.GetSample(
                "SimConnect-IntegrationTests-OnOffValues",
                Extensions.DataPointAggregate.StepInterpolation,
                1,
                range2,
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(timestamps2);
            Assert.NotNull(values2);
            Assert.Single(values2);
            Assert.Equal(1, values2[0]);
        }
        
        [Fact]
        public async Task TestGetSampleNoDataSampling() {
            var services = new ServiceCollection();
            services.AddSingleton<DefaultConfig<AutomationConfig>>();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var dataPoints = cdf.DataPoints;

            // DataSampling disabled range
            var currentTimeEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var disabledDataSampling = new SamplingConfiguration( end: currentTimeEpoch );
            var (timestamp, value) = await dataPoints.GetLatestValue(
                "SimConnect-IntegrationTests-OnOffValues",
                disabledDataSampling,
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            
            Assert.Equal(1, value);
        }
    }
}
