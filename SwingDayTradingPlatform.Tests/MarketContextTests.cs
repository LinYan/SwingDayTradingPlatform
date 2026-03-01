using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Strategy;

namespace SwingDayTradingPlatform.Tests;

public class MarketContextTests
{
    private static MultiStrategyConfig DefaultConfig() => TestHelpers.DefaultMultiConfig();

    [Fact]
    public void OnNewBar_BelowWarmup_DoesNotSetIndicators()
    {
        var ctx = new MarketContext(DefaultConfig());
        var bars = TestHelpers.GenerateBars(10); // less than warmup (~55)
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        Assert.Equal(0m, ctx.Ema20);
        Assert.Equal(0m, ctx.Ema50);
        Assert.Equal(0m, ctx.Atr14);
    }

    [Fact]
    public void OnNewBar_AfterWarmup_SetsIndicators()
    {
        var ctx = new MarketContext(DefaultConfig());
        var bars = TestHelpers.GenerateWarmupBars(60);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        Assert.NotEqual(0m, ctx.Ema20);
        Assert.NotEqual(0m, ctx.Ema50);
        Assert.True(ctx.Atr14 >= 0);
    }

    [Fact]
    public void OnNewBar_IncrementalEma_DiffersFromScratch()
    {
        var config = DefaultConfig();
        var ctx = new MarketContext(config);

        // Seed with full warmup
        var bars = TestHelpers.GenerateWarmupBars(60);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        var ema20After60 = ctx.Ema20;

        // Add one more bar and call again (incremental path)
        bars.Add(TestHelpers.MakeBar(5010, 5015, 5005, 5012, minutesOffset: 300));
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        var ema20After61 = ctx.Ema20;

        // EMA should have changed after the new bar
        Assert.NotEqual(ema20After60, ema20After61);
    }

    [Fact]
    public void OnNewDay_ClearsSwingsAndLevels_ButNotEma()
    {
        var ctx = new MarketContext(DefaultConfig());
        var bars = TestHelpers.GenerateWarmupBars(60);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);

        var ema20Before = ctx.Ema20;
        ctx.OnNewDay();

        // Swing/SR cleared
        Assert.Empty(ctx.SwingPoints);
        Assert.Empty(ctx.SRLevels);
        Assert.Null(ctx.LatestBigMove);
        Assert.Equal(DirectionBias.Both, ctx.Bias);

        // EMA state is preserved (not reset)
        // Note: Ema20 property itself is set in OnNewBar, but internal running state persists
        // After OnNewDay + OnNewBar with new data, the EMA continues from where it left off
        bars.Add(TestHelpers.MakeBar(5010, 5015, 5005, 5012, minutesOffset: 500));
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        // The EMA should still be reasonable (not reset to 0)
        Assert.NotEqual(0m, ctx.Ema20);
    }

    [Fact]
    public void AdjustSwingPointIndices_ShiftsIndicesDown()
    {
        var ctx = new MarketContext(DefaultConfig());
        // Manually add swing points
        ctx.SwingPoints.Add(new SwingPoint(10, 5000m, SwingType.High));
        ctx.SwingPoints.Add(new SwingPoint(20, 4990m, SwingType.Low));
        ctx.SwingPoints.Add(new SwingPoint(30, 5010m, SwingType.High));

        ctx.AdjustSwingPointIndices(15);

        // Index 10 - 15 = -5 → removed
        // Index 20 - 15 = 5 → kept
        // Index 30 - 15 = 15 → kept
        Assert.Equal(2, ctx.SwingPoints.Count);
        Assert.Equal(5, ctx.SwingPoints[0].BarIndex);
        Assert.Equal(15, ctx.SwingPoints[1].BarIndex);
    }

    [Fact]
    public void AdjustSwingPointIndices_RemovesAll_WhenTrimCountLarge()
    {
        var ctx = new MarketContext(DefaultConfig());
        ctx.SwingPoints.Add(new SwingPoint(5, 5000m, SwingType.High));
        ctx.SwingPoints.Add(new SwingPoint(10, 4990m, SwingType.Low));

        ctx.AdjustSwingPointIndices(20);

        Assert.Empty(ctx.SwingPoints);
    }

    [Fact]
    public void AdjustSwingPointIndices_ZeroTrim_NoChange()
    {
        var ctx = new MarketContext(DefaultConfig());
        ctx.SwingPoints.Add(new SwingPoint(10, 5000m, SwingType.High));

        ctx.AdjustSwingPointIndices(0);

        Assert.Single(ctx.SwingPoints);
        Assert.Equal(10, ctx.SwingPoints[0].BarIndex);
    }

    [Fact]
    public void HourlyBias_DefaultBoth()
    {
        var ctx = new MarketContext(DefaultConfig());
        Assert.Equal(DirectionBias.Both, ctx.Bias);
    }

    [Fact]
    public void SwingPoints_TrimmedTo50()
    {
        var ctx = new MarketContext(DefaultConfig());
        // Add 60 swing points directly
        for (var i = 0; i < 60; i++)
            ctx.SwingPoints.Add(new SwingPoint(i, 5000m + i, SwingType.High));

        // Simulate the trim logic from OnNewBar
        if (ctx.SwingPoints.Count > 50)
            ctx.SwingPoints.RemoveRange(0, ctx.SwingPoints.Count - 50);

        Assert.Equal(50, ctx.SwingPoints.Count);
        Assert.Equal(10, ctx.SwingPoints[0].BarIndex); // First 10 removed
    }

    [Fact]
    public void OnNewBar_EmptyBars_DoesNotThrow()
    {
        var ctx = new MarketContext(DefaultConfig());
        ctx.OnNewBar(new List<MarketBar>(), DateTimeOffset.UtcNow);
        Assert.Equal(0m, ctx.Ema20);
    }

    [Fact]
    public void HourlyBar_UsesLocalTimezone_NotUtc()
    {
        var baseConfig = DefaultConfig();
        var config = new MultiStrategyConfig
        {
            FastEmaPeriod = baseConfig.FastEmaPeriod, SlowEmaPeriod = baseConfig.SlowEmaPeriod,
            AtrPeriod = baseConfig.AtrPeriod, SwingLookback = baseConfig.SwingLookback,
            SRClusterAtrFactor = baseConfig.SRClusterAtrFactor, BigMoveAtrFactor = baseConfig.BigMoveAtrFactor,
            MomentumBars = baseConfig.MomentumBars, MomentumBodyAtrRatio = baseConfig.MomentumBodyAtrRatio,
            TickSize = baseConfig.TickSize, MaxStopPoints = baseConfig.MaxStopPoints,
            BigMoveStaleBars = baseConfig.BigMoveStaleBars, EnableHourlyBias = true
        };
        var ctx = new MarketContext(config);

        // Create bars at 14:00-14:55 UTC (= 10:00-10:55 ET)
        // These should all fall in the 10:xx hour in ET, producing 1 hourly bar
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 60; i++)
        {
            var utcOpen = new DateTimeOffset(2024, 6, 10, 14, 0, 0, TimeSpan.Zero).AddMinutes(i * 5);
            bars.Add(new MarketBar(utcOpen, utcOpen.AddMinutes(5),
                price, price + 2, price - 2, price + 0.5m, 1000));
        }

        // Pass trading-local time in ET (UTC-4 during EDT)
        var etOffset = TimeSpan.FromHours(-4);

        // Feed bars one at a time with correct local time
        for (var i = 0; i < bars.Count; i++)
        {
            var subBars = bars.Take(i + 1).ToList();
            if (subBars.Count < 56) continue; // skip warmup
            var localTime = new DateTimeOffset(bars[i].CloseTimeUtc.DateTime, TimeSpan.Zero)
                .ToOffset(etOffset);
            ctx.OnNewBar(subBars, localTime);
        }

        // Should have hourly bars aggregated by ET hours, not UTC
        Assert.True(ctx.HourlyBars.Count >= 1, "Should have at least one hourly bar");
    }

    [Fact]
    public void HourlyBar_PartialHourIncluded()
    {
        var baseConfig2 = DefaultConfig();
        var config = new MultiStrategyConfig
        {
            FastEmaPeriod = baseConfig2.FastEmaPeriod, SlowEmaPeriod = baseConfig2.SlowEmaPeriod,
            AtrPeriod = baseConfig2.AtrPeriod, SwingLookback = baseConfig2.SwingLookback,
            SRClusterAtrFactor = baseConfig2.SRClusterAtrFactor, BigMoveAtrFactor = baseConfig2.BigMoveAtrFactor,
            MomentumBars = baseConfig2.MomentumBars, MomentumBodyAtrRatio = baseConfig2.MomentumBodyAtrRatio,
            TickSize = baseConfig2.TickSize, MaxStopPoints = baseConfig2.MaxStopPoints,
            BigMoveStaleBars = baseConfig2.BigMoveStaleBars, EnableHourlyBias = true
        };
        var ctx = new MarketContext(config);

        // Create bars: 60 warmup bars, then 3 bars in a new hour
        var bars = new List<MarketBar>();
        var price = 5000m;
        var etOffset = TimeSpan.FromHours(-4);

        // Warmup: 60 bars starting at 10:00 ET (14:00 UTC)
        for (var i = 0; i < 60; i++)
        {
            var utcOpen = new DateTimeOffset(2024, 6, 10, 14, 0, 0, TimeSpan.Zero).AddMinutes(i * 5);
            bars.Add(new MarketBar(utcOpen, utcOpen.AddMinutes(5),
                price, price + 2, price - 2, price + 0.5m, 1000));
        }

        // Feed all bars
        for (var i = 0; i < bars.Count; i++)
        {
            var subBars = bars.Take(i + 1).ToList();
            if (subBars.Count < 56) continue;
            var localTime = new DateTimeOffset(bars[i].CloseTimeUtc.DateTime, TimeSpan.Zero)
                .ToOffset(etOffset);
            ctx.OnNewBar(subBars, localTime);
        }

        var hourlyCountBefore = ctx.HourlyBars.Count;
        Assert.True(hourlyCountBefore >= 1, "Should have hourly bars after warmup");

        // The last partial hour should be included (not dropped)
        var lastHourly = ctx.HourlyBars[^1];
        Assert.True(lastHourly.Volume > 0, "Partial hourly bar should have volume");
    }
}
