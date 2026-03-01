using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy.Strategies;

/// <summary>
/// Strategy 12: Slope Inflection + Strong Trend Reversal (斜率拐点)
///
/// Detects EMA slope inflection points (trend turns), optionally confirmed by
/// strong trend bars. Uses bar-by-bar trailing stop (prevBar.Open) with no target —
/// designed to capture rare large trend moves while keeping losses small.
///
/// Entry logic:
///   LONG:  EMA slope was negative/flat → slope crosses positive (first bar only)
///   SHORT: EMA slope was positive/flat → slope crosses negative (first bar only)
///
/// Exit: bar-by-bar trailing stop using prevBar.Open (starts from bar 2 after entry)
/// </summary>
public static class SlopeInflectionStrategy
{
    public const string Name = "SlopeInflection";

    // Incremental EMA state — avoids O(N) recomputation and survives bar trimming
    private static decimal _runningEma;
    private static decimal _prevEma;
    private static int _emaBarCount;
    private static int _emaPeriod;
    private static decimal _emaMultiplier;

    public static StrategySignal? Evaluate(
        IReadOnlyList<MarketBar> bars,
        MarketContext ctx,
        MultiStrategyConfig config,
        int idx)
    {
        var period = config.SI_SmoothingPeriod;
        var window = config.SI_FlatCrossWindow;
        var eps = config.SI_SlopeEpsTicks * config.TickSize;
        var tick = config.TickSize;

        // Need enough bars for EMA warmup + slope window
        if (idx < period + window + 1 || ctx.Atr14 <= 0) return null;

        // Cooldown: skip first N bars of the day (6 bars = 30 min for 5-min bars)
        if (ctx.BarsSinceOpen <= config.SI_CooldownBars) return null;

        // Update incremental EMA
        UpdateEma(bars, period);

        var bar = bars[idx];

        // Compute slope array over the lookback window + 1 (for crossing check)
        // We need slopes for [idx - window - 1 .. idx] relative positions
        // Recompute EMA series only for the needed window using a local forward pass
        var windowNeeded = window + 2; // +1 for crossing check, +1 for slope diff
        var startIdx = Math.Max(0, idx - windowNeeded - period);
        var localCloses = new decimal[idx - startIdx + 1];
        for (var i = 0; i < localCloses.Length; i++)
            localCloses[i] = bars[startIdx + i].Close;

        var localEma = ComputeLocalEmaSeries(localCloses, period);

        // Compute slopes from localEma (slope = ema[i] - ema[i-1])
        var slopeStart = Math.Max(1, localEma.Length - windowNeeded);
        var slopes = new decimal[localEma.Length];
        for (var i = 1; i < localEma.Length; i++)
            slopes[i] = localEma[i] - localEma[i - 1];

        var slopeIdx = localEma.Length - 1; // maps to idx
        var direction = DetectInflection(slopes, slopeIdx, window, eps);
        if (direction == PositionSide.Flat) return null;

        // Entry bar direction filter: long entries need bullish bar, short need bearish
        if (direction == PositionSide.Long && bar.Close <= bar.Open) return null;
        if (direction == PositionSide.Short && bar.Close >= bar.Open) return null;

        // Check strong trend (optional confirmation for tight stop)
        var useTightStop = false;
        if (config.SI_UseTightStop)
        {
            useTightStop = IsStrongTrend(bars, idx, config.SI_StrongTrendLookback,
                config.SI_StrongTrendPct, direction);
        }

        // Calculate stop price
        decimal stopPrice;
        if (useTightStop)
        {
            // TightStop: Long → bar.Low - 1 tick, Short → bar.High + 1 tick
            stopPrice = direction == PositionSide.Long
                ? bar.Low - tick
                : bar.High + tick;
        }
        else
        {
            // Normal stop: use the prior swing extreme within the flat window
            // For long: lowest low of the flat window bars
            // For short: highest high of the flat window bars
            var windowStart = Math.Max(0, idx - window);
            if (direction == PositionSide.Long)
            {
                var lowestLow = bar.Low;
                for (var i = windowStart; i < idx; i++)
                    if (bars[i].Low < lowestLow) lowestLow = bars[i].Low;
                stopPrice = lowestLow - tick;
            }
            else
            {
                var highestHigh = bar.High;
                for (var i = windowStart; i < idx; i++)
                    if (bars[i].High > highestHigh) highestHigh = bars[i].High;
                stopPrice = highestHigh + tick;
            }
        }

        // Guard: stop distance must be <= MaxStopPoints and > 0
        var stopDistance = Math.Abs(bar.Close - stopPrice);
        if (stopDistance <= 0 || stopDistance > config.MaxStopPoints) return null;

        var stopMode = useTightStop ? "TightStop" : "NormalStop";
        var reason = $"[{Name}] {direction} inflection at {bar.Close:F2} ({stopMode})";

        return new StrategySignal(
            $"SI-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
            bar.CloseTimeUtc,
            direction,
            bar.Close,
            stopPrice,
            null, // No target price
            reason);
    }

    /// <summary>
    /// Reset EMA state (called implicitly when period changes or on first use).
    /// </summary>
    private static void UpdateEma(IReadOnlyList<MarketBar> bars, int period)
    {
        if (_emaPeriod != period || _emaBarCount == 0 || _emaBarCount > bars.Count)
        {
            // Reseed: compute full EMA from scratch
            _emaPeriod = period;
            _emaMultiplier = 2m / (period + 1);
            _runningEma = bars[0].Close;
            for (var i = 1; i < bars.Count; i++)
                _runningEma = (bars[i].Close - _runningEma) * _emaMultiplier + _runningEma;
            _prevEma = _runningEma; // approximation
            _emaBarCount = bars.Count;
        }
        else if (bars.Count > _emaBarCount)
        {
            // Incremental: just process the new bar
            _prevEma = _runningEma;
            _runningEma = (bars[^1].Close - _runningEma) * _emaMultiplier + _runningEma;
            _emaBarCount = bars.Count;
        }
    }

    /// <summary>
    /// Compute a local EMA series for a small slice of close prices.
    /// Used to get slope values over the inflection window.
    /// </summary>
    private static decimal[] ComputeLocalEmaSeries(decimal[] closes, int period)
    {
        var result = new decimal[closes.Length];
        if (closes.Length == 0) return result;

        var mult = 2m / (period + 1);
        result[0] = closes[0];
        for (var i = 1; i < closes.Length; i++)
            result[i] = (closes[i] - result[i - 1]) * mult + result[i - 1];

        return result;
    }

    /// <summary>
    /// FlatThenCross inflection detection with crossing check.
    ///
    /// LONG:  current slope > eps AND previous slope &lt;= eps (just crossed)
    ///        AND majority of W bars were flat/negative
    /// SHORT: current slope &lt; -eps AND previous slope >= -eps (just crossed)
    ///        AND majority of W bars were flat/positive
    /// </summary>
    private static PositionSide DetectInflection(decimal[] slopes, int idx, int window, decimal eps)
    {
        if (idx < 2) return PositionSide.Flat;

        var currentSlope = slopes[idx];
        var prevSlope = slopes[idx - 1];

        // LONG inflection: slope just crossed above +eps
        if (currentSlope > eps && prevSlope <= eps)
        {
            var flatOrNegCount = 0;
            for (var i = idx - window; i < idx; i++)
            {
                if (i >= 1 && slopes[i] <= eps) // flat or negative
                    flatOrNegCount++;
            }

            if (flatOrNegCount > window / 2) // majority
                return PositionSide.Long;
        }

        // SHORT inflection: slope just crossed below -eps
        if (currentSlope < -eps && prevSlope >= -eps)
        {
            var flatOrPosCount = 0;
            for (var i = idx - window; i < idx; i++)
            {
                if (i >= 1 && slopes[i] >= -eps) // flat or positive
                    flatOrPosCount++;
            }

            if (flatOrPosCount > window / 2) // majority
                return PositionSide.Short;
        }

        return PositionSide.Flat;
    }

    /// <summary>
    /// Check if the recent bars show a strong trend in the given direction.
    /// Strong trend = >= pct of bars are directional (bullish for longs, bearish for shorts).
    /// </summary>
    private static bool IsStrongTrend(
        IReadOnlyList<MarketBar> bars, int idx, int lookback, decimal pct, PositionSide direction)
    {
        var start = Math.Max(0, idx - lookback);
        var count = idx - start;
        if (count <= 0) return false;

        var directionalCount = 0;
        for (var i = start; i < idx; i++)
        {
            if (direction == PositionSide.Long && bars[i].Close > bars[i].Open)
                directionalCount++;
            else if (direction == PositionSide.Short && bars[i].Close < bars[i].Open)
                directionalCount++;
        }

        return (decimal)directionalCount / count >= pct;
    }
}
