using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.Tests;

public class TradeAnalyticsTests
{
    private static BacktestTrade MakeTrade(
        decimal pnlPoints, string exitReason, int number,
        int entryHour = 10, DayOfWeek day = DayOfWeek.Monday,
        int holdMinutes = 15, string strategy = "EmaPullback")
    {
        // June 10, 2024 is a Monday. Offset by day-of-week to get the correct day.
        var monday = new DateTimeOffset(2024, 6, 10, entryHour, 0, 0, TimeSpan.Zero);
        var entryTime = monday.AddDays((int)day - (int)DayOfWeek.Monday);
        var exitTime = entryTime.AddMinutes(holdMinutes);
        return new BacktestTrade(
            number, entryTime, exitTime,
            5000m, 5000m + pnlPoints, "Long", exitReason,
            pnlPoints, pnlPoints * 50m, 0m)
        {
            StrategyName = strategy,
            MAE = Math.Abs(Math.Min(pnlPoints, 0)),
            MFE = Math.Max(pnlPoints, 0) + 2m,
            InitialStopDistance = 3m,
            RMultiple = pnlPoints / 3m
        };
    }

    [Fact]
    public void ByHourOfDay_GroupsByHour()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5, "Target", 1, entryHour: 10),
            MakeTrade(-3, "StopLoss", 2, entryHour: 10),
            MakeTrade(4, "Target", 3, entryHour: 14)
        };

        var result = TradeAnalytics.ByHourOfDay(trades, "UTC");
        Assert.Equal(2, result.Count);

        var hour10 = result.First(h => h.Hour == 10);
        Assert.Equal(2, hour10.TradeCount);
        Assert.Equal(1, hour10.Wins);
        Assert.Equal(1, hour10.Losses);
    }

    [Fact]
    public void ByHourOfDay_EmptyTrades_ReturnsEmpty()
    {
        Assert.Empty(TradeAnalytics.ByHourOfDay([]));
    }

    [Fact]
    public void ByDayOfWeek_GroupsByDay()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5, "Target", 1, day: DayOfWeek.Monday),
            MakeTrade(3, "Target", 2, day: DayOfWeek.Monday),
            MakeTrade(-2, "StopLoss", 3, day: DayOfWeek.Wednesday)
        };

        var result = TradeAnalytics.ByDayOfWeek(trades, "UTC");
        Assert.Equal(2, result.Count);

        var monday = result.First(d => d.Day == DayOfWeek.Monday);
        Assert.Equal(2, monday.TradeCount);
        Assert.True(monday.PnL > 0);
    }

    [Fact]
    public void HoldTimeDistribution_CreatesCorrectBuckets()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5, "Target", 1, holdMinutes: 3),
            MakeTrade(3, "Target", 2, holdMinutes: 10),
            MakeTrade(-2, "StopLoss", 3, holdMinutes: 45),
            MakeTrade(1, "Target", 4, holdMinutes: 90)
        };

        var result = TradeAnalytics.HoldTimeDistribution(trades);
        Assert.True(result.Count >= 3);
        Assert.Equal(trades.Count, result.Sum(b => b.Count));
    }

    [Fact]
    public void AnalyzeStreaks_CorrectMaxStreaks()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5, "Target", 1),   // W
            MakeTrade(3, "Target", 2),   // W
            MakeTrade(2, "Target", 3),   // W  (streak=3)
            MakeTrade(-1, "StopLoss", 4), // L
            MakeTrade(-2, "StopLoss", 5), // L  (streak=2)
            MakeTrade(4, "Target", 6),   // W
        };

        var result = TradeAnalytics.AnalyzeStreaks(trades);
        Assert.Equal(3, result.MaxWinStreak);
        Assert.Equal(2, result.MaxLossStreak);
        Assert.True(result.TotalWinStreaks >= 1);
        Assert.True(result.TotalLossStreaks >= 1);
    }

    [Fact]
    public void AnalyzeStreaks_EmptyTrades_ReturnsZeros()
    {
        var result = TradeAnalytics.AnalyzeStreaks([]);
        Assert.Equal(0, result.MaxWinStreak);
        Assert.Equal(0, result.MaxLossStreak);
    }

    [Fact]
    public void ByExitReason_GroupsCorrectly()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5, "Target", 1),
            MakeTrade(3, "Target", 2),
            MakeTrade(-2, "StopLoss", 3),
            MakeTrade(-1, "Flatten", 4)
        };

        var result = TradeAnalytics.ByExitReason(trades);
        Assert.Equal(3, result.Count);

        var targets = result.First(e => e.ExitReason == "Target");
        Assert.Equal(2, targets.Count);
        Assert.Equal(100m, targets.WinRate);
    }

    [Fact]
    public void MaeMfeAnalysis_GroupsByStrategy()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade(5, "Target", 1, strategy: "EmaPullback"),
            MakeTrade(3, "Target", 2, strategy: "BrooksPA")
        };

        var result = TradeAnalytics.MaeMfeAnalysis(trades);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void CompareStrategies_ProducesComparison()
    {
        var results = new Dictionary<string, BacktestResult>
        {
            ["A"] = MakeResult(100, 60, 1.5m),
            ["B"] = MakeResult(80, 55, 1.2m)
        };

        var comparison = TradeAnalytics.CompareStrategies(results);
        Assert.Equal(2, comparison.Count);
        Assert.Equal("A", comparison[0].StrategyName); // higher ExpR first
    }

    private static BacktestResult MakeResult(int trades, decimal winRate, decimal pf)
    {
        return new BacktestResult
        {
            Parameters = new BacktestParameters(),
            NetPnL = 5000m,
            GrossPnL = 5000m,
            TotalCommissions = 0m,
            ReturnPct = 20m,
            TotalTrades = trades,
            WinningTrades = (int)(trades * winRate / 100),
            LosingTrades = trades - (int)(trades * winRate / 100),
            FlattenedTrades = 0,
            WinRate = winRate,
            ProfitFactor = pf,
            AvgWinPoints = 5m,
            AvgLossPoints = 3m,
            MaxDrawdown = 1000m,
            MaxDrawdownPct = 4m,
            SharpeRatio = 1.5m,
            SortinoRatio = 2.0m,
            StartingCapital = 25000m,
            EndingCapital = 30000m,
            EquityCurve = [],
            DailyReturns = [],
            MonthlyReturns = [],
            Trades = [],
            ExpectancyR = pf * 0.5m,
            CalmarRatio = 1.0m,
            AvgHoldTimeMinutes = 30m
        };
    }
}
