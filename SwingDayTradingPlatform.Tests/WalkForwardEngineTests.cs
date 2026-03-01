using SwingDayTradingPlatform.Backtesting;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Tests;

public class WalkForwardEngineTests
{
    [Fact]
    public void GenerateFolds_ProducesCorrectFolds()
    {
        // Create bars spanning 12 months
        var bars = new List<MarketBar>();
        var start = new DateTimeOffset(2023, 1, 2, 14, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 252 * 2; i++) // ~2 years of trading days
        {
            var time = start.AddDays(i);
            if (time.DayOfWeek == DayOfWeek.Saturday || time.DayOfWeek == DayOfWeek.Sunday)
                continue;
            bars.Add(new MarketBar(time, time.AddMinutes(5), 5000, 5005, 4995, 5002, 1000));
        }

        var config = new WalkForwardEngine.WalkForwardConfig
        {
            TrainMonths = 6,
            TestMonths = 1,
            StepMonths = 1
        };

        var tz = TimeZoneInfo.Utc;
        var folds = WalkForwardEngine.GenerateFolds(bars, config, tz);

        Assert.NotEmpty(folds);

        // Each fold should have train period before test period
        foreach (var fold in folds)
        {
            Assert.True(fold.TrainStart < fold.TrainEnd);
            Assert.True(fold.TestStart < fold.TestEnd);
            Assert.Equal(fold.TrainEnd, fold.TestStart);
        }
    }

    [Fact]
    public void GenerateFolds_ShortData_ReturnsEmpty()
    {
        // Only 3 months of data with 6-month train window
        var bars = new List<MarketBar>();
        var start = new DateTimeOffset(2023, 1, 2, 14, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 60; i++) // ~3 months
        {
            var time = start.AddDays(i);
            bars.Add(new MarketBar(time, time.AddMinutes(5), 5000, 5005, 4995, 5002, 1000));
        }

        var config = new WalkForwardEngine.WalkForwardConfig
        {
            TrainMonths = 6,
            TestMonths = 1,
            StepMonths = 1
        };

        var folds = WalkForwardEngine.GenerateFolds(bars, config, TimeZoneInfo.Utc);
        Assert.Empty(folds);
    }

    [Fact]
    public void GenerateFolds_AtLeastOneFold()
    {
        var bars = new List<MarketBar>();
        var start = new DateTimeOffset(2023, 1, 2, 14, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 252; i++) // 1 year
        {
            var time = start.AddDays(i);
            if (time.DayOfWeek == DayOfWeek.Saturday || time.DayOfWeek == DayOfWeek.Sunday)
                continue;
            bars.Add(new MarketBar(time, time.AddMinutes(5), 5000, 5005, 4995, 5002, 1000));
        }

        var config = new WalkForwardEngine.WalkForwardConfig
        {
            TrainMonths = 6,
            TestMonths = 1,
            StepMonths = 1
        };

        var folds = WalkForwardEngine.GenerateFolds(bars, config, TimeZoneInfo.Utc);
        Assert.True(folds.Count >= 1, "Should generate at least one fold from 1 year of data");
    }

    [Fact]
    public void GenerateFolds_SteppingProducesOverlap()
    {
        var bars = new List<MarketBar>();
        var start = new DateTimeOffset(2023, 1, 2, 14, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 500; i++)
        {
            var time = start.AddDays(i);
            if (time.DayOfWeek == DayOfWeek.Saturday || time.DayOfWeek == DayOfWeek.Sunday)
                continue;
            bars.Add(new MarketBar(time, time.AddMinutes(5), 5000, 5005, 4995, 5002, 1000));
        }

        var config = new WalkForwardEngine.WalkForwardConfig
        {
            TrainMonths = 6,
            TestMonths = 1,
            StepMonths = 1
        };

        var folds = WalkForwardEngine.GenerateFolds(bars, config, TimeZoneInfo.Utc);
        if (folds.Count >= 2)
        {
            // Step is 1 month, so consecutive folds should overlap in training
            Assert.True(folds[1].TrainStart > folds[0].TrainStart);
            Assert.True(folds[1].TrainStart < folds[0].TrainEnd);
        }
    }
}
