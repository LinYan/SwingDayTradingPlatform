using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.Tests;

public class MonteCarloEngineTests
{
    private static BacktestTrade MakeTrade(decimal pnlDollars, int number)
    {
        return new BacktestTrade(
            number,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow,
            5000m, 5000m + pnlDollars / 50m,
            "Long", "Target",
            pnlDollars / 50m, pnlDollars, 0m)
        {
            RMultiple = pnlDollars / 100m,
            InitialStopDistance = 2m
        };
    }

    private static List<BacktestTrade> MakeMixedTrades()
    {
        return
        [
            MakeTrade(500, 1),
            MakeTrade(300, 2),
            MakeTrade(-200, 3),
            MakeTrade(400, 4),
            MakeTrade(-150, 5),
            MakeTrade(250, 6),
            MakeTrade(-100, 7),
            MakeTrade(350, 8),
            MakeTrade(-250, 9),
            MakeTrade(200, 10)
        ];
    }

    [Fact]
    public void Run_DeterministicSeed_ProducesSameResults()
    {
        var trades = MakeMixedTrades();
        var result1 = MonteCarloEngine.Run(trades, 25000m, 50m, 100, seed: 42);
        var result2 = MonteCarloEngine.Run(trades, 25000m, 50m, 100, seed: 42);

        Assert.Equal(result1.NetPnL.P50, result2.NetPnL.P50);
        Assert.Equal(result1.MaxDrawdown.P50, result2.MaxDrawdown.P50);
        Assert.Equal(result1.ProbabilityOfRuin, result2.ProbabilityOfRuin);
    }

    [Fact]
    public void Run_PercentileBands_InOrder()
    {
        var trades = MakeMixedTrades();
        var result = MonteCarloEngine.Run(trades, 25000m, 50m, 500, seed: 123);

        // P5 <= P25 <= P50 <= P75 <= P95
        Assert.True(result.NetPnL.P5 <= result.NetPnL.P25);
        Assert.True(result.NetPnL.P25 <= result.NetPnL.P50);
        Assert.True(result.NetPnL.P50 <= result.NetPnL.P75);
        Assert.True(result.NetPnL.P75 <= result.NetPnL.P95);
    }

    [Fact]
    public void Run_MaxDrawdown_NonNegative()
    {
        var trades = MakeMixedTrades();
        var result = MonteCarloEngine.Run(trades, 25000m, 50m, 200, seed: 99);

        Assert.True(result.MaxDrawdown.P5 >= 0);
        Assert.True(result.MaxDrawdownPct.P5 >= 0);
    }

    [Fact]
    public void Run_EmptyTrades_ReturnsZeros()
    {
        var result = MonteCarloEngine.Run([], 25000m, 50m, 100);

        Assert.Equal(0, result.OriginalTradeCount);
        Assert.Equal(0m, result.NetPnL.P50);
        Assert.Equal(0m, result.ProbabilityOfRuin);
    }

    [Fact]
    public void Run_AllWinners_LowRuinProbability()
    {
        var trades = Enumerable.Range(1, 20).Select(i => MakeTrade(200, i)).ToList();
        var result = MonteCarloEngine.Run(trades, 25000m, 50m, 500, seed: 42);

        Assert.Equal(0m, result.ProbabilityOfRuin);
        Assert.True(result.NetPnL.P50 > 0);
    }

    [Fact]
    public void Run_IterationCount_Matches()
    {
        var trades = MakeMixedTrades();
        var result = MonteCarloEngine.Run(trades, 25000m, 50m, 250, seed: 1);

        Assert.Equal(250, result.Iterations);
        Assert.Equal(trades.Count, result.OriginalTradeCount);
    }
}
