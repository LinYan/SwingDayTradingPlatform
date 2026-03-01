using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy.Strategies;

/// <summary>
/// Strategy 7: Second Leg Trend Continuation
///
/// Thesis: The dominant pattern in manual trading journals — after a first-leg impulse
/// move in the trend direction, price pulls back to an anchor (EMA20 or VWAP), then
/// continues with a second leg. Optional fake breakout detection adds conviction.
///
///   1. First leg impulse — recent directional move spanning >= N × ATR
///   2. Pullback to anchor — price retraces 25-75% to EMA20 or VWAP
///   3. Fake breakout (optional) — price briefly crosses anchor then reclaims it
///   4. Entry confirmation — bar closes in trend direction with minimum body
///
/// Entry: confirmation bar close
/// Stop:  below pullback low (long) / above pullback high (short) + ATR buffer
/// Target: entry + risk × reward ratio
/// </summary>
public static class SecondLegStrategy
{
    public const string Name = "SecondLeg";

    public static StrategySignal? Evaluate(
        IReadOnlyList<MarketBar> bars,
        MarketContext ctx,
        MultiStrategyConfig config,
        int idx)
    {
        if (idx < config.SL_FirstLegMaxBars + 2 || ctx.Atr14 <= 0) return null;

        var bar = bars[idx];
        var tick = config.TickSize;

        // Body filter
        if (!PatternDetector.HasMinimumBody(bar, ctx.Atr14, config.SL_EntryBodyMinAtr))
            return null;

        // Trend alignment
        var isUptrend = ctx.Ema20 > ctx.Ema50;
        var isDowntrend = ctx.Ema20 < ctx.Ema50;
        if (!isUptrend && !isDowntrend) return null;

        // EMA slope verification: require genuine trend momentum
        var slope = ctx.Atr14 > 0 ? (ctx.Ema20 - ctx.PreviousEma20) / ctx.Atr14 : 0;
        if (isUptrend && slope < config.EmaMinSlopeAtr) return null;
        if (isDowntrend && slope > -config.EmaMinSlopeAtr) return null;

        // --- LONG SETUP ---
        if (isUptrend && bar.Close > bar.Open)
        {
            // Phase 1: Find first leg (scan backward for impulsive move up)
            if (!FindFirstLeg(bars, ctx.Atr14, config, idx, PositionSide.Long,
                    out var legHigh, out var legLow, out var legEndIdx))
                return null;

            var legRange = legHigh - legLow;

            // Phase 2: Pullback to anchor
            var pullbackLow = decimal.MaxValue;
            for (var i = legEndIdx + 1; i <= idx; i++)
                if (bars[i].Low < pullbackLow) pullbackLow = bars[i].Low;

            var retrace = (legHigh - pullbackLow) / legRange;
            if (retrace < config.SL_MinPullbackRetrace || retrace > config.SL_MaxPullbackRetrace)
                return null;

            // Check anchor proximity (EMA20 or VWAP)
            var anchorTol = ctx.Atr14 * config.SL_AnchorToleranceAtr;
            var nearEma = Math.Abs(pullbackLow - ctx.Ema20) <= anchorTol;
            var nearVwap = ctx.Vwap > 0 && Math.Abs(pullbackLow - ctx.Vwap) <= anchorTol;
            if (!nearEma && !nearVwap) return null;
            var anchor = nearEma ? "EMA20" : "VWAP";

            // Phase 3: Fake breakout detection
            var fakeBreakout = false;
            if (config.SL_EnableFakeBreakout)
            {
                var anchorPrice = nearEma ? ctx.Ema20 : ctx.Vwap;
                if (bar.Low < anchorPrice && bar.Close > anchorPrice)
                    fakeBreakout = true;
            }

            // Phase 4: Entry
            var stop = pullbackLow - tick - ctx.Atr14 * config.SL_StopAtrBuffer;
            var risk = bar.Close - stop;
            if (risk <= 0 || risk > config.MaxStopPoints) return null;
            var target = bar.Close + risk * config.SL_RewardRatio;

            var fb = fakeBreakout ? " (fake breakout)" : "";
            return new StrategySignal(
                $"SL-L-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                bar.CloseTimeUtc, PositionSide.Long, bar.Close, stop, target,
                $"[{Name}] 2nd leg long off {anchor}{fb}, pullback {retrace:P0}, target {target:F2}");
        }

        // --- SHORT SETUP ---
        if (isDowntrend && bar.Close < bar.Open)
        {
            if (!FindFirstLeg(bars, ctx.Atr14, config, idx, PositionSide.Short,
                    out var legHigh, out var legLow, out var legEndIdx))
                return null;

            var legRange = legHigh - legLow;

            var pullbackHigh = decimal.MinValue;
            for (var i = legEndIdx + 1; i <= idx; i++)
                if (bars[i].High > pullbackHigh) pullbackHigh = bars[i].High;

            var retrace = (pullbackHigh - legLow) / legRange;
            if (retrace < config.SL_MinPullbackRetrace || retrace > config.SL_MaxPullbackRetrace)
                return null;

            var anchorTol = ctx.Atr14 * config.SL_AnchorToleranceAtr;
            var nearEma = Math.Abs(pullbackHigh - ctx.Ema20) <= anchorTol;
            var nearVwap = ctx.Vwap > 0 && Math.Abs(pullbackHigh - ctx.Vwap) <= anchorTol;
            if (!nearEma && !nearVwap) return null;
            var anchor = nearEma ? "EMA20" : "VWAP";

            var fakeBreakout = false;
            if (config.SL_EnableFakeBreakout)
            {
                var anchorPrice = nearEma ? ctx.Ema20 : ctx.Vwap;
                if (bar.High > anchorPrice && bar.Close < anchorPrice)
                    fakeBreakout = true;
            }

            var stop = pullbackHigh + tick + ctx.Atr14 * config.SL_StopAtrBuffer;
            var risk = stop - bar.Close;
            if (risk <= 0 || risk > config.MaxStopPoints) return null;
            var target = bar.Close - risk * config.SL_RewardRatio;

            var fb = fakeBreakout ? " (fake breakout)" : "";
            return new StrategySignal(
                $"SL-S-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                bar.CloseTimeUtc, PositionSide.Short, bar.Close, stop, target,
                $"[{Name}] 2nd leg short off {anchor}{fb}, pullback {retrace:P0}, target {target:F2}");
        }

        return null;
    }

    /// <summary>
    /// Scan backward from idx to find the most recent first-leg impulse move.
    /// Uses incremental high/low tracking to avoid O(n³) recomputation.
    /// </summary>
    private static bool FindFirstLeg(
        IReadOnlyList<MarketBar> bars, decimal atr,
        MultiStrategyConfig config, int idx, PositionSide direction,
        out decimal legHigh, out decimal legLow, out int legEndIdx)
    {
        legHigh = 0; legLow = 0; legEndIdx = 0;
        var threshold = atr * config.SL_MinFirstLegAtr;
        var maxBars = config.SL_FirstLegMaxBars;

        // Scan windows ending 2..maxBars bars ago (leave room for pullback)
        for (var end = idx - 2; end >= idx - maxBars && end >= 1; end--)
        {
            // Maintain running high/low as we extend the start backward
            var high = decimal.MinValue;
            var low = decimal.MaxValue;

            for (var start = end; start >= end - maxBars && start >= 0; start--)
            {
                // Incrementally update high/low with the new bar
                if (bars[start].High > high) high = bars[start].High;
                if (bars[start].Low < low) low = bars[start].Low;

                // Need at least 3 bars in the window
                if (end - start < 2) continue;

                if (high - low < threshold) continue;

                var legDir = bars[end].Close > bars[start].Close
                    ? PositionSide.Long : PositionSide.Short;

                if (legDir == direction)
                {
                    legHigh = high;
                    legLow = low;
                    legEndIdx = end;
                    return true;
                }
            }
        }

        return false;
    }
}
