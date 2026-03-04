using SwingDayTradingPlatform.AlertService;

namespace SwingDayTradingPlatform.AlertService.Tests;

public class EmaCalculatorTests
{
    [Fact]
    public void FirstValue_Returns_InputAsEma()
    {
        var ema = new EmaCalculator(20);
        var result = ema.Add(100m);

        Assert.Equal(100m, result);
        Assert.Equal(100m, ema.CurrentEma);
        Assert.Equal(1, ema.Count);
    }

    [Fact]
    public void Period_IsStored()
    {
        var ema = new EmaCalculator(20);
        Assert.Equal(20, ema.Period);
    }

    [Fact]
    public void EmaConvergesToConstant_WhenSameValueRepeated()
    {
        var ema = new EmaCalculator(5);
        for (var i = 0; i < 50; i++)
            ema.Add(100m);

        Assert.Equal(100m, ema.CurrentEma);
    }

    [Fact]
    public void Ema_FollowsTrend_Upward()
    {
        var ema = new EmaCalculator(3);
        // multiplier = 2/(3+1) = 0.5
        var v1 = ema.Add(10m); // EMA = 10
        var v2 = ema.Add(20m); // EMA = (20-10)*0.5 + 10 = 15
        var v3 = ema.Add(30m); // EMA = (30-15)*0.5 + 15 = 22.5

        Assert.Equal(10m, v1);
        Assert.Equal(15m, v2);
        Assert.Equal(22.5m, v3);
    }

    [Fact]
    public void Ema_Period20_KnownValues()
    {
        var ema = new EmaCalculator(20);
        // multiplier = 2/21 ≈ 0.095238
        var v1 = ema.Add(100m); // 100
        Assert.Equal(100m, v1);

        var v2 = ema.Add(110m); // (110-100)*2/21 + 100 = 100.952...
        Assert.True(v2 > 100m && v2 < 110m);
    }

    [Fact]
    public void Count_IncrementsWithEachAdd()
    {
        var ema = new EmaCalculator(10);
        Assert.Equal(0, ema.Count);

        ema.Add(1m);
        Assert.Equal(1, ema.Count);

        ema.Add(2m);
        Assert.Equal(2, ema.Count);

        ema.Add(3m);
        Assert.Equal(3, ema.Count);
    }
}
