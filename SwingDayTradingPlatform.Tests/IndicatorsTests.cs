using SwingDayTradingPlatform.Strategy;

namespace SwingDayTradingPlatform.Tests;

public class IndicatorsTests
{
    [Fact]
    public void Ema_EmptyList_ReturnsZero()
    {
        Assert.Equal(0m, Indicators.Ema([], 10));
    }

    [Fact]
    public void Ema_SingleValue_ReturnsThatValue()
    {
        Assert.Equal(100m, Indicators.Ema([100m], 10));
    }

    [Fact]
    public void Ema_ConstantValues_ReturnsConstant()
    {
        var values = Enumerable.Repeat(50m, 20).ToList();
        Assert.Equal(50m, Indicators.Ema(values, 10));
    }

    [Fact]
    public void Ema_AscendingValues_TrailsBehind()
    {
        // EMA of ascending series should be less than last value
        var values = Enumerable.Range(1, 30).Select(i => (decimal)i).ToList();
        var ema = Indicators.Ema(values, 10);
        Assert.True(ema < 30m, $"EMA {ema} should trail behind last value 30");
        Assert.True(ema > 20m, $"EMA {ema} should be reasonably close to recent values");
    }

    [Fact]
    public void Ema_ShorterPeriod_ReactsFaster()
    {
        var values = Enumerable.Repeat(50m, 20).Concat(Enumerable.Repeat(100m, 10)).ToList();
        var fastEma = Indicators.Ema(values, 5);
        var slowEma = Indicators.Ema(values, 20);
        Assert.True(fastEma > slowEma, $"Fast EMA {fastEma} should react faster to price jump than slow EMA {slowEma}");
    }

    [Fact]
    public void Atr_TooFewBars_ReturnsZero()
    {
        var bar = TestHelpers.MakeBar(100, 105, 95, 102);
        Assert.Equal(0m, Indicators.Atr([bar], 14));
    }

    [Fact]
    public void Atr_IdenticalBars_ReturnsZero()
    {
        var bars = Enumerable.Range(0, 20).Select(i =>
            TestHelpers.MakeBar(100, 100, 100, 100, minutesOffset: i * 5)).ToList();
        Assert.Equal(0m, Indicators.Atr(bars, 14));
    }

    [Fact]
    public void Atr_KnownVolatility_ReturnsExpected()
    {
        // Bars with H-L=10 and no gap should have ATR=10
        var bars = Enumerable.Range(0, 20).Select(i =>
            TestHelpers.MakeBar(100, 105, 95, 100, minutesOffset: i * 5)).ToList();
        var atr = Indicators.Atr(bars, 14);
        Assert.Equal(10m, atr);
    }

    [Fact]
    public void Atr_WithGaps_IncludesTrueRange()
    {
        // Bar 0: O100 H105 L95 C103 → range = 10
        // Bar 1: O107 H110 L102 C108 → TR = max(8, |110-103|=7, |102-103|=1) = 8
        var bars = new List<Shared.MarketBar>
        {
            TestHelpers.MakeBar(100, 105, 95, 103, minutesOffset: 0),
            TestHelpers.MakeBar(107, 110, 102, 108, minutesOffset: 5)
        };
        var atr = Indicators.Atr(bars, 14);
        Assert.Equal(8m, atr); // only 1 TR computed from bar pair
    }

    [Fact]
    public void EmaSeries_ReturnsOneValuePerBar()
    {
        var closes = Enumerable.Range(1, 10).Select(i => (decimal)i).ToList();
        var series = Indicators.EmaSeries(closes, 5);
        Assert.Equal(10, series.Count);
        Assert.Equal(1m, series[0]); // first value = first close
    }

    [Fact]
    public void EmaSeries_Empty_ReturnsEmpty()
    {
        var series = Indicators.EmaSeries([], 5);
        Assert.Empty(series);
    }

    [Fact]
    public void SessionVwap_ResetsOnNewDay()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var day1 = new DateTimeOffset(2024, 6, 10, 14, 0, 0, TimeSpan.Zero); // 10am ET
        var day2 = new DateTimeOffset(2024, 6, 11, 14, 0, 0, TimeSpan.Zero); // next day

        var bars = new List<Shared.MarketBar>
        {
            new(day1, day1.AddMinutes(5), 100, 110, 90, 105, 1000), // TP = (110+90+105)/3 = 101.67
            new(day2, day2.AddMinutes(5), 200, 210, 190, 205, 1000), // new day, TP = (210+190+205)/3 = 201.67
        };

        var vwaps = Indicators.SessionVwap(bars, tz);
        Assert.Equal(2, vwaps.Count);
        // Day 2 VWAP should be around 201.67 (fresh start), not averaged with day 1
        Assert.True(vwaps[1] > 190m, $"Day 2 VWAP {vwaps[1]} should reflect day 2 prices only");
    }

    [Fact]
    public void SessionVwap_ZeroVolume_UsesOne()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var time = new DateTimeOffset(2024, 6, 10, 14, 0, 0, TimeSpan.Zero);
        var bars = new List<Shared.MarketBar>
        {
            new(time, time.AddMinutes(5), 100, 110, 90, 100, 0), // zero volume
        };

        var vwaps = Indicators.SessionVwap(bars, tz);
        Assert.Equal(1, vwaps.Count);
        Assert.True(vwaps[0] > 0); // should still produce a value
    }
}
