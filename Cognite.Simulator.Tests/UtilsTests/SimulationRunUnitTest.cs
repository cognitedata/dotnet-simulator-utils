using CogniteSdk.Alpha;
using Cognite.Simulator.Utils;
using Xunit;
using System;
using Cognite.Extractor.Common;

namespace Cognite.Simulator.Tests.UtilsTests;

public class SimulationRunUnitTest
{

    [Fact]
    public void TestRunReadinessTimeout()
    {
        {
            var input = new SimulationRunItem(new SimulationRun()
            {
                CreatedTime = DateTime.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds(),
                Status = SimulationRunStatus.ready,
                Id = 1,
            });

            var ex = Assert.Throws<ConnectorException>(() => input.ValidateReadinessForExecution(1));
            Assert.Equal("Simulation has timed out because it is older than 1 second(s)", ex.Message);
        }

        {
            var input = new SimulationRunItem(new SimulationRun()
            {
                CreatedTime = 0,
                Status = SimulationRunStatus.ready,
                Id = 1,
            });

            var ex = Assert.Throws<ConnectorException>(() => input.ValidateReadinessForExecution());
            Assert.Equal("Simulation has timed out because it is older than 3600 second(s)", ex.Message);
        }
    }

    [Fact]
    public void TestRunReadinessAlreadyRunning()
    {
        var input = new SimulationRunItem(new SimulationRun()
        {
            CreatedTime = DateTime.UtcNow.ToUnixTimeMilliseconds(),
            Status = SimulationRunStatus.running,
            Id = 1,
        });

        var ex = Assert.Throws<ConnectorException>(() => input.ValidateReadinessForExecution());
        Assert.Equal("Simulation entered unrecoverable state and failed", ex.Message);
    }
}
