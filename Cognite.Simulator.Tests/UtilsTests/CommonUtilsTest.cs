using Cognite.Extractor.Common;
using Cognite.Simulator.Utils;
using CogniteSdk;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public class CommonUtilsTest
    {
        [Theory]
        [InlineData("60s", 1, 0, 0)]
        [InlineData("12m", 12, 0, 0)]
        [InlineData("240h", 0, 240, 0)]
        [InlineData("2d", 0, 0, 2)]
        [InlineData("1w", 0, 0, 7)]
        public void TestParseStringToTimeSpan(string repeat, int min, int hours, int days)
        {
            var expected = new TimeSpan(days, hours, min, 0);
            var result = SimulationUtils.ConfigurationTimeStringToTimeSpan(repeat);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(1631296740000, 1631293140000, 1631296740000, true, true)]
        [InlineData(1631274480000, 1631263860000, 1631267460000, true, true)]
        [InlineData(1631278800000, 1631275200000, 1631278800000, true, false)]
        [InlineData(1631274480000, 1631263860000, 1631267460000, false, true)]
        [InlineData(1631271600000, 1631268000000, 1631271600000, false, false)]
        public async Task TestRunSteadyStateAndLogicalCheck(
            long validationEnd,
            long expectedStart,
            long expectedEnd,
            bool runLogicCheck,
            bool runSsdCheck)
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var dataPoints = cdf.DataPoints;

            var config = NewRoutineConfig();
            config.LogicalCheck.First().Enabled = runLogicCheck;
            config.SteadyStateDetection.First().Enabled = runSsdCheck;

            var result = await SimulationUtils.RunSteadyStateAndLogicalCheck(
                dataPoints,
                config,
                CogniteTime.FromUnixTimeMilliseconds(validationEnd),
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.True(result.Min.HasValue);
            Assert.Equal(expectedStart, result.Min.Value);
            Assert.True(result.Max.HasValue);
            Assert.Equal(expectedEnd, result.Max.Value);
        }

        private static SimulatorRoutineRevisionConfiguration NewRoutineConfig()
        {
            // Assumes a time series in CDF with the id SimConnect-IntegrationTests-OnOffValues and
            // one with id SimConnect-IntegrationTests-SsdSensorData.
            // The time stamps and values for these time series match the ones used in the DataProcessing
            // library tests
            return new()
            {
                DataSampling = new SimulatorRoutineRevisionDataSampling
                {
                    Granularity = 1,
                    SamplingWindow = 60,
                    ValidationWindow = 1200
                },
                LogicalCheck = new List<SimulatorRoutineRevisionLogicalCheck>
                {
                    new SimulatorRoutineRevisionLogicalCheck
                    {
                        Enabled = true,
                        TimeseriesExternalId = "SimConnect-IntegrationTests-OnOffValues",
                        Aggregate = "stepInterpolation",
                        Operator = "eq",
                        Value = 1.0
                    }
                },
                SteadyStateDetection = new List<SimulatorRoutineRevisionSteadyStateDetection>
                {
                    new SimulatorRoutineRevisionSteadyStateDetection
                    {
                        Enabled = true,
                        TimeseriesExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                        Aggregate = "average",
                        MinSectionSize = 60,
                        VarThreshold = 1.0,
                        SlopeThreshold = -3.0
                    }
                }
            };
        }
    }
}
