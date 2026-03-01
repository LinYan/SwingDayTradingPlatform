using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.Tests;

public class RMetricsTests
{
    private static BacktestTrade MakeTrade(decimal pnlPoints, decimal stopDistance, int number = 1)
    {
        var rMult = stopDistance > 0 ? pnlPoints / stopDistance : 0m;
        return new BacktestTrade(
            number,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow,
            5000m,
            5000m + pnlPoints,
            "Long",
            "Target",
            pnlPoints,
            pnlPoints * 50m,
            0m)
        {
            RMultiple = rMult,
            InitialStopDistance = stopDistance
        };
    }

    [Fact]
    public void ExpectancyR_AllWinners_PositiveExpectancy()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(4m, 2m, 1),   // +2R
            MakeTrade(6m, 3m, 2),   // +2R
            MakeTrade(3m, 1.5m, 3)  // +2R
        };

        var result = ResultCalculator.CalculateExpectancyR(trades);
        Assert.True(result > 0);
        Assert.Equal(2.0m, result); // All +2R, winRate=100% => 1.0 * 2.0 - 0 * 0 = 2.0
    }

    [Fact]
    public void ExpectancyR_MixedTrades_CorrectCalculation()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(4m, 2m, 1),   // +2R
            MakeTrade(-2m, 2m, 2),  // -1R
            MakeTrade(6m, 2m, 3),   // +3R
            MakeTrade(-2m, 2m, 4)   // -1R
        };

        var result = ResultCalculator.CalculateExpectancyR(trades);
        // WinRate=0.5, AvgWinR=2.5, LossRate=0.5, AvgLossR=1.0
        // Expectancy = 0.5 * 2.5 - 0.5 * 1.0 = 0.75
        Assert.Equal(0.75m, result);
    }

    [Fact]
    public void ExpectancyR_NoTrades_ReturnsZero()
    {
        var result = ResultCalculator.CalculateExpectancyR([]);
        Assert.Equal(0m, result);
    }

    [Fact]
    public void MaxDrawdownR_SimpleDrawdown()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(4m, 2m, 1),   // +2R (cumR=2)
            MakeTrade(-2m, 2m, 2),  // -1R (cumR=1, peak=2, DD=1)
            MakeTrade(-2m, 2m, 3),  // -1R (cumR=0, peak=2, DD=2)
            MakeTrade(4m, 2m, 4)    // +2R (cumR=2, peak=2, DD=0)
        };

        var result = ResultCalculator.CalculateMaxDrawdownR(trades);
        Assert.Equal(2.0m, result);
    }

    [Fact]
    public void MaxDrawdownR_NoTrades_ReturnsZero()
    {
        Assert.Equal(0m, ResultCalculator.CalculateMaxDrawdownR([]));
    }

    [Fact]
    public void MaxDrawdownR_AllWinners_ReturnsZero()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(4m, 2m, 1),
            MakeTrade(6m, 3m, 2)
        };

        Assert.Equal(0m, ResultCalculator.CalculateMaxDrawdownR(trades));
    }

    [Fact]
    public void RDistribution_CreatesCorrectBuckets()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(4m, 2m, 1),   // +2R
            MakeTrade(-2m, 2m, 2),  // -1R
            MakeTrade(1m, 2m, 3),   // +0.5R
            MakeTrade(-1m, 2m, 4),  // -0.5R
        };

        var dist = ResultCalculator.CalculateRDistribution(trades);
        Assert.NotEmpty(dist);

        // Each bucket should cover 0.5R range
        foreach (var bucket in dist)
        {
            Assert.Equal(0.5m, bucket.BucketMax - bucket.BucketMin);
        }

        // Total count should equal trade count
        Assert.Equal(trades.Count, dist.Sum(b => b.Count));
    }

    [Fact]
    public void RDistribution_EmptyTrades_ReturnsEmpty()
    {
        Assert.Empty(ResultCalculator.CalculateRDistribution([]));
    }

    [Fact]
    public void AvgRPerTrade_Correct()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(4m, 2m, 1),   // +2R
            MakeTrade(-2m, 2m, 2),  // -1R
        };

        var result = ResultCalculator.CalculateAvgRPerTrade(trades);
        Assert.Equal(0.5m, result); // (2 + -1) / 2 = 0.5
    }
}
