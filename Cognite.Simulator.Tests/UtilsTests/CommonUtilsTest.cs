using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cognite.Extractor.Common;
using Cognite.Simulator.Utils;

using CogniteSdk;
using CogniteSdk.Alpha;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public class CommonUtilsTest
    {

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

            await SeedData.GetOrCreateTestTimeseries(cdf);

            var config = NewRoutineConfig();
            config.LogicalCheck.First().Enabled = runLogicCheck;
            config.SteadyStateDetection.First().Enabled = runSsdCheck;

            var result = await SimulationUtils.RunSteadyStateAndLogicalCheck(
                dataPoints,
                config,
                CogniteTime.FromUnixTimeMilliseconds(validationEnd),
                System.Threading.CancellationToken.None);

            Assert.True(result.Min.HasValue);
            Assert.Equal(expectedStart, result.Min.Value);
            Assert.True(result.Max.HasValue);
            Assert.Equal(expectedEnd, result.Max.Value);
        }

        private static SimulatorRoutineRevisionConfiguration NewRoutineConfig()
        {
            // Assumes a time series in CDF with the id utils-tests-OnOffValues and
            // one with id utils-tests-SsdSensorData.
            // The time stamps and values for these time series match the ones used in the DataProcessing
            // library tests
            return new()
            {
                DataSampling = new SimulatorRoutineRevisionDataSampling
                {
                    Enabled = true,
                    Granularity = 1,
                    SamplingWindow = 60,
                    ValidationWindow = 1200
                },
                LogicalCheck = new List<SimulatorRoutineRevisionLogicalCheck>
                {
                    new SimulatorRoutineRevisionLogicalCheck
                    {
                        Enabled = true,
                        TimeseriesExternalId = "utils-tests-OnOffValues",
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
                        TimeseriesExternalId = "utils-tests-SsdSensorData",
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
