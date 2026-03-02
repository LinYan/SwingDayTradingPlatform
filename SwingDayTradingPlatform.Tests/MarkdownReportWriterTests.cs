using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.Tests;

public class MarkdownReportWriterTests
{
    private static BacktestResult MakeResult(string name, decimal netPnL = 5000m, decimal expectancyR = 0.75m,
        decimal maxDDPct = 5m, decimal pf = 1.5m, int trades = 50)
    {
        return new BacktestResult
        {
            Parameters = new BacktestParameters(),
            NetPnL = netPnL,
            GrossPnL = netPnL,
            TotalCommissions = 0m,
            ReturnPct = netPnL / 250m,
            TotalTrades = trades,
            WinningTrades = (int)(trades * 0.6),
            LosingTrades = trades - (int)(trades * 0.6),
            FlattenedTrades = 0,
            WinRate = 60m,
            ProfitFactor = pf,
            AvgWinPoints = 5m,
            AvgLossPoints = 3m,
            MaxDrawdown = 1000m,
            MaxDrawdownPct = maxDDPct,
            SharpeRatio = 1.5m,
            SortinoRatio = 2.0m,
            StartingCapital = 25000m,
            EndingCapital = 25000m + netPnL,
            EquityCurve = [],
            DailyReturns = [],
            MonthlyReturns = [new MonthlyReturn(2024, 1, 500m, 2m), new MonthlyReturn(2024, 2, -200m, -0.8m)],
            Trades = [],
            StrategyName = name,
            ExpectancyR = expectancyR,
            CalmarRatio = 1.5m,
            RecoveryFactor = 5m,
            CAGR = 20m,
            PayoffRatio = 1.67m,
            UlcerIndex = 2.5m,
            TailRatio = 1.2m,
            MfeEfficiency = 0.6m,
            MaeRatio = 0.4m,
            MaxConsecutiveWins = 5,
            MaxConsecutiveLosses = 3,
            AvgHoldTimeMinutes = 25m,
            MaxHoldTimeMinutes = 90m,
            RDistribution = [new RDistributionBucket(-1m, -0.5m, 10, 20m), new RDistributionBucket(0.5m, 1m, 15, 30m)]
        };
    }

    [Fact]
    public void Generate_AllNulls_ReturnsHeader()
    {
        var report = MarkdownReportWriter.Generate();
        Assert.Contains("Quantitative Trading Research Report", report);
        Assert.Contains("Generated:", report);
    }

    [Fact]
    public void Generate_WithStrategyResults_ContainsDetails()
    {
        var results = new Dictionary<string, BacktestResult>
        {
            ["EmaPullback"] = MakeResult("EmaPullback"),
            ["BrooksPA"] = MakeResult("BrooksPA", 3000m, 0.5m)
        };

        var report = MarkdownReportWriter.Generate(strategyResults: results);

        Assert.Contains("Executive Summary", report);
        Assert.Contains("Strategy Details", report);
        Assert.Contains("Advanced Metrics", report);
        Assert.Contains("EmaPullback", report);
        Assert.Contains("BrooksPA", report);
        Assert.Contains("CAGR", report);
        Assert.Contains("Calmar", report);
    }

    [Fact]
    public void Generate_ExecutiveSummary_IdentifiesBestAndWorst()
    {
        var results = new Dictionary<string, BacktestResult>
        {
            ["A"] = MakeResult("A", netPnL: 10000m, expectancyR: 1.5m, maxDDPct: 3m),
            ["B"] = MakeResult("B", netPnL: 2000m, expectancyR: 0.3m, maxDDPct: 15m)
        };

        var report = MarkdownReportWriter.Generate(strategyResults: results);

        Assert.Contains("Best strategy (ExpR):** A", report);
        Assert.Contains("Best strategy (PnL):** A", report);
        Assert.Contains("Highest drawdown:** B", report);
    }

    [Fact]
    public void Generate_HighDrawdown_ShowsWarning()
    {
        var results = new Dictionary<string, BacktestResult>
        {
            ["Risky"] = MakeResult("Risky", maxDDPct: 25m)
        };

        var report = MarkdownReportWriter.Generate(strategyResults: results);
        Assert.Contains("WARNING", report);
        Assert.Contains("exceeds 20%", report);
    }

    [Fact]
    public void Generate_LosingStrategy_ShowsWarning()
    {
        var results = new Dictionary<string, BacktestResult>
        {
            ["Loser"] = MakeResult("Loser", pf: 0.8m)
        };

        var report = MarkdownReportWriter.Generate(strategyResults: results);
        Assert.Contains("WARNING", report);
        Assert.Contains("PF < 1.0", report);
    }

    [Fact]
    public void Generate_WithRankings_ContainsRankTable()
    {
        var results = new Dictionary<string, BacktestResult>
        {
            ["A"] = MakeResult("A")
        };
        var rankings = new List<StrategyRankResult>
        {
            new("A", 1.5m, 0.75m, 2.0m, 1.5m, 60m, 80m, 0.5m, 1.2m, results["A"])
        };

        var report = MarkdownReportWriter.Generate(strategyResults: results, rankings: rankings);
        Assert.Contains("Strategy Rankings", report);
        Assert.Contains("Top 3", report);
    }

    [Fact]
    public void Generate_WithMonteCarlo_ContainsCITable()
    {
        var mc = new Dictionary<string, MonteCarloResult>
        {
            ["Test"] = new MonteCarloResult(
                1000, 50,
                new PercentileBand("Net PnL", -1000, 2000, 5000, 8000, 12000),
                new PercentileBand("Max DD $", 200, 500, 1000, 2000, 3000),
                new PercentileBand("Max DD %", 1, 2, 4, 8, 12),
                new PercentileBand("Sharpe", 0.5m, 1.0m, 1.5m, 2.0m, 2.5m),
                5.5m, 25m)
        };

        var report = MarkdownReportWriter.Generate(monteCarloResults: mc);
        Assert.Contains("Monte Carlo Simulation", report);
        Assert.Contains("Probability of ruin", report);
        Assert.Contains("5.5%", report);
    }

    [Fact]
    public void Generate_WithStreaks_ContainsStreakTable()
    {
        var streaks = new Dictionary<string, StreakAnalysis>
        {
            ["Test"] = new StreakAnalysis(7, 4, 3.5m, 2.1m, 10, 8)
        };

        var report = MarkdownReportWriter.Generate(streakResults: streaks);
        Assert.Contains("Streak Analysis", report);
        Assert.Contains("MaxWinStreak", report);
    }

    [Fact]
    public void Generate_WithEfficiency_ContainsMaeMfe()
    {
        var metrics = new List<EfficiencyMetric>
        {
            new("EmaPullback", 0.65m, 0.35m, 100)
        };

        var report = MarkdownReportWriter.Generate(efficiencyMetrics: metrics);
        Assert.Contains("MAE/MFE Efficiency", report);
        Assert.Contains("MFE Capture", report);
    }

    [Fact]
    public void Generate_WithSensitivity_ContainsMatrix()
    {
        var sensitivity = new SensitivityResult(
            [new ParameterSensitivity("AtrMultiplier", 1.5m,
                [new PerturbationResult(-20, 1.2m, 0.5m, 1.0m, 1.3m, 4m, 40)], 0.01m)],
            "AtrMultiplier", "AtrMultiplier");

        var report = MarkdownReportWriter.Generate(sensitivityResult: sensitivity);
        Assert.Contains("Sensitivity Analysis", report);
        Assert.Contains("Most sensitive parameter", report);
        Assert.Contains("AtrMultiplier", report);
    }

    [Fact]
    public void Generate_WithStrategyComparison_ContainsTable()
    {
        var comparisons = new List<StrategyComparison>
        {
            new("A", 100, 60m, 1.5m, 5000m, 1.5m, 0.75m, 5m, 1.5m, 25m),
            new("B", 80, 55m, 1.2m, 3000m, 1.0m, 0.5m, 8m, 0.8m, 30m)
        };

        var report = MarkdownReportWriter.Generate(strategyComparisons: comparisons);
        Assert.Contains("Strategy Comparison", report);
        Assert.Contains("| A |", report);
        Assert.Contains("| B |", report);
    }

    [Fact]
    public void Generate_WithHourly_ContainsTimeBreakdown()
    {
        var hourly = new Dictionary<string, List<HourlyBreakdown>>
        {
            ["Test"] = [new HourlyBreakdown(10, 5, 3, 2, 500m, 100m)]
        };

        var report = MarkdownReportWriter.Generate(hourlyResults: hourly);
        Assert.Contains("Hour of Day", report);
        Assert.Contains("10:00", report);
    }

    [Fact]
    public void Generate_WithExitReasons_ContainsBreakdown()
    {
        var exits = new Dictionary<string, List<ExitReasonBreakdown>>
        {
            ["Test"] = [new ExitReasonBreakdown("Target", 30, 6000m, 200m, 100m)]
        };

        var report = MarkdownReportWriter.Generate(exitReasonResults: exits);
        Assert.Contains("Exit Reasons", report);
        Assert.Contains("Target", report);
    }

    [Fact]
    public void WriteToFile_CreatesDirectoryAndFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_report_{Guid.NewGuid()}", "report.md");
        try
        {
            MarkdownReportWriter.WriteToFile(path, "# Test");
            Assert.True(File.Exists(path));
            Assert.Equal("# Test", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Directory.Delete(Path.GetDirectoryName(path)!);
            }
        }
    }
}
