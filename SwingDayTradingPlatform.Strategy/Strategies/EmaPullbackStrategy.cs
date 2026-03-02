using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy.Strategies;

/// <summary>
/// Strategy 1: EMA Pullback + Higher Low / Lower High
/// Long: EMA20 > EMA50, close >= VWAP, swing low near EMA20
/// Short: EMA20 &lt; EMA50, close &lt;= VWAP, swing high near EMA20
/// Filters: body size, EMA slope, RSI confluence
/// Exit: ATR trailing stop + R/R target
/// </summary>
public static class EmaPullbackStrategy
{
    public const string Name = "EmaPullback";

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
            // Slope must be positive and above threshold for longs
            if (slope < config.EmaMinSlopeAtr)
                return null;

            // RSI confluence: long requires RSI in [40, 70]
            if (ctx.Rsi14 < config.EmaRsiLongMin || ctx.Rsi14 > config.EmaRsiLongMax)
                return null;

            if (PatternDetector.IsHigherLow(bars, ctx.SwingPoints, ctx.Ema20, ctx.Atr14, idx, out var swingLowPrice, tolerance))
            {
                var stop = swingLowPrice - tick - ctx.Atr14 * config.EmaStopAtrBuffer;
                var risk = bar.Close - stop;
                if (risk <= 0) return null; // Stop at or above entry — invalid setup
                var target = bar.Close + risk * config.EmaPullbackRewardRatio;
                return new StrategySignal(
                    $"EMA-L-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                    bar.CloseTimeUtc,
                    PositionSide.Long,
                    bar.Close,
                    stop,
                    target,
                    $"[{Name}] Higher low at {swingLowPrice:F2} near EMA20, close > EMA20, target {target:F2}");
            }
        }

        // Short: Lower High near EMA20 + trend + VWAP filter
        if (ctx.Ema20 < ctx.Ema50 && bar.Close <= ctx.Vwap)
        {
            // Slope must be negative and below threshold for shorts
            if (slope > -config.EmaMinSlopeAtr)
                return null;

            // RSI confluence: short requires RSI in [30, 60]
            if (ctx.Rsi14 < config.EmaRsiShortMin || ctx.Rsi14 > config.EmaRsiShortMax)
                return null;

            if (PatternDetector.IsLowerHigh(bars, ctx.SwingPoints, ctx.Ema20, ctx.Atr14, idx, out var swingHighPrice, tolerance))
            {
                var stop = swingHighPrice + tick + ctx.Atr14 * config.EmaStopAtrBuffer;
                var risk = stop - bar.Close;
                if (risk <= 0) return null; // Stop at or below entry — invalid setup
                var target = bar.Close - risk * config.EmaPullbackRewardRatio;
                return new StrategySignal(
                    $"EMA-S-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                    bar.CloseTimeUtc,
                    PositionSide.Short,
                    bar.Close,
                    stop,
                    target,
                    $"[{Name}] Lower high at {swingHighPrice:F2} near EMA20, close < EMA20, target {target:F2}");
            }
        }

        return null;
    }
}
