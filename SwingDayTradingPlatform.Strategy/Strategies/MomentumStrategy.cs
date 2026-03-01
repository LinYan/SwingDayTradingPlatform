using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy.Strategies;

/// <summary>
/// Strategy 4: Momentum Trend Following with Pullback Entry
/// Phase 1: Detect momentum burst (relaxed: average body threshold, allow moderate bars)
/// Phase 2: Enter on pullback with depth check and containment
/// Exit: ATR trailing stop + R/R target
/// </summary>
public static class MomentumStrategy
{
    public const string Name = "Momentum";

    public static StrategySignal? Evaluate(
        IReadOnlyList<MarketBar> bars,
        MarketContext ctx,
        MultiStrategyConfig config,
        int idx)
    {
        if (ctx.Atr14 <= 0) return null;

        var bar = bars[idx];
        var tick = config.TickSize;

        // Phase 1: Detect new momentum burst and cache it
        if (PatternDetector.IsMomentumBurst(
                bars, ctx.Atr14, config.MomentumAvgBodyAtrRatio,
                config.MomentumBars, idx,
                out var burstDirection, out var rawStop,
                config.MomentumMaxModerate, config.MomentumModerateMinRatio))
        {
            ctx.PendingMomentumSetup = (burstDirection, rawStop, idx);
            return null;
        }

        // Phase 2: Check for pullback entry on pending setup
        if (ctx.PendingMomentumSetup is (var direction, var burstStop, var detectedAtIdx))
        {
            // Expiry check
            if (idx - detectedAtIdx > config.MomentumPullbackWindowBars)
            {
                ctx.PendingMomentumSetup = null;
                return null;
            }

            if (direction == PositionSide.Long)
            {
                // Pullback: bar pulls back (Low < previous bar Low) then closes bullish
                if (idx >= 1 && bar.Low < bars[idx - 1].Low && bar.Close > bar.Open)
                {
                    // Compute burst high for retrace check
                    var burstHigh = decimal.MinValue;
                    for (var i = detectedAtIdx - config.MomentumBars + 1; i <= detectedAtIdx && i < bars.Count; i++)
                    {
                        if (i >= 0 && bars[i].High > burstHigh) burstHigh = bars[i].High;
                    }

                    // Pullback depth check
                    var burstRange = burstHigh - burstStop;
                    if (burstRange > 0)
                    {
                        var retrace = (burstHigh - bar.Low) / burstRange;
                        if (retrace < config.MomentumPullbackMinRetrace || retrace > config.MomentumPullbackMaxRetrace)
                            return null;
                    }

                    // Better stop: lowest low of pullback bars
                    var pullbackLow = bar.Low;
                    for (var i = detectedAtIdx + 1; i <= idx && i < bars.Count; i++)
                    {
                        if (bars[i].Low < pullbackLow) pullbackLow = bars[i].Low;
                    }

                    var stop = pullbackLow - tick;
                    var risk = bar.Close - stop;
                    var target = bar.Close + risk * config.MomentumRewardRatio;

                    ctx.PendingMomentumSetup = null;
                    return new StrategySignal(
                        $"MOM-L-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                        bar.CloseTimeUtc,
                        PositionSide.Long,
                        bar.Close,
                        stop,
                        target,
                        $"[{Name}] Pullback entry after {config.MomentumBars}-bar bullish burst, target {target:F2}");
                }
            }
            else if (direction == PositionSide.Short)
            {
                // Pullback: bar pulls back (High > previous bar High) then closes bearish
                if (idx >= 1 && bar.High > bars[idx - 1].High && bar.Close < bar.Open)
                {
                    // Compute burst low for retrace check
                    var burstLow = decimal.MaxValue;
                    for (var i = detectedAtIdx - config.MomentumBars + 1; i <= detectedAtIdx && i < bars.Count; i++)
                    {
                        if (i >= 0 && bars[i].Low < burstLow) burstLow = bars[i].Low;
                    }

                    // Pullback depth check
                    var burstRange = burstStop - burstLow;
                    if (burstRange > 0)
                    {
                        var retrace = (bar.High - burstLow) / burstRange;
                        if (retrace < config.MomentumPullbackMinRetrace || retrace > config.MomentumPullbackMaxRetrace)
                            return null;
                    }

                    // Better stop: highest high of pullback bars
                    var pullbackHigh = bar.High;
                    for (var i = detectedAtIdx + 1; i <= idx && i < bars.Count; i++)
                    {
                        if (bars[i].High > pullbackHigh) pullbackHigh = bars[i].High;
                    }

                    var stop = pullbackHigh + tick;
                    var risk = stop - bar.Close;
                    var target = bar.Close - risk * config.MomentumRewardRatio;

                    ctx.PendingMomentumSetup = null;
                    return new StrategySignal(
                        $"MOM-S-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                        bar.CloseTimeUtc,
                        PositionSide.Short,
                        bar.Close,
                        stop,
                        target,
                        $"[{Name}] Pullback entry after {config.MomentumBars}-bar bearish burst, target {target:F2}");
                }
            }
        }

        return null;
    }
}
