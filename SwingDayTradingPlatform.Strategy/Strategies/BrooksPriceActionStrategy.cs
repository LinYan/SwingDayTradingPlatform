using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy.Strategies;

/// <summary>
/// Strategy 9: Brooks Price Action — Al Brooks-style 5-minute ES entries
///
/// Core thesis: Pure price-action reading using only the 20 EMA. Al Brooks teaches
/// that most profitable setups come from two simple patterns:
///
///   A) Two-Legged Pullback (H2/L2): After a trend move, price pulls back in two
///      distinct legs to the EMA20 area — the highest-probability with-trend entry.
///      H2 = second higher low in a bull trend, L2 = second lower high in a bear trend.
///
///   B) ii Pattern Breakout: Two consecutive inside bars (each bar's range is inside
///      the prior bar) create a tight compression. Breakout of the ii pattern in the
///      trend direction is a high-probability entry with a tight stop.
///
/// Entry: Signal bar must close in trend direction with body >= 50% of range.
/// Stop:  One tick beyond signal bar's extreme (opposite side of entry).
/// Target: Measured move (first leg projected from pullback end) or 2:1 R/R.
/// </summary>
public static class BrooksPriceActionStrategy
{
    public const string Name = "BrooksPA";

    public static StrategySignal? Evaluate(
        IReadOnlyList<MarketBar> bars,
        MarketContext ctx,
        MultiStrategyConfig config,
        int idx)
    {
        if (idx < 10 || ctx.Atr14 <= 0) return null;

        var bar = bars[idx];
        var tick = config.TickSize;

        // Determine always-in direction using EMA20 relationship
        // Brooks: if price is mostly above EMA20, always-in long; below, always-in short
        var alwaysInLong = bar.Close > ctx.Ema20 && ctx.Ema20 > ctx.Ema50;
        var alwaysInShort = bar.Close < ctx.Ema20 && ctx.Ema20 < ctx.Ema50;
        if (!alwaysInLong && !alwaysInShort) return null;

        // Signal bar quality: body must be >= configured ratio of range, close in trend direction
        var body = Math.Abs(bar.Close - bar.Open);
        var range = bar.High - bar.Low;
        if (range <= 0 || body < range * config.BrooksPA_SignalBarBodyRatio) return null;

        // Minimum bar range filter: reject tiny bars
        if (range < ctx.Atr14 * config.BrooksPA_MinBarRangeAtr) return null;

        // Try ii pattern first (tighter stop, higher probability)
        var iiSignal = TryIIBreakout(bars, ctx, config, idx, alwaysInLong ? PositionSide.Long : PositionSide.Short);
        if (iiSignal is not null) return iiSignal;

        // Try two-legged pullback (H2/L2)
        var h2l2Signal = TryTwoLeggedPullback(bars, ctx, config, idx, alwaysInLong ? PositionSide.Long : PositionSide.Short);
        return h2l2Signal;
    }

    /// <summary>
    /// ii Pattern: Two consecutive inside bars, then breakout in trend direction.
    /// Inside bar = high lower than prior high AND low higher than prior low.
    /// </summary>
    private static StrategySignal? TryIIBreakout(
        IReadOnlyList<MarketBar> bars,
        MarketContext ctx,
        MultiStrategyConfig config,
        int idx,
        PositionSide direction)
    {
        if (idx < 3) return null;

        var bar = bars[idx];     // current (breakout) bar
        var i1 = bars[idx - 1];  // second inside bar
        var i2 = bars[idx - 2];  // first inside bar
        var mother = bars[idx - 3]; // mother bar (the bar both insides are inside of)
        var tick = config.TickSize;

        // Check ii: i2 is inside mother, i1 is inside i2
        var i2Inside = i2.High < mother.High && i2.Low > mother.Low;
        var i1Inside = i1.High < i2.High && i1.Low > i2.Low;
        if (!i2Inside || !i1Inside) return null;

        // Breakout confirmation: current bar must break in trend direction
        if (direction == PositionSide.Long)
        {
            // Bar must close above mother high (breakout) and be bullish
            if (bar.Close <= bar.Open) return null;
            if (bar.High <= i1.High) return null; // No breakout

            var stop = Math.Min(i1.Low, i2.Low) - tick;
            var risk = bar.Close - stop;
            if (risk <= 0 || risk > config.MaxStopPoints) return null;

            // Max stop ticks check
            if (risk / tick > config.BrooksPA_MaxStopTicks) return null;

            var target = bar.Close + risk * config.BrooksPA_RewardRatio;
            return new StrategySignal(
                $"BPA-iiL-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                bar.CloseTimeUtc,
                PositionSide.Long,
                bar.Close,
                stop,
                target,
                $"[{Name}] ii breakout long above {i1.High:F2}, stop {stop:F2}");
        }
        else
        {
            if (bar.Close >= bar.Open) return null;
            if (bar.Low >= i1.Low) return null; // No breakout

            var stop = Math.Max(i1.High, i2.High) + tick;
            var risk = stop - bar.Close;
            if (risk <= 0 || risk > config.MaxStopPoints) return null;

            if (risk / tick > config.BrooksPA_MaxStopTicks) return null;

            var target = bar.Close - risk * config.BrooksPA_RewardRatio;
            return new StrategySignal(
                $"BPA-iiS-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                bar.CloseTimeUtc,
                PositionSide.Short,
                bar.Close,
                stop,
                target,
                $"[{Name}] ii breakout short below {i1.Low:F2}, stop {stop:F2}");
        }
    }

    /// <summary>
    /// Two-Legged Pullback (H2/L2): After a trend move, price pulls back in two
    /// distinct legs toward EMA20. Entry is on the second leg completion.
    ///
    /// For long (H2): Find two swing lows in the pullback area, second higher than first,
    /// both near EMA20. Current bar is the signal bar closing bullish.
    ///
    /// For short (L2): Find two swing highs in the pullback area, second lower than first,
    /// both near EMA20. Current bar is the signal bar closing bearish.
    /// </summary>
    private static StrategySignal? TryTwoLeggedPullback(
        IReadOnlyList<MarketBar> bars,
        MarketContext ctx,
        MultiStrategyConfig config,
        int idx,
        PositionSide direction)
    {
        var bar = bars[idx];
        var tick = config.TickSize;
        var lookback = config.BrooksPA_PullbackLookback;
        var emaTolerance = ctx.Atr14 * config.BrooksPA_EmaToleranceAtr;

        if (direction == PositionSide.Long)
        {
            // Signal bar must be bullish
            if (bar.Close <= bar.Open) return null;

            // Find two swing lows (legs of the pullback) within lookback window near EMA20
            var leg1Low = decimal.MaxValue;
            var leg1Idx = -1;
            var leg2Low = decimal.MaxValue;
            var leg2Idx = -1;

            // Scan for swing lows: a bar whose low is lower than both neighbors
            var startScan = Math.Max(1, idx - lookback);
            for (var i = startScan; i < idx - 1; i++)
            {
                if (bars[i].Low <= bars[i - 1].Low && bars[i].Low <= bars[i + 1].Low)
                {
                    // This is a swing low candidate
                    if (leg1Idx == -1)
                    {
                        leg1Low = bars[i].Low;
                        leg1Idx = i;
                    }
                    else if (i - leg1Idx >= 2) // Need separation between legs
                    {
                        leg2Low = bars[i].Low;
                        leg2Idx = i;
                    }
                }
            }

            if (leg1Idx == -1 || leg2Idx == -1) return null;

            // H2 pattern: second low must be higher than first (higher low)
            if (leg2Low <= leg1Low) return null;

            // Both lows should be near EMA20 (within tolerance)
            // At least the second leg should touch/test the EMA area
            var leg2BarEma = EstimateEmaAtBar(bars, ctx, idx, leg2Idx);
            if (Math.Abs(leg2Low - leg2BarEma) > emaTolerance) return null;

            // Current bar must be near or above EMA20
            if (bar.Low > ctx.Ema20 + emaTolerance) return null; // Too far above, not a pullback entry

            // Between the two legs, price must have rallied (not just sideways)
            var interLegHigh = decimal.MinValue;
            for (var i = leg1Idx + 1; i < leg2Idx; i++)
                if (bars[i].High > interLegHigh) interLegHigh = bars[i].High;
            if (interLegHigh <= leg1Low) return null; // No rally between legs

            var stop = leg2Low - tick;
            var risk = bar.Close - stop;
            if (risk <= 0 || risk > config.MaxStopPoints) return null;
            if (risk / tick > config.BrooksPA_MaxStopTicks) return null;

            // Measured move target: project first leg distance from second leg low
            var firstLegSize = interLegHigh - leg1Low;
            var measuredTarget = leg2Low + firstLegSize;
            var rrTarget = bar.Close + risk * config.BrooksPA_RewardRatio;
            var target = Math.Min(measuredTarget, rrTarget); // Conservative: use smaller target

            return new StrategySignal(
                $"BPA-H2-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                bar.CloseTimeUtc,
                PositionSide.Long,
                bar.Close,
                stop,
                target,
                $"[{Name}] H2 two-leg pullback, legs at {leg1Low:F2}/{leg2Low:F2}, target {target:F2}");
        }
        else // Short — L2 pattern
        {
            // Signal bar must be bearish
            if (bar.Close >= bar.Open) return null;

            var leg1High = decimal.MinValue;
            var leg1Idx = -1;
            var leg2High = decimal.MinValue;
            var leg2Idx = -1;

            var startScan = Math.Max(1, idx - lookback);
            for (var i = startScan; i < idx - 1; i++)
            {
                if (bars[i].High >= bars[i - 1].High && bars[i].High >= bars[i + 1].High)
                {
                    if (leg1Idx == -1)
                    {
                        leg1High = bars[i].High;
                        leg1Idx = i;
                    }
                    else if (i - leg1Idx >= 2)
                    {
                        leg2High = bars[i].High;
                        leg2Idx = i;
                    }
                }
            }

            if (leg1Idx == -1 || leg2Idx == -1) return null;

            // L2 pattern: second high must be lower than first (lower high)
            if (leg2High >= leg1High) return null;

            var leg2BarEma = EstimateEmaAtBar(bars, ctx, idx, leg2Idx);
            if (Math.Abs(leg2High - leg2BarEma) > emaTolerance) return null;

            if (bar.High < ctx.Ema20 - emaTolerance) return null;

            // Between the two legs, price must have dropped
            var interLegLow = decimal.MaxValue;
            for (var i = leg1Idx + 1; i < leg2Idx; i++)
                if (bars[i].Low < interLegLow) interLegLow = bars[i].Low;
            if (interLegLow >= leg1High) return null;

            var stop = leg2High + tick;
            var risk = stop - bar.Close;
            if (risk <= 0 || risk > config.MaxStopPoints) return null;
            if (risk / tick > config.BrooksPA_MaxStopTicks) return null;

            var firstLegSize = leg1High - interLegLow;
            var measuredTarget = leg2High - firstLegSize;
            var rrTarget = bar.Close - risk * config.BrooksPA_RewardRatio;
            var target = Math.Max(measuredTarget, rrTarget);

            return new StrategySignal(
                $"BPA-L2-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                bar.CloseTimeUtc,
                PositionSide.Short,
                bar.Close,
                stop,
                target,
                $"[{Name}] L2 two-leg pullback, legs at {leg1High:F2}/{leg2High:F2}, target {target:F2}");
        }
    }

    /// <summary>
    /// Rough linear interpolation of EMA20 at a historical bar index,
    /// based on current EMA20 slope. Used to check if a past swing was near EMA20.
    /// </summary>
    private static decimal EstimateEmaAtBar(
        IReadOnlyList<MarketBar> bars,
        MarketContext ctx,
        int currentIdx,
        int targetIdx)
    {
        var barsBack = currentIdx - targetIdx;
        if (barsBack <= 0) return ctx.Ema20;

        // Use the EMA slope to extrapolate backward
        var slopePerBar = ctx.Ema20 - ctx.PreviousEma20;
        return ctx.Ema20 - slopePerBar * barsBack;
    }
}
