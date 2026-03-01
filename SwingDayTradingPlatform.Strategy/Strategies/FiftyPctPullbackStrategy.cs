using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy.Strategies;

/// <summary>
/// Strategy 3: 50% Pullback Reversal
/// Big move = range over 5-20 bars > 3*ATR
/// Requires configurable retracement depth before entry.
/// Uses tighter stop at most recent swing extreme + ATR buffer.
/// Rejects if stop > MaxStopPoints or move is stale.
/// Entry body filter: requires minimum body size.
/// Exit: ATR trailing stop + R/R target at 50% retracement level
/// </summary>
public static class FiftyPctPullbackStrategy
{
    public const string Name = "FiftyPctPullback";

    public static StrategySignal? Evaluate(
        IReadOnlyList<MarketBar> bars,
        MarketContext ctx,
        MultiStrategyConfig config,
        int idx)
    {
        var bigMove = ctx.LatestBigMove;
        if (bigMove is null || ctx.Atr14 <= 0)
            return null;

        // Staleness check
        if (idx - bigMove.EndIndex > config.BigMoveStaleBars)
            return null;

        var bar = bars[idx];
        var tick = config.TickSize;
        var moveRange = bigMove.MoveHigh - bigMove.MoveLow;
        if (moveRange <= 0) return null;

        var midPoint = (bigMove.MoveHigh + bigMove.MoveLow) / 2m;

        // Entry body filter
        if (!PatternDetector.HasMinimumBody(bar, ctx.Atr14, config.FiftyPctEntryBodyMinAtr))
            return null;

        // After big drop (move direction = Short/down) + higher low confirmation → long
        if (bigMove.MoveDirection == PositionSide.Short)
        {
            var retracementPct = (bar.Close - bigMove.MoveLow) / moveRange;
            if (retracementPct < config.FiftyPctRetracementMin || retracementPct > config.FiftyPctRetracementMax)
                return null;

            var swingLows = ctx.SwingPoints
                .Where(s => s.Type == SwingType.Low && s.BarIndex >= bigMove.StartIndex)
                .ToList();

            if (swingLows.Count >= 2 && swingLows[^1].Price > swingLows[^2].Price)
            {
                if (bar.Close > bar.Open && bar.Close > swingLows[^1].Price)
                {
                    var stop = swingLows[^1].Price - ctx.Atr14 * 0.5m;

                    if (bar.Close - stop > config.MaxStopPoints)
                        return null;

                    var target = midPoint > bar.Close ? midPoint : (decimal?)null;
                    var risk = bar.Close - stop;
                    var reward = target.HasValue ? target.Value - bar.Close : 0m;

                    if (!target.HasValue || reward < risk * 1.5m)
                        return null;

                    return new StrategySignal(
                        $"50P-L-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                        bar.CloseTimeUtc,
                        PositionSide.Long,
                        bar.Close,
                        stop,
                        target,
                        $"[{Name}] Higher low after drop, target 50% at {midPoint:F2}");
                }
            }
        }

        // After big rally (move direction = Long/up) + lower high confirmation → short
        if (bigMove.MoveDirection == PositionSide.Long)
        {
            var retracementPct = (bigMove.MoveHigh - bar.Close) / moveRange;
            if (retracementPct < config.FiftyPctRetracementMin || retracementPct > config.FiftyPctRetracementMax)
                return null;

            var swingHighs = ctx.SwingPoints
                .Where(s => s.Type == SwingType.High && s.BarIndex >= bigMove.StartIndex)
                .ToList();

            if (swingHighs.Count >= 2 && swingHighs[^1].Price < swingHighs[^2].Price)
            {
                if (bar.Close < bar.Open && bar.Close < swingHighs[^1].Price)
                {
                    var stop = swingHighs[^1].Price + ctx.Atr14 * 0.5m;

                    if (stop - bar.Close > config.MaxStopPoints)
                        return null;

                    var target = midPoint < bar.Close ? midPoint : (decimal?)null;
                    var risk = stop - bar.Close;
                    var reward = target.HasValue ? bar.Close - target.Value : 0m;

                    if (!target.HasValue || reward < risk * 1.5m)
                        return null;

                    return new StrategySignal(
                        $"50P-S-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                        bar.CloseTimeUtc,
                        PositionSide.Short,
                        bar.Close,
                        stop,
                        target,
                        $"[{Name}] Lower high after rally, target 50% at {midPoint:F2}");
                }
            }
        }

        return null;
    }
}
