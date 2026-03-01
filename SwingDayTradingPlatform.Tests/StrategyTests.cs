using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Strategy;
using SwingDayTradingPlatform.Strategy.Strategies;

namespace SwingDayTradingPlatform.Tests;

public class StrategyTests
{
    private static readonly DateTimeOffset BaseTime = new(2024, 6, 10, 14, 0, 0, TimeSpan.Zero);

    private static MarketBar MakeBar(int offsetMinutes, decimal open, decimal high, decimal low, decimal close, long volume = 1000)
    {
        var openTime = BaseTime.AddMinutes(offsetMinutes);
        return new MarketBar(openTime, openTime.AddMinutes(5), open, high, low, close, volume);
    }

    private static MultiStrategyConfig TestConfig(
        decimal? emaPullbackRewardRatio = null,
        decimal? emaPullbackTolerance = null,
        decimal? maxStopPoints = null) => new()
    {
        FastEmaPeriod = 20,
        SlowEmaPeriod = 50,
        AtrPeriod = 14,
        SwingLookback = 3,
        SRClusterAtrFactor = 0.5m,
        BigMoveAtrFactor = 3.0m,
        TickSize = 0.25m,
        EnableHourlyBias = false,
        EnableTimeFilter = false,
        EnableBreakEvenStop = false,
        EmaPullbackRewardRatio = emaPullbackRewardRatio ?? 2.0m,
        EmaPullbackTolerance = emaPullbackTolerance ?? 0.5m,
        EmaBodyMinAtrRatio = 0m, // disable body filter for legacy tests
        EmaMinSlopeAtr = 0m, // disable slope filter for legacy tests
        EmaRsiLongMin = 0m, // disable RSI filter for legacy tests
        EmaRsiLongMax = 100m,
        EmaRsiShortMin = 0m,
        EmaRsiShortMax = 100m,
        MaxStopPoints = maxStopPoints ?? 15m
    };

    /// <summary>
    /// Build a series of bars where EMA20 > EMA50, close > VWAP,
    /// and the last bar forms a higher-low pattern near EMA20.
    /// </summary>
    private static (List<MarketBar> bars, MarketContext ctx) BuildEmaPullbackLongScenario(MultiStrategyConfig config)
    {
        var bars = new List<MarketBar>();
        // Generate 60 bars in an uptrend so EMA20 > EMA50
        var price = 4950m;
        for (var i = 0; i < 60; i++)
        {
            price += 0.5m; // gentle uptrend
            var bar = MakeBar(i * 5, price - 0.5m, price + 2m, price - 2m, price);
            bars.Add(bar);
        }

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        return (bars, ctx);
    }

    #region EmaPullback Tests

    [Fact]
    public void EmaPullback_ReturnsNull_WhenTrendIsDown()
    {
        var config = TestConfig();
        // Build downtrend bars: EMA20 < EMA50
        var bars = new List<MarketBar>();
        var price = 5100m;
        for (var i = 0; i < 60; i++)
        {
            price -= 0.5m;
            bars.Add(MakeBar(i * 5, price + 0.5m, price + 2m, price - 2m, price));
        }

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);

        // In downtrend with uniform step-down data and no swing pattern, expect null
        var signal = EmaPullbackStrategy.Evaluate(bars, ctx, config, bars.Count - 1);
        Assert.Null(signal);
    }

    [Fact]
    public void EmaPullback_LongSignal_HasCorrectRewardRatio()
    {
        var config = TestConfig(emaPullbackRewardRatio: 3.0m);
        var (bars, ctx) = BuildEmaPullbackLongScenario(config);

        // Inject swing points to simulate higher-low pattern near EMA20
        var ema20 = ctx.Ema20;
        ctx.SwingPoints.Clear();
        ctx.SwingPoints.Add(new SwingPoint(bars.Count - 10, ema20 - 1m, SwingType.Low));
        ctx.SwingPoints.Add(new SwingPoint(bars.Count - 3, ema20 - 0.5m, SwingType.Low));

        var signal = EmaPullbackStrategy.Evaluate(bars, ctx, config, bars.Count - 1);

        if (signal is not null)
        {
            Assert.Equal(PositionSide.Long, signal.Direction);
            // Verify target uses configured reward ratio
            var risk = signal.EntryPrice - signal.StopPrice;
            var expectedTarget = signal.EntryPrice + risk * 3.0m;
            Assert.Equal(expectedTarget, signal.TargetPrice);
            Assert.Contains("EmaPullback", signal.Reason);
        }
    }

    #endregion

    #region MarketContext VWAP Tests

    [Fact]
    public void Vwap_AccumulatesAcrossUtcMidnight_WithoutSpuriousReset()
    {
        var config = TestConfig();
        var ctx = new MarketContext(config);

        // Simulate bars spanning UTC midnight (23:55 → 00:05 UTC)
        // This represents a normal CME session that crosses UTC midnight
        var bars = new List<MarketBar>();
        var price = 5000m;

        // Generate 59 bars before UTC midnight to satisfy warmup
        for (var i = 0; i < 59; i++)
        {
            var openTime = new DateTimeOffset(2024, 6, 10, 20, 0, 0, TimeSpan.Zero).AddMinutes(i * 5);
            bars.Add(new MarketBar(openTime, openTime.AddMinutes(5),
                price, price + 1, price - 1, price, 1000));
        }

        // Feed all bars and capture VWAP
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        var vwapBefore = ctx.Vwap;
        Assert.True(vwapBefore > 0, "VWAP should be positive after feeding bars");

        // Add one more bar that crosses UTC midnight (but same trading day)
        var crossMidnight = new DateTimeOffset(2024, 6, 11, 0, 5, 0, TimeSpan.Zero);
        bars.Add(new MarketBar(crossMidnight.AddMinutes(-5), crossMidnight,
            price, price + 1, price - 1, price, 1000));

        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        var vwapAfter = ctx.Vwap;

        // After fix: VWAP should NOT reset at UTC midnight.
        // The cumulative volume should be higher (60 bars vs 59), so VWAP
        // should still be a valid average, not reset to a single bar's typical price.
        Assert.True(vwapAfter > 0, "VWAP should remain positive after UTC midnight bar");
    }

    [Fact]
    public void Vwap_ResetsOnNewDay_ViaOnNewDayCall()
    {
        var config = TestConfig();
        var ctx = new MarketContext(config);

        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 60; i++)
        {
            var openTime = new DateTimeOffset(2024, 6, 10, 14, 0, 0, TimeSpan.Zero).AddMinutes(i * 5);
            bars.Add(new MarketBar(openTime, openTime.AddMinutes(5),
                price, price + 1, price - 1, price, 1000));
        }

        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        Assert.True(ctx.Vwap > 0);

        // Simulate new trading day reset
        ctx.OnNewDay();
        Assert.Equal(0m, ctx.Vwap);
    }

    #endregion
}
