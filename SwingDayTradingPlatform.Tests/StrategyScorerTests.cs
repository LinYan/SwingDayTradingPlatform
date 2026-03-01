using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.Tests;

public class StrategyScorerTests
{
    private static BacktestResult MakeResult(
        string name, decimal expectancyR, decimal profitFactor,
        int totalTrades, decimal winRate, decimal maxDrawdownR,
        List<MonthlyReturn>? monthlyReturns = null)
    {
        return new BacktestResult
        {
            Parameters = new BacktestParameters(),
            NetPnL = 1000m,
            GrossPnL = 1000m,
            TotalCommissions = 0m,
            ReturnPct = 4m,
            TotalTrades = totalTrades,
            WinningTrades = (int)(totalTrades * winRate / 100m),
            LosingTrades = totalTrades - (int)(totalTrades * winRate / 100m),
            FlattenedTrades = 0,
            WinRate = winRate,
            ProfitFactor = profitFactor,
            AvgWinPoints = 2m,
            AvgLossPoints = 1m,
            MaxDrawdown = 500m,
            MaxDrawdownPct = 2m,
            SharpeRatio = 1.5m,
            SortinoRatio = 2.0m,
            StartingCapital = 25000m,
            EndingCapital = 26000m,
            EquityCurve = [],
            DailyReturns = [new DailyReturn(DateOnly.FromDateTime(DateTime.Today), 100m, 2)],
            MonthlyReturns = monthlyReturns ?? [new MonthlyReturn(2024, 1, 500m, 2m), new MonthlyReturn(2024, 2, 500m, 2m)],
            Trades = [],
            StrategyName = name,
            ExpectancyR = expectancyR,
            AvgRPerTrade = expectancyR,
            MaxDrawdownR = maxDrawdownR,
            RDistribution = []
        };
    }

    [Fact]
    public void HighExpectancy_RanksHigher()
    {
        var results = new Dictionary<string, BacktestResult>
        {
            ["LowR"] = MakeResult("LowR", 0.2m, 1.5m, 50, 55m, 2m),
            ["HighR"] = MakeResult("HighR", 0.8m, 2.5m, 50, 60m, 1m),
        };

        var rankings = StrategyScorer.Rank(results);
        Assert.Equal(2, rankings.Count);
        Assert.Equal("HighR", rankings[0].StrategyName);
    }

    [Fact]
    public void InsufficientTrades_Excluded()
    {
        var results = new Dictionary<string, BacktestResult>
        {
            ["Enough"] = MakeResult("Enough", 0.5m, 2.0m, 50, 55m, 1m),
            ["TooFew"] = MakeResult("TooFew", 1.0m, 3.0m, 5, 80m, 0.5m),
        };

        var rankings = StrategyScorer.Rank(results);
        Assert.Single(rankings);
        Assert.Equal("Enough", rankings[0].StrategyName);
    }

    [Fact]
    public void CustomWeights_Respected()
    {
        var results = new Dictionary<string, BacktestResult>
        {
            ["A"] = MakeResult("A", 0.3m, 1.5m, 50, 55m, 0.5m),
            ["B"] = MakeResult("B", 0.5m, 1.0m, 50, 50m, 3.0m),
        };

        // Heavy drawdown penalty should favor A (lower DD)
        var weights = new ScoringWeights
        {
            ExpectancyWeight = 0.10m,
            DrawdownPenaltyWeight = 0.50m
        };

        var rankings = StrategyScorer.Rank(results, weights);
        Assert.Equal(2, rankings.Count);
        Assert.Equal("A", rankings[0].StrategyName);
    }

    [Fact]
    public void EmptyResults_ReturnsEmpty()
    {
        var rankings = StrategyScorer.Rank(new Dictionary<string, BacktestResult>());
        Assert.Empty(rankings);
    }

    [Fact]
    public void ScoreContainsCorrectMetrics()
    {
        var monthlyReturns = new List<MonthlyReturn>
        {
            new(2024, 1, 200m, 0.8m),
            new(2024, 2, -50m, -0.2m),
            new(2024, 3, 300m, 1.2m)
        };

        var results = new Dictionary<string, BacktestResult>
        {
            ["Test"] = MakeResult("Test", 0.5m, 2.0m, 30, 60m, 1.5m, monthlyReturns)
        };

        var rankings = StrategyScorer.Rank(results);
        Assert.Single(rankings);

        var rank = rankings[0];
        Assert.Equal("Test", rank.StrategyName);
        Assert.Equal(0.5m, rank.OosExpectancyR);
        Assert.Equal(2.0m, rank.OosProfitFactor);
        Assert.Equal(60m, rank.OosWinRate);
        Assert.Equal(1.5m, rank.OosMaxDrawdownR);
        // 2 out of 3 months profitable = 66.67%
        Assert.True(rank.PctProfitableMonths > 60m);
    }
}
