using System;
using Cognite.DataProcessing;
using Xunit;

namespace Cognite.Simulator.Tests.DataProcessingTests;

public class OptimizersTest
{
    // shared test inputs
    private readonly Func<double, double> _testFunction1;
    private readonly Func<double, double> _testFunction2;
    private readonly Func<double, double> _testFunction3;
    private readonly Func<double, double> _testFunction4;

    public OptimizersTest()
    {
        _testFunction1 = x => x * x * x - x;
        _testFunction2 = x => Math.Pow((x - 1.5), 2) - 0.8;
        _testFunction3 = x => x * x * x - 2 * x + 2;
        _testFunction4 = x => 3 * x * x * x + 4 * x * x + 5 * x + 6;
    }

    [Fact]
    public void TestMinimizeScalarBounded()
    {
        var calcRoot = Optimizers.MinimizeScalarBounded(_testFunction1, -1.5, -0.5);
        Assert.Equal(-1.5, calcRoot.X, 4);

        // minimum at 1.0
        calcRoot = Optimizers.MinimizeScalarBounded(_testFunction2, 0, 1);
        Assert.Equal(1, calcRoot.X, 4);

        // minimum at 1.5
        calcRoot = Optimizers.MinimizeScalarBounded(_testFunction2, 1, 5);
        Assert.Equal(1.5, calcRoot.X, 4);
        
        // minimum at 0.8164965876370768
        calcRoot = Optimizers.MinimizeScalarBounded(_testFunction3, -5, 5);
        Assert.Equal(0.8164965876370768, calcRoot.X, 4);
        calcRoot = Optimizers.MinimizeScalarBounded(_testFunction3, -2, 4);
        Assert.Equal(0.8164965876370768, calcRoot.X, 4);
        
        // minimum at -2
        calcRoot = Optimizers.MinimizeScalarBounded(_testFunction4, -2, -1);
        Assert.Equal(-2, calcRoot.X, 4);

        // exception when the bracketing interval is not valid
        Assert.Throws<ArgumentException>(() => Optimizers.MinimizeScalarBounded(_testFunction2, 5, 1));
    }
}