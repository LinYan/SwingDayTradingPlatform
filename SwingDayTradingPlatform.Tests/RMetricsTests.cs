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

    // --- Advanced Metrics Tests ---

    [Fact]
    public void CalmarRatio_PositiveCAGR_CorrectRatio()
    {
        var calmar = ResultCalculator.CalculateCalmarRatio(20m, 10m);
        Assert.Equal(2.0m, calmar); // 20/10 = 2
    }

    [Fact]
    public void CalmarRatio_ZeroDrawdown_ReturnsCapped()
    {
        var calmar = ResultCalculator.CalculateCalmarRatio(10m, 0m);
        Assert.Equal(999.99m, calmar);
    }

    [Fact]
    public void RecoveryFactor_Correct()
    {
        var rf = ResultCalculator.CalculateRecoveryFactor(5000m, 1000m);
        Assert.Equal(5.0m, rf);
    }

    [Fact]
    public void RecoveryFactor_ZeroDrawdown_ReturnsCapped()
    {
        var rf = ResultCalculator.CalculateRecoveryFactor(5000m, 0m);
        Assert.Equal(999.99m, rf);
    }

    [Fact]
    public void PayoffRatio_Correct()
    {
        var ratio = ResultCalculator.CalculatePayoffRatio(6m, 3m);
        Assert.Equal(2.0m, ratio);
    }

    [Fact]
    public void MaxConsecutiveWinsLosses_Correct()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(4m, 2m, 1),   // W
            MakeTrade(2m, 2m, 2),   // W
            MakeTrade(3m, 2m, 3),   // W (streak=3)
            MakeTrade(-2m, 2m, 4),  // L
            MakeTrade(-1m, 2m, 5),  // L (streak=2)
            MakeTrade(5m, 2m, 6),   // W
        };

        var (maxWins, maxLosses) = ResultCalculator.CalculateMaxConsecutiveWinsLosses(trades);
        Assert.Equal(3, maxWins);
        Assert.Equal(2, maxLosses);
    }

    [Fact]
    public void MaxConsecutiveWinsLosses_Empty_ReturnsZeros()
    {
        var (maxWins, maxLosses) = ResultCalculator.CalculateMaxConsecutiveWinsLosses([]);
        Assert.Equal(0, maxWins);
        Assert.Equal(0, maxLosses);
    }

    [Fact]
    public void HoldTimes_Correct()
    {
        var baseTime = new DateTimeOffset(2024, 6, 10, 14, 0, 0, TimeSpan.Zero);
        var t1 = new BacktestTrade(1, baseTime, baseTime.AddMinutes(30),
            5000, 5005, "Long", "Target", 5, 250, 0);
        var t2 = new BacktestTrade(2, baseTime, baseTime.AddMinutes(60),
            5000, 5003, "Long", "Target", 3, 150, 0);

        var (avg, max) = ResultCalculator.CalculateHoldTimes([t1, t2]);
        Assert.Equal(45m, avg); // (30 + 60) / 2
        Assert.Equal(60m, max);
    }

    [Fact]
    public void UlcerIndex_FlatEquity_ReturnsZero()
    {
        var curve = new List<EquityPoint>
        {
            new(DateTimeOffset.UtcNow.AddMinutes(-20), 25000m, 0),
            new(DateTimeOffset.UtcNow.AddMinutes(-10), 25000m, 0),
            new(DateTimeOffset.UtcNow, 25000m, 0),
        };

        var ui = ResultCalculator.CalculateUlcerIndex(curve);
        Assert.Equal(0m, ui);
    }

    [Fact]
    public void UlcerIndex_WithDrawdown_Positive()
    {
        var curve = new List<EquityPoint>
        {
            new(DateTimeOffset.UtcNow.AddMinutes(-20), 25000m, 0),
            new(DateTimeOffset.UtcNow.AddMinutes(-10), 24000m, 4m),
            new(DateTimeOffset.UtcNow, 25500m, 0),
        };

        var ui = ResultCalculator.CalculateUlcerIndex(curve);
        Assert.True(ui > 0);
    }

    [Fact]
    public void TailRatio_InsufficientTrades_ReturnsZero()
    {
        var trades = Enumerable.Range(1, 5).Select(i => MakeTrade(4m, 2m, i)).ToList();
        Assert.Equal(0m, ResultCalculator.CalculateTailRatio(trades));
    }

    [Fact]
    public void MfeEfficiency_CorrectCalculation()
    {
        var t1 = new BacktestTrade(1, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
            5000, 5005, "Long", "Target", 5, 250, 0) { MFE = 10m };
        var t2 = new BacktestTrade(2, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
            5000, 5003, "Long", "Target", 3, 150, 0) { MFE = 6m };

        var eff = ResultCalculator.CalculateMfeEfficiency([t1, t2]);
        Assert.Equal(0.5m, eff); // (5/10 + 3/6) / 2 = 0.5
    }

    [Fact]
    public void MaeRatio_CorrectCalculation()
    {
        var t1 = new BacktestTrade(1, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
            5000, 5005, "Long", "Target", 5, 250, 0)
            { MAE = 1.5m, InitialStopDistance = 3m };
        var t2 = new BacktestTrade(2, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
            5000, 5003, "Long", "Target", 3, 150, 0)
            { MAE = 1m, InitialStopDistance = 4m };

        var ratio = ResultCalculator.CalculateMaeRatio([t1, t2]);
        Assert.Equal(0.375m, ratio); // (1.5/3 + 1/4) / 2 = (0.5 + 0.25) / 2 = 0.375
    }

    [Fact]
    public void CAGR_PositiveGrowth_PositiveCAGR()
    {
        var dailyReturns = new List<DailyReturn>
        {
            new(new DateOnly(2023, 1, 1), 100m, 1),
            new(new DateOnly(2024, 1, 1), 100m, 1),
        };

        var cagr = ResultCalculator.CalculateCAGR(25000m, 30000m, dailyReturns);
        Assert.True(cagr > 0);
    }

    [Fact]
    public void CAGR_NoTrades_ReturnsZero()
    {
        Assert.Equal(0m, ResultCalculator.CalculateCAGR(25000m, 30000m, []));
    }
}
