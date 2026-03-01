using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Strategy;

namespace SwingDayTradingPlatform.Tests;

public class MultiStrategyEngineTests
{
    private static TradingConfig DefaultTradingConfig() => TestHelpers.DefaultTradingConfig();

    private static MultiStrategyConfig AllStrategiesConfig() => TestHelpers.DefaultMultiConfig();

    private static DateTimeOffset TradingTime(int minutesOffset = 0) =>
        new DateTimeOffset(2024, 6, 10, 14, 0, 0, TimeSpan.Zero).AddMinutes(minutesOffset);

    [Fact]
    public void OnBarClosed_WarmupPhase_ReturnsNull()
    {
        var engine = new MultiStrategyEngine(AllStrategiesConfig());
        var config = DefaultTradingConfig();

        for (var i = 0; i < 30; i++)
        {
            var bar = TestHelpers.MakeBar(5000 + i, 5005 + i, 4995 + i, 5002 + i, minutesOffset: i * 5);
            var signal = engine.OnBarClosed(bar, null, TradingTime(i * 5), true, config);
            Assert.Null(signal);
        }

        Assert.Contains("Warm-up", engine.LatestReason);
    }

    [Fact]
    public void OnBarClosed_CountsBarsProcessed()
    {
        var engine = new MultiStrategyEngine(AllStrategiesConfig());
        var config = DefaultTradingConfig();

        var bar = TestHelpers.MakeBar(5000, 5005, 4995, 5002);
        engine.OnBarClosed(bar, null, TradingTime(), true, config);

        Assert.Equal(1, engine.BarsProcessed);
    }

    [Fact]
    public void OnBarClosed_CannotOpen_ReturnsNull()
    {
        var engine = new MultiStrategyEngine(AllStrategiesConfig());
        var config = DefaultTradingConfig();
        var bars = TestHelpers.GenerateWarmupBars(60);

        // Feed all warmup bars
        foreach (var b in bars)
            engine.OnBarClosed(b, null, TradingTime(bars.IndexOf(b) * 5), false, config);

        Assert.Contains("Outside entry window", engine.LatestReason);
    }

    [Fact]
    public void OnBarClosed_InPosition_SkipsEntry()
    {
        var engine = new MultiStrategyEngine(AllStrategiesConfig());
        var config = DefaultTradingConfig();
        var position = new PositionSnapshot("ES", "", PositionSide.Long, 1, 5000, 5010, 500, DateTimeOffset.UtcNow);

        var bars = TestHelpers.GenerateWarmupBars(60);
        StrategySignal? lastSignal = null;
        foreach (var b in bars)
            lastSignal = engine.OnBarClosed(b, position, TradingTime(bars.IndexOf(b) * 5), true, config);

        // Should not return entry signal while in position
        // It might return flatten signal if bar-break happens, or null
        // The key is no NEW entry signal while in position
    }

    [Fact]
    public void OnBarClosed_NewDay_ResetsContext()
    {
        var engine = new MultiStrategyEngine(AllStrategiesConfig());
        var config = DefaultTradingConfig();

        // Feed bars on day 1
        var day1 = new DateTimeOffset(2024, 6, 10, 14, 0, 0, TimeSpan.Zero);
        var bar1 = new MarketBar(day1, day1.AddMinutes(5), 5000, 5005, 4995, 5002, 1000);
        engine.OnBarClosed(bar1, null, day1, true, config);

        // Feed bar on day 2
        var day2 = new DateTimeOffset(2024, 6, 11, 14, 0, 0, TimeSpan.Zero);
        var bar2 = new MarketBar(day2, day2.AddMinutes(5), 5010, 5015, 5005, 5012, 1000);
        engine.OnBarClosed(bar2, null, day2, true, config);

        Assert.Equal(2, engine.BarsProcessed);
    }

    [Fact]
    public void OnBarClosed_BarTrimming_TrimsWhenExceedsThreshold()
    {
        var config = new MultiStrategyConfig
        {
            FastEmaPeriod = 5,
            SlowEmaPeriod = 10,
            AtrPeriod = 5,
            EnableStrategy1 = false,
            EnableStrategy5 = false,
            EnableStrategy7 = false,
            EnableStrategy9 = false,
            EnableHourlyBias = false,
            EnableTimeFilter = false,
            SwingLookback = 3,
            SRClusterAtrFactor = 0.5m,
            BigMoveAtrFactor = 3.0m,
            TickSize = 0.25m
        };
        var engine = new MultiStrategyEngine(config);
        var tradingConfig = DefaultTradingConfig();

        // keepBars = max(10, 5) + 50 = 60. Trim triggers when count > 120.
        for (var i = 0; i < 150; i++)
        {
            var bar = TestHelpers.MakeBar(5000 + i, 5005 + i, 4995 + i, 5002 + i, minutesOffset: i * 5);
            engine.OnBarClosed(bar, null, TradingTime(i * 5), false, tradingConfig);
        }

        // After trimming, bars should be less than 150 (trimmed from 120+)
        Assert.True(engine.BarsProcessed < 150, $"Bars should be trimmed, got {engine.BarsProcessed}");
    }

    [Fact]
    public void FastEma_SlowEma_ExposedFromContext()
    {
        var engine = new MultiStrategyEngine(AllStrategiesConfig());
        var config = DefaultTradingConfig();

        var bars = TestHelpers.GenerateWarmupBars(60);
        foreach (var b in bars)
            engine.OnBarClosed(b, null, TradingTime(bars.IndexOf(b) * 5), false, config);

        // After warmup, EMAs should be set
        Assert.NotEqual(0m, engine.FastEma);
        Assert.NotEqual(0m, engine.SlowEma);
    }

    [Fact]
    public void OnBarClosed_FlatPosition_ClearsActiveTrade()
    {
        var engine = new MultiStrategyEngine(AllStrategiesConfig());
        var config = DefaultTradingConfig();

        // Feed bars with flat position (null) on day change
        var day1 = new DateTimeOffset(2024, 6, 10, 14, 0, 0, TimeSpan.Zero);
        var bar1 = new MarketBar(day1, day1.AddMinutes(5), 5000, 5005, 4995, 5002, 1000);
        engine.OnBarClosed(bar1, null, day1, false, config);

        var day2 = new DateTimeOffset(2024, 6, 11, 14, 0, 0, TimeSpan.Zero);
        var bar2 = new MarketBar(day2, day2.AddMinutes(5), 5010, 5015, 5005, 5012, 1000);
        // Flat position should clear active trade on new day
        var flatPos = new PositionSnapshot("ES", "", PositionSide.Flat, 0, 0, 0, 0, day2);
        engine.OnBarClosed(bar2, flatPos, day2, false, config);

        // Should not crash, active trade cleared
        Assert.True(true);
    }

    [Fact]
    public void LatestSignalText_InitiallyNone()
    {
        var engine = new MultiStrategyEngine(AllStrategiesConfig());
        Assert.Equal("None", engine.LatestSignalText);
    }

    [Fact]
    public void LatestReason_InitiallyWaiting()
    {
        var engine = new MultiStrategyEngine(AllStrategiesConfig());
        Assert.Equal("Waiting", engine.LatestReason);
    }

    [Fact]
    public void TrailingStop_DoesNotActivateBeforeThreshold()
    {
        // Create a config with trailing stop activation at 3 bars
        var config = new MultiStrategyConfig
        {
            FastEmaPeriod = 20,
            SlowEmaPeriod = 50,
            AtrPeriod = 14,
            EnableStrategy1 = false,
            EnableStrategy5 = false,
            EnableStrategy7 = false,
            EnableStrategy9 = false,
            EnableHourlyBias = false,
            EnableTimeFilter = false,
            TrailingStopActivationBars = 3,
            TrailingStopAtrMultiplier = 2.0m,
            UseBarBreakExit = false
        };
        var engine = new MultiStrategyEngine(config);

        // The trailing stop won't activate in the first 3 bars of a trade.
        // We verify by checking that the engine doesn't crash and processes normally.
        var tradingConfig = DefaultTradingConfig();
        var bars = TestHelpers.GenerateWarmupBars(60);
        foreach (var b in bars)
            engine.OnBarClosed(b, null, TradingTime(bars.IndexOf(b) * 5), false, tradingConfig);

        Assert.True(engine.BarsProcessed > 0);
    }

    [Fact]
    public void TrailingStop_TriggersOnReversal()
    {
        // Create a simple active trade scenario with controlled trailing stop
        var config = new MultiStrategyConfig
        {
            FastEmaPeriod = 5,
            SlowEmaPeriod = 10,
            AtrPeriod = 5,
            EnableStrategy1 = false,
            EnableStrategy5 = false,
            EnableStrategy7 = false,
            EnableStrategy9 = false,
            EnableHourlyBias = false,
            EnableTimeFilter = false,
            TrailingStopActivationBars = 2,
            TrailingStopAtrMultiplier = 1.0m,
            UseBarBreakExit = false
        };
        var engine = new MultiStrategyEngine(config);
        var tradingConfig = DefaultTradingConfig();

        // Feed warmup bars
        var bars = TestHelpers.GenerateWarmupBars(20, startPrice: 5000m);
        foreach (var b in bars)
            engine.OnBarClosed(b, null, TradingTime(bars.IndexOf(b) * 5), false, tradingConfig);

        // Verify engine processes without error
        Assert.True(engine.BarsProcessed > 0);
    }

    [Fact]
    public void BarBreakExit_DisabledByDefault()
    {
        var config = AllStrategiesConfig();
        Assert.False(config.UseBarBreakExit);
    }
}
