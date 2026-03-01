using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Strategy;

namespace SwingDayTradingPlatform.Tests;

public class PatternDetectorTests
{
    [Fact]
    public void UpdateSwingPoints_TooFewBars_NoSwings()
    {
        var bars = TestHelpers.GenerateBars(3);
        var swings = new List<SwingPoint>();
        PatternDetector.UpdateSwingPoints(bars, swings, 3);
        Assert.Empty(swings);
    }

    [Fact]
    public void UpdateSwingPoints_DetectsSwingHigh()
    {
        // Create bars where bar at index 3 has the highest high
        var bars = new List<MarketBar>
        {
            TestHelpers.MakeBar(100, 102, 98, 101, minutesOffset: 0),
            TestHelpers.MakeBar(101, 103, 99, 102, minutesOffset: 5),
            TestHelpers.MakeBar(102, 104, 100, 103, minutesOffset: 10),
            TestHelpers.MakeBar(103, 110, 101, 104, minutesOffset: 15), // swing high candidate
            TestHelpers.MakeBar(104, 106, 100, 103, minutesOffset: 20), // confirmation bar (lower high)
        };
        var swings = new List<SwingPoint>();
        // Call incrementally — candidate is bars.Count-2 = index 3 when 5 bars present
        PatternDetector.UpdateSwingPoints(bars, swings, 3);
        var highs = swings.Where(s => s.Type == SwingType.High).ToList();
        Assert.True(highs.Count > 0, "Should detect at least one swing high");
    }

    [Fact]
    public void UpdateSwingPoints_DetectsSwingLow()
    {
        // Create bars where bar at index 3 has the lowest low
        var bars = new List<MarketBar>
        {
            TestHelpers.MakeBar(105, 107, 103, 104, minutesOffset: 0),
            TestHelpers.MakeBar(104, 106, 102, 103, minutesOffset: 5),
            TestHelpers.MakeBar(103, 105, 101, 102, minutesOffset: 10),
            TestHelpers.MakeBar(102, 104, 90, 101, minutesOffset: 15), // swing low candidate
            TestHelpers.MakeBar(101, 103, 95, 102, minutesOffset: 20), // confirmation bar (higher low)
        };
        var swings = new List<SwingPoint>();
        // Call incrementally — candidate is bars.Count-2 = index 3 when 5 bars present
        PatternDetector.UpdateSwingPoints(bars, swings, 3);
        var lows = swings.Where(s => s.Type == SwingType.Low).ToList();
        Assert.True(lows.Count > 0, "Should detect at least one swing low");
    }

    [Fact]
    public void UpdateSwingPoints_NoDuplicates()
    {
        var bars = new List<MarketBar>
        {
            TestHelpers.MakeBar(100, 102, 98, 101, minutesOffset: 0),
            TestHelpers.MakeBar(101, 103, 99, 102, minutesOffset: 5),
            TestHelpers.MakeBar(102, 104, 100, 103, minutesOffset: 10),
            TestHelpers.MakeBar(103, 110, 101, 104, minutesOffset: 15),
            TestHelpers.MakeBar(104, 106, 100, 103, minutesOffset: 20),
            TestHelpers.MakeBar(103, 105, 99, 102, minutesOffset: 25),
        };
        var swings = new List<SwingPoint>();
        // Call twice - should not create duplicates
        PatternDetector.UpdateSwingPoints(bars, swings, 3);
        var count1 = swings.Count;
        PatternDetector.UpdateSwingPoints(bars, swings, 3);
        Assert.Equal(count1, swings.Count);
    }

    [Fact]
    public void UpdateSRLevels_ClustersNearbySwings()
    {
        var swings = new List<SwingPoint>
        {
            new(5, 5000m, SwingType.High),
            new(10, 5001m, SwingType.High), // within 2 pts tolerance
            new(15, 4900m, SwingType.Low),
        };
        var levels = new List<SRLevel>();
        PatternDetector.UpdateSRLevels(swings, levels, 2m); // tolerance = 2 pts

        // 5000 and 5001 should cluster together
        Assert.True(levels.Count <= 2);
        var clusterLevel = levels.FirstOrDefault(l => l.TouchCount >= 2);
        Assert.NotNull(clusterLevel);
        Assert.True(clusterLevel.Price > 4999m && clusterLevel.Price < 5002m);
    }

    [Fact]
    public void UpdateSRLevels_EmptySwings_EmptyLevels()
    {
        var levels = new List<SRLevel>();
        PatternDetector.UpdateSRLevels([], levels, 1m);
        Assert.Empty(levels);
    }

    [Fact]
    public void DetectBigMove_NoMove_ReturnsNull()
    {
        var bars = TestHelpers.GenerateBars(20, startPrice: 5000m, volatility: 1m);
        var result = PatternDetector.DetectBigMove(bars, 5m, 3.0m);
        Assert.Null(result); // range ~1 pt, threshold 15 pts
    }

    [Fact]
    public void DetectBigMove_TooFewBars_ReturnsNull()
    {
        var bars = TestHelpers.GenerateBars(3);
        Assert.Null(PatternDetector.DetectBigMove(bars, 5m, 3.0m));
    }

    [Fact]
    public void DetectBigMove_LargeMove_ReturnsInfo()
    {
        // Create a big uptrend
        var bars = TestHelpers.GenerateBars(10, startPrice: 5000m, stepSize: 5m, direction: 1, volatility: 2m);
        // Range = ~50 pts. ATR for 2pt volatility bars is ~3.2. Threshold = 3 * 3.2 = 9.6. Should detect.
        var result = PatternDetector.DetectBigMove(bars, 3m, 3.0m);
        Assert.NotNull(result);
        Assert.Equal(PositionSide.Long, result.MoveDirection);
    }

    [Fact]
    public void CheckBarBreakExit_Long_ExitsOnLowerLow()
    {
        var prevBar = TestHelpers.MakeBar(100, 105, 95, 102);
        var curBar = TestHelpers.MakeBar(102, 104, 93, 101, minutesOffset: 5); // Low 93 < prev Low 95
        Assert.True(PatternDetector.CheckBarBreakExit(curBar, prevBar, PositionSide.Long));
    }

    [Fact]
    public void CheckBarBreakExit_Long_NoExitOnHigherLow()
    {
        var prevBar = TestHelpers.MakeBar(100, 105, 95, 102);
        var curBar = TestHelpers.MakeBar(102, 104, 96, 101, minutesOffset: 5); // Low 96 > prev Low 95
        Assert.False(PatternDetector.CheckBarBreakExit(curBar, prevBar, PositionSide.Long));
    }

    [Fact]
    public void CheckBarBreakExit_Short_ExitsOnHigherHigh()
    {
        var prevBar = TestHelpers.MakeBar(100, 105, 95, 98);
        var curBar = TestHelpers.MakeBar(98, 107, 94, 99, minutesOffset: 5); // High 107 > prev High 105
        Assert.True(PatternDetector.CheckBarBreakExit(curBar, prevBar, PositionSide.Short));
    }

    [Fact]
    public void CheckBarBreakExit_Short_NoExitOnLowerHigh()
    {
        var prevBar = TestHelpers.MakeBar(100, 105, 95, 98);
        var curBar = TestHelpers.MakeBar(98, 103, 94, 99, minutesOffset: 5); // High 103 < prev High 105
        Assert.False(PatternDetector.CheckBarBreakExit(curBar, prevBar, PositionSide.Short));
    }

    [Fact]
    public void CheckBarBreakExit_Flat_AlwaysFalse()
    {
        var prevBar = TestHelpers.MakeBar(100, 110, 90, 100);
        var curBar = TestHelpers.MakeBar(100, 120, 80, 100, minutesOffset: 5);
        Assert.False(PatternDetector.CheckBarBreakExit(curBar, prevBar, PositionSide.Flat));
    }

    [Fact]
    public void CheckReversalBarExit_Long_ExitsOnBearishClose()
    {
        var bar = TestHelpers.MakeBar(102, 105, 99, 100); // Close 100 < Open 102
        Assert.True(PatternDetector.CheckReversalBarExit(bar, PositionSide.Long));
    }

    [Fact]
    public void CheckReversalBarExit_Long_NoExitOnBullishClose()
    {
        var bar = TestHelpers.MakeBar(100, 105, 99, 103); // Close 103 > Open 100
        Assert.False(PatternDetector.CheckReversalBarExit(bar, PositionSide.Long));
    }

    [Fact]
    public void CheckReversalBarExit_Short_ExitsOnBullishClose()
    {
        var bar = TestHelpers.MakeBar(100, 105, 99, 103); // Close 103 > Open 100
        Assert.True(PatternDetector.CheckReversalBarExit(bar, PositionSide.Short));
    }

    [Fact]
    public void CheckReversalBarExit_Flat_AlwaysFalse()
    {
        var bar = TestHelpers.MakeBar(100, 110, 90, 105);
        Assert.False(PatternDetector.CheckReversalBarExit(bar, PositionSide.Flat));
    }

    [Fact]
    public void IsMomentumBurst_BullishBurst_Detected()
    {
        // 4 consecutive bars with large bullish bodies
        var bars = new List<MarketBar>();
        for (var i = 0; i < 8; i++)
        {
            var open = 5000m + i * 10m;
            var close = open + 8m; // body = 8
            bars.Add(TestHelpers.MakeBar(open, close + 1, open - 1, close, minutesOffset: i * 5));
        }

        var atr = 3m; // body (8) > avgBodyRatio(1) * atr(3) = 3
        var result = PatternDetector.IsMomentumBurst(bars, atr, 1.0m, 4, 7, out var dir, out var stop, maxModerate: 1, moderateMinRatio: 0.4m);
        Assert.True(result);
        Assert.Equal(PositionSide.Long, dir);
    }

    [Fact]
    public void IsMomentumBurst_MixedBars_NotDetected()
    {
        var bars = new List<MarketBar>
        {
            TestHelpers.MakeBar(100, 108, 99, 107, minutesOffset: 0), // bullish
            TestHelpers.MakeBar(107, 108, 100, 101, minutesOffset: 5), // bearish
            TestHelpers.MakeBar(101, 109, 100, 108, minutesOffset: 10), // bullish
            TestHelpers.MakeBar(108, 109, 101, 102, minutesOffset: 15), // bearish
        };
        var result = PatternDetector.IsMomentumBurst(bars, 3m, 1.0m, 4, 3, out _, out _, maxModerate: 1, moderateMinRatio: 0.4m);
        Assert.False(result);
    }

    [Fact]
    public void IsHigherLow_RequiresMinimumTwoSwingLows()
    {
        var bars = TestHelpers.GenerateBars(10);
        var swings = new List<SwingPoint> { new(2, 4990m, SwingType.Low) };
        Assert.False(PatternDetector.IsHigherLow(bars, swings, 5000m, 5m, 8, out _));
    }

    [Fact]
    public void IsLowerHigh_RequiresMinimumTwoSwingHighs()
    {
        var bars = TestHelpers.GenerateBars(10);
        var swings = new List<SwingPoint> { new(2, 5010m, SwingType.High) };
        Assert.False(PatternDetector.IsLowerHigh(bars, swings, 5000m, 5m, 8, out _));
    }

    [Fact]
    public void IsReversalCandle_BullishPinBar_Detected()
    {
        // Bullish pin bar: long lower wick, tiny body at top
        var bar = TestHelpers.MakeBar(99.5m, 100m, 90m, 100m, minutesOffset: 5);
        var prevBar = TestHelpers.MakeBar(100, 102, 98, 99, minutesOffset: 0);
        Assert.True(PatternDetector.IsReversalCandle(bar, prevBar, PositionSide.Long));
    }

    [Fact]
    public void IsReversalCandle_BearishPinBar_Detected()
    {
        // Bearish pin bar: long upper wick, tiny body at bottom
        var bar = TestHelpers.MakeBar(100.5m, 110m, 100m, 100m, minutesOffset: 5);
        var prevBar = TestHelpers.MakeBar(100, 102, 98, 101, minutesOffset: 0);
        Assert.True(PatternDetector.IsReversalCandle(bar, prevBar, PositionSide.Short));
    }

    [Fact]
    public void IsReversalCandle_BullishEngulfing_Detected()
    {
        // Bearish previous bar, bullish engulfing current bar
        var prevBar = TestHelpers.MakeBar(102, 103, 98, 99, minutesOffset: 0);
        var bar = TestHelpers.MakeBar(98, 104, 97, 103, minutesOffset: 5);
        Assert.True(PatternDetector.IsReversalCandle(bar, prevBar, PositionSide.Long));
    }

    [Fact]
    public void IsReversalCandle_NormalBar_NotDetected()
    {
        // Normal bar, not a reversal pattern
        var prevBar = TestHelpers.MakeBar(100, 102, 98, 101, minutesOffset: 0);
        var bar = TestHelpers.MakeBar(101, 103, 99, 102, minutesOffset: 5);
        Assert.False(PatternDetector.IsReversalCandle(bar, prevBar, PositionSide.Long));
        Assert.False(PatternDetector.IsReversalCandle(bar, prevBar, PositionSide.Short));
    }
}
