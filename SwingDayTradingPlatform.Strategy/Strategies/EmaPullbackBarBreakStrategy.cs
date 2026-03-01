using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy.Strategies;

/// <summary>
/// Strategy 5: EMA Pullback + Bar-Break Exit
/// Same entry logic as EmaPullbackStrategy (Higher Low / Lower High near EMA20).
/// Exit: break of previous bar's low (long) or high (short) instead of fixed R/R target.
/// </summary>
public static class EmaPullbackBarBreakStrategy
{
    public const string Name = "EmaPullbackBarBreak";

    public static StrategySignal? Evaluate(
        IReadOnlyList<MarketBar> bars,
        MarketContext ctx,
        MultiStrategyConfig config,
        int idx)
    {
        if (idx < 1) return null;

        var bar = bars[idx];
        var tick = config.TickSize;
        var tolerance = config.EmaPullbackTolerance;

        // Body filter: skip if entry bar body is too small
        if (!PatternDetector.HasMinimumBody(bar, ctx.Atr14, config.EmaBodyMinAtrRatio))
            return null;

        // EMA slope filter: reject if EMA20 is flat (no trend momentum)
        var slope = ctx.Atr14 > 0 ? (ctx.Ema20 - ctx.PreviousEma20) / ctx.Atr14 : 0;

        // Long: Higher Low near EMA20 + trend + VWAP filter
        if (ctx.Ema20 > ctx.Ema50 && bar.Close >= ctx.Vwap)
        {
            if (slope < config.EmaMinSlopeAtr)
                return null;

            if (ctx.Rsi14 < config.EmaRsiLongMin || ctx.Rsi14 > config.EmaRsiLongMax)
                return null;

            if (PatternDetector.IsHigherLow(bars, ctx.SwingPoints, ctx.Ema20, ctx.Atr14, idx, out var swingLowPrice, tolerance))
            {
                var stop = swingLowPrice - tick - ctx.Atr14 * config.EmaStopAtrBuffer;
                return new StrategySignal(
                    $"EMABB-L-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                    bar.CloseTimeUtc,
                    PositionSide.Long,
                    bar.Close,
                    stop,
                    null,
                    $"[{Name}] Higher low at {swingLowPrice:F2} near EMA20, exit on bar-break");
            }
        }

        // Short: Lower High near EMA20 + trend + VWAP filter
        if (ctx.Ema20 < ctx.Ema50 && bar.Close <= ctx.Vwap)
        {
            if (slope > -config.EmaMinSlopeAtr)
                return null;

            if (ctx.Rsi14 < config.EmaRsiShortMin || ctx.Rsi14 > config.EmaRsiShortMax)
                return null;

            if (PatternDetector.IsLowerHigh(bars, ctx.SwingPoints, ctx.Ema20, ctx.Atr14, idx, out var swingHighPrice, tolerance))
            {
                var stop = swingHighPrice + tick + ctx.Atr14 * config.EmaStopAtrBuffer;
                return new StrategySignal(
                    $"EMABB-S-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                    bar.CloseTimeUtc,
                    PositionSide.Short,
                    bar.Close,
                    stop,
                    null,
                    $"[{Name}] Lower high at {swingHighPrice:F2} near EMA20, exit on bar-break");
            }
        }

        return null;
    }
}
