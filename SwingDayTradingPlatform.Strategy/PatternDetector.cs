using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy;

public enum SwingType { High, Low }

public sealed record SwingPoint(int BarIndex, decimal Price, SwingType Type);

public sealed record SRLevel(decimal Price, int TouchCount, SwingType DominantType, int LatestTouchBarIndex = 0);

public sealed record BigMoveInfo(
    int StartIndex,
    int EndIndex,
    decimal MoveHigh,
    decimal MoveLow,
    PositionSide MoveDirection);

public static class PatternDetector
{
    /// <summary>
    /// Detect swing highs/lows using 3-bar left, 1-bar right confirmation.
    /// Appends new swing points found at the confirmation bar index.
    /// </summary>
    public static void UpdateSwingPoints(
        IReadOnlyList<MarketBar> bars,
        List<SwingPoint> swingPoints,
        int lookback)
    {
        if (bars.Count < lookback + 2)
            return;

        // Check the bar at index (count - 2) as the candidate, confirmed by (count - 1)
        var candidateIdx = bars.Count - 2;

        // Already detected this index?
        if (swingPoints.Count > 0 && swingPoints[^1].BarIndex >= candidateIdx)
            return;

        var candidate = bars[candidateIdx];

        // Swing high: candidate.High >= all bars in [candidateIdx-lookback .. candidateIdx+1]
        var isSwingHigh = true;
        var isSwingLow = true;

        for (var i = candidateIdx - lookback; i <= candidateIdx + 1; i++)
        {
            if (i < 0 || i >= bars.Count || i == candidateIdx)
                continue;
            if (bars[i].High >= candidate.High)
                isSwingHigh = false;
            if (bars[i].Low <= candidate.Low)
                isSwingLow = false;
        }

        if (isSwingHigh)
            swingPoints.Add(new SwingPoint(candidateIdx, candidate.High, SwingType.High));
        if (isSwingLow)
            swingPoints.Add(new SwingPoint(candidateIdx, candidate.Low, SwingType.Low));
    }

    /// <summary>
    /// Cluster nearby swing points into S/R levels.
    /// </summary>
    public static void UpdateSRLevels(
        IReadOnlyList<SwingPoint> swingPoints,
        List<SRLevel> levels,
        decimal clusterTolerance)
    {
        levels.Clear();
        if (swingPoints.Count == 0)
            return;

        // Sort by price for clustering
        var sorted = swingPoints.OrderBy(p => p.Price).ToList();
        var used = new bool[sorted.Count];

        for (var i = 0; i < sorted.Count; i++)
        {
            if (used[i]) continue;

            var cluster = new List<SwingPoint> { sorted[i] };
            used[i] = true;

            for (var j = i + 1; j < sorted.Count; j++)
            {
                if (used[j]) continue;
                if (Math.Abs(sorted[j].Price - sorted[i].Price) <= clusterTolerance)
                {
                    cluster.Add(sorted[j]);
                    used[j] = true;
                }
            }

            var avgPrice = cluster.Average(c => c.Price);
            var dominantType = cluster.Count(c => c.Type == SwingType.High) >= cluster.Count(c => c.Type == SwingType.Low)
                ? SwingType.High
                : SwingType.Low;
            var latestTouchBarIndex = cluster.Max(c => c.BarIndex);

            levels.Add(new SRLevel(avgPrice, cluster.Count, dominantType, latestTouchBarIndex));
        }
    }

    /// <summary>
    /// Detect a big move (range over recent bars exceeds factor * ATR).
    /// Scans back 5–20 bars from current position.
    /// </summary>
    public static BigMoveInfo? DetectBigMove(
        IReadOnlyList<MarketBar> bars,
        decimal atr,
        decimal factor)
    {
        if (bars.Count < 6 || atr <= 0)
            return null;

        var threshold = atr * factor;
        var lastIdx = bars.Count - 1;

        // Scan windows of size 5..20 bars ending at lastIdx
        for (var windowSize = 5; windowSize <= Math.Min(20, bars.Count - 1); windowSize++)
        {
            var startIdx = lastIdx - windowSize;
            if (startIdx < 0) break;

            var high = decimal.MinValue;
            var low = decimal.MaxValue;
            for (var i = startIdx; i <= lastIdx; i++)
            {
                if (bars[i].High > high) high = bars[i].High;
                if (bars[i].Low < low) low = bars[i].Low;
            }

            if (high - low >= threshold)
            {
                // Determine direction: compare start close to end close
                var direction = bars[lastIdx].Close > bars[startIdx].Close
                    ? PositionSide.Long   // rally (move up)
                    : PositionSide.Short; // drop (move down)

                return new BigMoveInfo(startIdx, lastIdx, high, low, direction);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true if the bar's body is at least <paramref name="minRatio"/> × ATR.
    /// </summary>
    public static bool HasMinimumBody(MarketBar bar, decimal atr, decimal minRatio)
        => Math.Abs(bar.Close - bar.Open) >= atr * minRatio;

    /// <summary>
    /// Detect a reversal candle (pin bar or engulfing pattern).
    /// Optionally rejects candles whose range is smaller than <paramref name="minRangeAtr"/> × ATR.
    /// </summary>
    public static bool IsReversalCandle(MarketBar bar, MarketBar prevBar, PositionSide direction, decimal atr = 0, decimal minRangeAtr = 0)
    {
        var body = Math.Abs(bar.Close - bar.Open);
        var range = bar.High - bar.Low;

        if (range <= 0) return false;

        // Reject tiny candles if min range is specified
        if (minRangeAtr > 0 && atr > 0 && range < minRangeAtr * atr)
            return false;

        if (direction == PositionSide.Long)
        {
            // Bullish pin bar: long lower wick, small body in upper third
            var lowerWick = Math.Min(bar.Open, bar.Close) - bar.Low;
            if (lowerWick >= range * 0.6m && body <= range * 0.3m)
                return true;

            // Bullish engulfing: current bar bullish and engulfs previous bearish bar
            if (bar.Close > bar.Open && prevBar.Close < prevBar.Open
                && bar.Close >= prevBar.Open && bar.Open <= prevBar.Close)
                return true;
        }
        else if (direction == PositionSide.Short)
        {
            // Bearish pin bar: long upper wick, small body in lower third
            var upperWick = bar.High - Math.Max(bar.Open, bar.Close);
            if (upperWick >= range * 0.6m && body <= range * 0.3m)
                return true;

            // Bearish engulfing: current bar bearish and engulfs previous bullish bar
            if (bar.Close < bar.Open && prevBar.Close > prevBar.Open
                && bar.Open >= prevBar.Close && bar.Close <= prevBar.Open)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check for Higher Low near EMA for long setup.
    /// Returns true if the most recent swing low is higher than the previous swing low,
    /// both near the EMA, and current bar confirms.
    /// </summary>
    public static bool IsHigherLow(
        IReadOnlyList<MarketBar> bars,
        IReadOnlyList<SwingPoint> swingPoints,
        decimal ema,
        decimal atr,
        int idx,
        out decimal swingLowPrice,
        decimal tolerance = 1.0m)
    {
        swingLowPrice = 0;
        var swingLows = swingPoints.Where(s => s.Type == SwingType.Low && s.BarIndex < idx).ToList();
        if (swingLows.Count < 2)
            return false;

        var latest = swingLows[^1];
        var previous = swingLows[^2];

        // Latest swing low must be higher than previous
        if (latest.Price <= previous.Price)
            return false;

        // Latest swing low should be near EMA (within tolerance * ATR)
        if (Math.Abs(latest.Price - ema) > atr * tolerance)
            return false;

        // Current bar must confirm: Low > swing low and Close > EMA
        var currentBar = bars[idx];
        if (currentBar.Low <= latest.Price)
            return false;
        if (currentBar.Close <= ema)
            return false;

        swingLowPrice = latest.Price;
        return true;
    }

    /// <summary>
    /// Check for Lower High near EMA for short setup.
    /// </summary>
    public static bool IsLowerHigh(
        IReadOnlyList<MarketBar> bars,
        IReadOnlyList<SwingPoint> swingPoints,
        decimal ema,
        decimal atr,
        int idx,
        out decimal swingHighPrice,
        decimal tolerance = 1.0m)
    {
        swingHighPrice = 0;
        var swingHighs = swingPoints.Where(s => s.Type == SwingType.High && s.BarIndex < idx).ToList();
        if (swingHighs.Count < 2)
            return false;

        var latest = swingHighs[^1];
        var previous = swingHighs[^2];

        // Latest swing high must be lower than previous
        if (latest.Price >= previous.Price)
            return false;

        // Latest swing high should be near EMA (within tolerance * ATR)
        if (Math.Abs(latest.Price - ema) > atr * tolerance)
            return false;

        // Current bar must confirm: High < swing high and Close < EMA
        var currentBar = bars[idx];
        if (currentBar.High >= latest.Price)
            return false;
        if (currentBar.Close >= ema)
            return false;

        swingHighPrice = latest.Price;
        return true;
    }

    /// <summary>
    /// Detect momentum burst: N consecutive bars same direction.
    /// Uses average body threshold with allowance for moderate bars.
    /// </summary>
    public static bool IsMomentumBurst(
        IReadOnlyList<MarketBar> bars,
        decimal atr,
        decimal avgBodyRatio,
        int count,
        int idx,
        out PositionSide direction,
        out decimal stopPrice,
        int maxModerate = 1,
        decimal moderateMinRatio = 0.4m)
    {
        direction = PositionSide.Flat;
        stopPrice = 0;

        if (idx < count - 1 || atr <= 0)
            return false;

        var bullish = true;
        var bearish = true;
        var lowestLow = decimal.MaxValue;
        var highestHigh = decimal.MinValue;
        var bodySum = 0m;
        var moderateCount = 0;
        var moderateThreshold = atr * moderateMinRatio;

        for (var i = idx - count + 1; i <= idx; i++)
        {
            var bar = bars[i];
            var body = Math.Abs(bar.Close - bar.Open);
            bodySum += body;

            // Reject if any bar body is below moderate threshold
            if (body < moderateThreshold)
                return false;

            // Count bars below the full average threshold as moderate
            if (body < atr * avgBodyRatio)
                moderateCount++;

            if (bar.Close <= bar.Open) bullish = false;
            if (bar.Close >= bar.Open) bearish = false;

            if (bar.Low < lowestLow) lowestLow = bar.Low;
            if (bar.High > highestHigh) highestHigh = bar.High;
        }

        // Reject if too many moderate bars
        if (moderateCount > maxModerate)
            return false;

        // Reject if average body is below threshold
        if (bodySum / count < atr * avgBodyRatio)
            return false;

        if (bullish)
        {
            direction = PositionSide.Long;
            stopPrice = lowestLow;
            return true;
        }

        if (bearish)
        {
            direction = PositionSide.Short;
            stopPrice = highestHigh;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Bar-break trailing exit: long exits when bar.Low &lt; prevBar.Low;
    /// short exits when bar.High &gt; prevBar.High.
    /// </summary>
    public static bool CheckBarBreakExit(MarketBar bar, MarketBar prevBar, PositionSide direction)
    {
        return direction switch
        {
            PositionSide.Long => bar.Low < prevBar.Low,
            PositionSide.Short => bar.High > prevBar.High,
            _ => false
        };
    }

    /// <summary>
    /// ABCD exit: long exits when bar is bearish AND closes below previous bar's low;
    /// short exits when bar is bullish AND closes above previous bar's high.
    /// </summary>
    public static bool CheckCloseBeyondPrevBarExit(MarketBar bar, MarketBar prevBar, PositionSide direction)
    {
        return direction == PositionSide.Long
            ? bar.Close < bar.Open && bar.Close < prevBar.Low
            : bar.Close > bar.Open && bar.Close > prevBar.High;
    }

    /// <summary>
    /// Reversal bar exit: long exits when bar closes below its open (bearish bar);
    /// short exits when bar closes above its open (bullish bar).
    /// </summary>
    public static bool CheckReversalBarExit(MarketBar bar, PositionSide direction)
    {
        return direction switch
        {
            PositionSide.Long => bar.Close < bar.Open,
            PositionSide.Short => bar.Close > bar.Open,
            _ => false
        };
    }
}
