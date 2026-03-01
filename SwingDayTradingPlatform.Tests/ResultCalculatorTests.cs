using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.Tests;

public class ResultCalculatorTests
{
    private static BacktestConfig DefaultConfig() => new()
    {
        StartingCapital = 25000m,
        CommissionPerTrade = 2.50m,
        SlippagePoints = 0.25m,
        PointValue = 50m,
        Timezone = "America/New_York"
    };

    private static BacktestParameters DefaultParams() => new();

    private static BacktestTrade MakeTrade(
        decimal entryPrice, decimal exitPrice, string direction, string exitReason,
        int tradeNumber = 1, int dayOffset = 0)
    {
        var entryTime = new DateTimeOffset(2024, 6, 10 + dayOffset, 10, 0, 0, TimeSpan.FromHours(-4));
        var exitTime = entryTime.AddHours(2);
        var pnlPoints = direction == "Long" ? exitPrice - entryPrice : entryPrice - exitPrice;
        var pnlDollars = pnlPoints * 50m; // PointValue = 50
        return new BacktestTrade(tradeNumber, entryTime, exitTime, entryPrice, exitPrice,
            direction, exitReason, pnlPoints, pnlDollars, 5m); // $5 commission
    }

    [Fact]
    public void Calculate_NoTrades_ZeroResults()
    {
        var config = DefaultConfig();
        var result = ResultCalculator.Calculate(
            DefaultParams(), config, [], [new EquityPoint(DateTimeOffset.UtcNow, 25000m, 0m)]);

        Assert.Equal(0m, result.NetPnL);
        Assert.Equal(0, result.TotalTrades);
        Assert.Equal(0m, result.WinRate);
        Assert.Equal(25000m, result.EndingCapital);
    }

    [Fact]
    public void Calculate_WinningTrade_CorrectPnL()
    {
        var config = DefaultConfig();
        var trade = MakeTrade(5000, 5010, "Long", "StopLoss"); // +10 pts = +$500
        var equityCurve = new List<EquityPoint>
        {
            new(DateTimeOffset.UtcNow, 25000m, 0m),
            new(DateTimeOffset.UtcNow.AddHours(1), 25500m, 0m)
        };

        var result = ResultCalculator.Calculate(DefaultParams(), config, [trade], equityCurve);

        Assert.Equal(1, result.WinningTrades);
        Assert.Equal(0, result.LosingTrades);
        Assert.Equal(100m, result.WinRate);
        Assert.True(result.GrossPnL > 0);
    }

    [Fact]
    public void Calculate_LosingTrade_CorrectPnL()
    {
        var trade = MakeTrade(5000, 4990, "Long", "StopLoss"); // -10 pts = -$500
        var config = DefaultConfig();
        var equityCurve = new List<EquityPoint>
        {
            new(DateTimeOffset.UtcNow, 25000m, 0m),
            new(DateTimeOffset.UtcNow.AddHours(1), 24500m, 2m)
        };

        var result = ResultCalculator.Calculate(DefaultParams(), config, [trade], equityCurve);

        Assert.Equal(0, result.WinningTrades);
        Assert.Equal(1, result.LosingTrades);
        Assert.Equal(0m, result.WinRate);
    }

    [Fact]
    public void Calculate_MixedTrades_WinRateCorrect()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5000, 5010, "Long", "Target", 1, 0),   // win
            MakeTrade(5000, 4990, "Long", "StopLoss", 2, 1),  // loss
            MakeTrade(5000, 5005, "Long", "Flatten", 3, 2),   // win (small)
        };
        var config = DefaultConfig();
        var equityCurve = new List<EquityPoint> { new(DateTimeOffset.UtcNow, 25000m, 0m) };

        var result = ResultCalculator.Calculate(DefaultParams(), config, trades, equityCurve);

        Assert.Equal(3, result.TotalTrades);
        Assert.Equal(2, result.WinningTrades);
        Assert.Equal(1, result.LosingTrades);
        Assert.Equal(1, result.FlattenedTrades);
        // Win rate = 2/3 * 100 ≈ 66.67
        Assert.True(result.WinRate > 66m && result.WinRate < 67m);
    }

    [Fact]
    public void Calculate_ProfitFactor_Correct()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5000, 5010, "Long", "Target", 1, 0),   // +$500
            MakeTrade(5000, 4995, "Long", "StopLoss", 2, 1),  // -$250
        };
        var config = DefaultConfig();
        var equityCurve = new List<EquityPoint> { new(DateTimeOffset.UtcNow, 25000m, 0m) };

        var result = ResultCalculator.Calculate(DefaultParams(), config, trades, equityCurve);

        Assert.True(result.ProfitFactor > 1.5m); // 500/250 = 2.0
    }

    [Fact]
    public void Calculate_MaxDrawdown_Correct()
    {
        var equityCurve = new List<EquityPoint>
        {
            new(DateTimeOffset.UtcNow, 25000m, 0m),
            new(DateTimeOffset.UtcNow.AddHours(1), 26000m, 0m), // peak
            new(DateTimeOffset.UtcNow.AddHours(2), 24500m, 5.77m), // drawdown = 1500
            new(DateTimeOffset.UtcNow.AddHours(3), 25500m, 1.92m),
        };

        var result = ResultCalculator.Calculate(DefaultParams(), DefaultConfig(), [], equityCurve);

        Assert.Equal(1500m, result.MaxDrawdown);
        // 1500 / 26000 * 100 ≈ 5.77%
        Assert.True(result.MaxDrawdownPct > 5.7m && result.MaxDrawdownPct < 5.8m);
    }

    [Fact]
    public void CalculateDailyReturns_GroupsByDate()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5000, 5010, "Long", "Target", 1, 0),
            MakeTrade(5010, 5020, "Long", "Target", 2, 0), // same day
            MakeTrade(5000, 4990, "Long", "StopLoss", 3, 1), // different day
        };

        var returns = ResultCalculator.CalculateDailyReturns(trades, DefaultConfig());

        Assert.Equal(2, returns.Count); // 2 different days
        Assert.Equal(2, returns[0].TradeCount); // Day 1 has 2 trades
        Assert.Equal(1, returns[1].TradeCount); // Day 2 has 1 trade
    }

    [Fact]
    public void CalculateMonthlyReturns_GroupsByMonth()
    {
        var dailyReturns = new List<DailyReturn>
        {
            new(new DateOnly(2024, 6, 10), 500m, 2),
            new(new DateOnly(2024, 6, 11), -200m, 1),
            new(new DateOnly(2024, 7, 1), 300m, 1),
        };

        var monthly = ResultCalculator.CalculateMonthlyReturns(dailyReturns, 25000m);

        Assert.Equal(2, monthly.Count);
        Assert.Equal(6, monthly[0].Month);
        Assert.Equal(300m, monthly[0].PnL); // 500 - 200
        Assert.Equal(7, monthly[1].Month);
    }

    [Fact]
    public void Calculate_Sharpe_ZeroForSingleTrade()
    {
        var trade = MakeTrade(5000, 5010, "Long", "Target");
        var config = DefaultConfig();
        var equityCurve = new List<EquityPoint> { new(DateTimeOffset.UtcNow, 25000m, 0m) };

        var result = ResultCalculator.Calculate(DefaultParams(), config, [trade], equityCurve);

        Assert.Equal(0m, result.SharpeRatio); // < 2 daily returns
    }

    [Fact]
    public void Calculate_CommissionsSubtracted()
    {
        var trade = MakeTrade(5000, 5000, "Long", "Flatten"); // 0 PnL points, but $5 commission
        var config = DefaultConfig();
        var equityCurve = new List<EquityPoint> { new(DateTimeOffset.UtcNow, 25000m, 0m) };

        var result = ResultCalculator.Calculate(DefaultParams(), config, [trade], equityCurve);

        Assert.True(result.TotalCommissions > 0);
        Assert.True(result.NetPnL < result.GrossPnL);
    }

    [Fact]
    public void Calculate_AllWins_ProfitFactor999()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5000, 5010, "Long", "Target", 1, 0),
            MakeTrade(5000, 5005, "Long", "Target", 2, 1),
        };
        var config = DefaultConfig();
        var equityCurve = new List<EquityPoint> { new(DateTimeOffset.UtcNow, 25000m, 0m) };

        var result = ResultCalculator.Calculate(DefaultParams(), config, trades, equityCurve);

        Assert.Equal(999.99m, result.ProfitFactor);
    }

    [Fact]
    public void Calculate_WinLoss_UsesNetPnL()
    {
        // Trade with positive gross PnL ($50) but high commission ($100) → net loss
        var entryTime = new DateTimeOffset(2024, 6, 10, 10, 0, 0, TimeSpan.FromHours(-4));
        var exitTime = entryTime.AddHours(2);
        var trade = new BacktestTrade(1, entryTime, exitTime, 5000m, 5001m,
            "Long", "Target", 1m, 50m, 100m); // pnlDollars=50, commission=100 → net = -50

        var config = DefaultConfig();
        var equityCurve = new List<EquityPoint> { new(DateTimeOffset.UtcNow, 25000m, 0m) };

        var result = ResultCalculator.Calculate(DefaultParams(), config, [trade], equityCurve);

        // Net PnL is negative (50 - 100 = -50), so this should be classified as a loss
        Assert.Equal(0, result.WinningTrades);
        Assert.Equal(1, result.LosingTrades);
    }

    [Fact]
    public void CalculateSharpe_UsesSampleStdDev()
    {
        // Create trades on different days to get multiple daily returns
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5000, 5010, "Long", "Target", 1, 0),  // +$500
            MakeTrade(5000, 4990, "Long", "StopLoss", 2, 1), // -$500
            MakeTrade(5000, 5005, "Long", "Target", 3, 2),   // +$250
        };

        var config = DefaultConfig();
        var equityCurve = new List<EquityPoint> { new(DateTimeOffset.UtcNow, 25000m, 0m) };

        var result = ResultCalculator.Calculate(DefaultParams(), config, trades, equityCurve);

        // With 3 daily returns using sample stddev (N-1), Sharpe should be non-zero and finite
        Assert.True(double.IsFinite((double)result.SharpeRatio));
    }
}
