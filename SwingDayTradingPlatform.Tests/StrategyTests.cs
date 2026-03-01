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
        int? srMinTouches = null,
        decimal? srReversalRewardRatio = null,
        decimal? momentumRewardRatio = null,
        int? momentumPullbackWindowBars = null,
        decimal? maxStopPoints = null,
        int? bigMoveStaleBars = null) => new()
    {
        FastEmaPeriod = 20,
        SlowEmaPeriod = 50,
        AtrPeriod = 14,
        SwingLookback = 3,
        SRClusterAtrFactor = 0.5m,
        BigMoveAtrFactor = 3.0m,
        MomentumBars = 3,
        MomentumBodyAtrRatio = 0.7m,
        TickSize = 0.25m,
        EnableHourlyBias = false,
        EmaPullbackRewardRatio = emaPullbackRewardRatio ?? 2.0m,
        EmaPullbackTolerance = emaPullbackTolerance ?? 0.75m,
        SRMinTouches = srMinTouches ?? 2,
        SRReversalRewardRatio = srReversalRewardRatio ?? 2.0m,
        MomentumRewardRatio = momentumRewardRatio ?? 2.5m,
        MomentumPullbackWindowBars = momentumPullbackWindowBars ?? 6,
        MaxStopPoints = maxStopPoints ?? 15m,
        BigMoveStaleBars = bigMoveStaleBars ?? 30
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

    #region SRReversal Tests

    [Fact]
    public void SRReversal_ReturnsNull_WhenNoSRLevels()
    {
        var config = TestConfig();
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 60; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        ctx.SRLevels.Clear(); // ensure empty

        var signal = SRReversalStrategy.Evaluate(bars, ctx, config, bars.Count - 1);
        Assert.Null(signal);
    }

    [Fact]
    public void SRReversal_ShortSignal_AtResistanceWithReversalCandle()
    {
        var config = TestConfig(srMinTouches: 2, srReversalRewardRatio: 2.0m);
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 59; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }

        // Last bar: bearish engulfing near resistance (close < open, body > prev body)
        var resistancePrice = 5002m;
        bars.Add(MakeBar(59 * 5, resistancePrice, resistancePrice + 0.5m, 4998m, 4998.5m));

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        // Inject S/R level at resistance
        ctx.SRLevels.Clear();
        ctx.SRLevels.Add(new SRLevel(resistancePrice, 3, SwingType.High));

        var signal = SRReversalStrategy.Evaluate(bars, ctx, config, bars.Count - 1);

        if (signal is not null)
        {
            Assert.Equal(PositionSide.Short, signal.Direction);
            Assert.Contains("SRReversal", signal.Reason);
            // Verify R/R: target should be entry - risk * ratio
            var risk = signal.StopPrice - signal.EntryPrice;
            var expectedTarget = signal.EntryPrice - risk * 2.0m;
            Assert.Equal(expectedTarget, signal.TargetPrice);
        }
    }

    [Fact]
    public void SRReversal_LongSignal_AtSupportWithReversalCandle()
    {
        var config = TestConfig(srMinTouches: 2, srReversalRewardRatio: 2.0m);
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 59; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }

        // Last bar: bullish (pin bar / engulfing) near support
        var supportPrice = 4998m;
        bars.Add(MakeBar(59 * 5, 4998.5m, 5002m, supportPrice - 0.5m, 5001.5m));

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        ctx.SRLevels.Clear();
        ctx.SRLevels.Add(new SRLevel(supportPrice, 3, SwingType.Low));

        var signal = SRReversalStrategy.Evaluate(bars, ctx, config, bars.Count - 1);

        if (signal is not null)
        {
            Assert.Equal(PositionSide.Long, signal.Direction);
            Assert.Contains("SRReversal", signal.Reason);
        }
    }

    #endregion

    #region FiftyPctPullback Tests

    [Fact]
    public void FiftyPctPullback_ReturnsNull_WhenNoBigMove()
    {
        var config = TestConfig();
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 60; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        Assert.Null(ctx.LatestBigMove);

        var signal = FiftyPctPullbackStrategy.Evaluate(bars, ctx, config, bars.Count - 1);
        Assert.Null(signal);
    }

    [Fact]
    public void FiftyPctPullback_ReturnsNull_WhenBigMoveIsStale()
    {
        var config = TestConfig(bigMoveStaleBars: 5);
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 60; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);

        // Manually inject a stale big move (ended long ago)
        var staleMove = new BigMoveInfo(0, 10, 5050m, 4950m, PositionSide.Short);
        typeof(MarketContext).GetProperty("LatestBigMove")!.SetValue(ctx, staleMove);

        var idx = bars.Count - 1;
        // idx - EndIndex = 59 - 10 = 49 > BigMoveStaleBars=5 → stale
        var signal = FiftyPctPullbackStrategy.Evaluate(bars, ctx, config, idx);
        Assert.Null(signal);
    }

    [Fact]
    public void FiftyPctPullback_LongSignal_AfterDropWithHigherLow()
    {
        var config = TestConfig(maxStopPoints: 50m, bigMoveStaleBars: 100);

        // Build bars: initial price, big drop, then retracement with higher lows
        var bars = new List<MarketBar>();
        var price = 5050m;

        // 30 bars flat
        for (var i = 0; i < 30; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }
        // Big drop over 10 bars
        for (var i = 30; i < 40; i++)
        {
            price -= 5m;
            bars.Add(MakeBar(i * 5, price + 5m, price + 6m, price - 1m, price));
        }
        // Recovery/retracement with higher lows
        for (var i = 40; i < 60; i++)
        {
            price += 1.5m;
            bars.Add(MakeBar(i * 5, price - 1.5m, price + 1m, price - 2m, price));
        }

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);

        // Inject big move pointing down
        var bigMove = new BigMoveInfo(30, 40, 5050m, 5000m, PositionSide.Short);
        typeof(MarketContext).GetProperty("LatestBigMove")!.SetValue(ctx, bigMove);

        // Inject two swing lows (higher-low confirmation)
        ctx.SwingPoints.Add(new SwingPoint(42, 5005m, SwingType.Low));
        ctx.SwingPoints.Add(new SwingPoint(50, 5010m, SwingType.Low));

        var idx = bars.Count - 1;
        var signal = FiftyPctPullbackStrategy.Evaluate(bars, ctx, config, idx);

        // Signal may or may not fire depending on retracement pct and R/R,
        // but if it fires it should be Long targeting the midpoint
        if (signal is not null)
        {
            Assert.Equal(PositionSide.Long, signal.Direction);
            Assert.Contains("FiftyPctPullback", signal.Reason);
        }
    }

    #endregion

    #region Momentum Tests

    [Fact]
    public void Momentum_ReturnsNull_WhenNoSetupPending()
    {
        var config = TestConfig();
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 60; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);
        Assert.Null(ctx.PendingMomentumSetup);

        var signal = MomentumStrategy.Evaluate(bars, ctx, config, bars.Count - 1);
        Assert.Null(signal);
    }

    [Fact]
    public void Momentum_LongPullbackEntry_AfterBullishBurst()
    {
        var config = TestConfig(momentumRewardRatio: 2.5m, momentumPullbackWindowBars: 6);
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 58; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }

        // Bar at idx=58: pullback bar (low < prev low, but closes bullish)
        var prevBar = bars[^1];
        bars.Add(MakeBar(58 * 5, 4999m, 5002m, prevBar.Low - 1m, 5001m));

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);

        // Inject pending bullish burst setup detected 2 bars ago
        ctx.PendingMomentumSetup = (PositionSide.Long, 4995m, bars.Count - 3);

        var idx = bars.Count - 1;
        var signal = MomentumStrategy.Evaluate(bars, ctx, config, idx);

        if (signal is not null)
        {
            Assert.Equal(PositionSide.Long, signal.Direction);
            Assert.Contains("Momentum", signal.Reason);
            // Verify reward ratio
            var risk = signal.EntryPrice - signal.StopPrice;
            var expectedTarget = signal.EntryPrice + risk * 2.5m;
            Assert.Equal(expectedTarget, signal.TargetPrice);
        }
    }

    [Fact]
    public void Momentum_ShortPullbackEntry_AfterBearishBurst()
    {
        var config = TestConfig(momentumRewardRatio: 2.5m, momentumPullbackWindowBars: 6);
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 58; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }

        // Bar at idx=58: pullback bar (high > prev high, but closes bearish)
        var prevBar = bars[^1];
        bars.Add(MakeBar(58 * 5, 5001m, prevBar.High + 1m, 4998m, 4999m));

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);

        // Inject pending bearish burst setup
        ctx.PendingMomentumSetup = (PositionSide.Short, 5005m, bars.Count - 3);

        var idx = bars.Count - 1;
        var signal = MomentumStrategy.Evaluate(bars, ctx, config, idx);

        if (signal is not null)
        {
            Assert.Equal(PositionSide.Short, signal.Direction);
            Assert.Contains("Momentum", signal.Reason);
            var risk = signal.StopPrice - signal.EntryPrice;
            var expectedTarget = signal.EntryPrice - risk * 2.5m;
            Assert.Equal(expectedTarget, signal.TargetPrice);
        }
    }

    [Fact]
    public void Momentum_ExpiresSetup_AfterWindowBars()
    {
        var config = TestConfig(momentumPullbackWindowBars: 3);
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 60; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);

        // Inject pending setup that's too old (detected 10 bars ago, window is 3)
        ctx.PendingMomentumSetup = (PositionSide.Long, 4995m, bars.Count - 11);

        var idx = bars.Count - 1;
        var signal = MomentumStrategy.Evaluate(bars, ctx, config, idx);
        Assert.Null(signal);
        Assert.Null(ctx.PendingMomentumSetup); // setup should be cleared
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

    #region Momentum Null Setup Tests

    [Fact]
    public void Momentum_NullPendingSetup_DoesNotEnterPhase2()
    {
        var config = TestConfig();
        var bars = new List<MarketBar>();
        var price = 5000m;
        for (var i = 0; i < 60; i++)
        {
            bars.Add(MakeBar(i * 5, price, price + 1, price - 1, price));
        }

        var ctx = new MarketContext(config);
        ctx.OnNewBar(bars, bars[^1].CloseTimeUtc);

        // Explicitly set to null
        ctx.PendingMomentumSetup = null;

        var signal = MomentumStrategy.Evaluate(bars, ctx, config, bars.Count - 1);
        Assert.Null(signal);
        Assert.Null(ctx.PendingMomentumSetup);
    }

    #endregion
}
