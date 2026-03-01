using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Strategy;

namespace SwingDayTradingPlatform.Strategy.Strategies;

/// <summary>
/// Strategy 2: S/R Level Reversal
/// Selects the nearest S/R level with sufficient touches.
/// Requires reversal candle confirmation (pin bar or engulfing).
/// Filters: level freshness, min reversal range, counter-trend extra touches.
/// Exit: ATR trailing stop + R/R target
/// </summary>
public static class SRReversalStrategy
{
    public const string Name = "SRReversal";

    public static StrategySignal? Evaluate(
        IReadOnlyList<MarketBar> bars,
        MarketContext ctx,
        MultiStrategyConfig config,
        int idx)
    {
        if (ctx.SRLevels.Count == 0 || ctx.Atr14 <= 0 || idx < 1)
            return null;

        var bar = bars[idx];
        var prevBar = bars[idx - 1];
        var tick = config.TickSize;
        var proximity = ctx.Atr14 * config.SRClusterAtrFactor;

        // Short setup: find nearest resistance level (dominated by swing highs)
        var nearestResistance = ctx.SRLevels
            .Where(l => l.DominantType == SwingType.High)
            .Where(l => l.LatestTouchBarIndex >= idx - config.SRMaxFreshnessBarsSinceTouch)
            .Where(l => Math.Abs(bar.High - l.Price) <= proximity)
            .Where(l =>
            {
                // Counter-trend: short when EMA20 > EMA50 requires extra touches
                var minTouches = ctx.Ema20 > ctx.Ema50
                    ? config.SRCounterTrendMinTouches
                    : config.SRMinTouches;
                return l.TouchCount >= minTouches;
            })
            .OrderBy(l => Math.Abs(bar.High - l.Price))
            .FirstOrDefault();

        if (nearestResistance is not null)
        {
            if (bar.Close <= nearestResistance.Price
                && PatternDetector.IsReversalCandle(bar, prevBar, PositionSide.Short, ctx.Atr14, config.SRMinReversalRangeAtr))
            {
                // Wick-based stop: use bar's high if tighter than level
                var levelStop = nearestResistance.Price + tick;
                var wickStop = bar.High + tick;
                var stop = wickStop < levelStop ? wickStop : levelStop;

                var risk = stop - bar.Close;
                var target = bar.Close - risk * config.SRReversalRewardRatio;
                return new StrategySignal(
                    $"SR-S-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                    bar.CloseTimeUtc,
                    PositionSide.Short,
                    bar.Close,
                    stop,
                    target,
                    $"[{Name}] Bearish reversal at resistance {nearestResistance.Price:F2} (touches: {nearestResistance.TouchCount}), target {target:F2}");
            }
        }

        // Long setup: find nearest support level (dominated by swing lows)
        var nearestSupport = ctx.SRLevels
            .Where(l => l.DominantType == SwingType.Low)
            .Where(l => l.LatestTouchBarIndex >= idx - config.SRMaxFreshnessBarsSinceTouch)
            .Where(l => Math.Abs(bar.Low - l.Price) <= proximity)
            .Where(l =>
            {
                // Counter-trend: long when EMA20 < EMA50 requires extra touches
                var minTouches = ctx.Ema20 < ctx.Ema50
                    ? config.SRCounterTrendMinTouches
                    : config.SRMinTouches;
                return l.TouchCount >= minTouches;
            })
            .OrderBy(l => Math.Abs(bar.Low - l.Price))
            .FirstOrDefault();

        if (nearestSupport is not null)
        {
            if (bar.Close >= nearestSupport.Price
                && PatternDetector.IsReversalCandle(bar, prevBar, PositionSide.Long, ctx.Atr14, config.SRMinReversalRangeAtr))
            {
                // Wick-based stop: use bar's low if tighter than level
                var levelStop = nearestSupport.Price - tick;
                var wickStop = bar.Low - tick;
                var stop = wickStop > levelStop ? wickStop : levelStop;

                var risk = bar.Close - stop;
                var target = bar.Close + risk * config.SRReversalRewardRatio;
                return new StrategySignal(
                    $"SR-L-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                    bar.CloseTimeUtc,
                    PositionSide.Long,
                    bar.Close,
                    stop,
                    target,
                    $"[{Name}] Bullish reversal at support {nearestSupport.Price:F2} (touches: {nearestSupport.TouchCount}), target {target:F2}");
            }
        }

        return null;
    }
}
