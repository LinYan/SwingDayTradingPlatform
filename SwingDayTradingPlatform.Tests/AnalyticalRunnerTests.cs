using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.Tests;

public class AnalyticalRunnerTests
{
    private static BacktestConfig DefaultConfig() => new()
    {
        StartingCapital = 25_000m,
        PointValue = 50m,
        Timezone = "America/New_York",
        EntryWindowStart = "09:40",
        EntryWindowEnd = "15:50",
        FlattenTime = "15:55"
    };

    [Fact]
    public async Task RunAsync_SmokeTest_ReturnsAllSections()
    {
        var bars = TestHelpers.GenerateWarmupBars(120, 5000m);
        var parameters = new BacktestParameters
        {
            EnableTimeFilter = false,
            EnableHourlyBias = false,
            EnableBreakEvenStop = false
        };
        var config = DefaultConfig();
        var options = new AnalysisOptions
        {
            RunMonteCarlo = true,
            MonteCarloIterations = 50,
            RunSensitivity = false,
            ReportOutputPath = ""
        };

        var statusMessages = new List<string>();
        var report = await AnalyticalRunner.RunAsync(
            bars, parameters, config, options,
            onStatus: msg => statusMessages.Add(msg));

        Assert.NotNull(report);
        Assert.NotNull(report.StrategyResults);
        Assert.NotNull(report.Rankings);
        Assert.NotNull(report.MonteCarloResults);
        Assert.NotNull(report.StreakResults);
        Assert.NotNull(report.HourlyResults);
        Assert.NotNull(report.StrategyComparisons);
        Assert.NotNull(report.EfficiencyMetrics);
        Assert.NotEmpty(report.ReportMarkdown);
        Assert.Contains("Quantitative Trading Research Report", report.ReportMarkdown);
        Assert.True(statusMessages.Count > 0);
    }

    [Fact]
    public async Task RunAsync_NoMonteCarlo_SkipsMC()
    {
        var bars = TestHelpers.GenerateWarmupBars(120, 5000m);
        var parameters = new BacktestParameters
        {
            EnableTimeFilter = false,
            EnableHourlyBias = false,
            EnableBreakEvenStop = false
        };
        var config = DefaultConfig();
        var options = new AnalysisOptions
        {
            RunMonteCarlo = false,
            RunSensitivity = false,
            ReportOutputPath = ""
        };

        var report = await AnalyticalRunner.RunAsync(bars, parameters, config, options);

        Assert.Empty(report.MonteCarloResults);
    }

    [Fact]
    public async Task RunAsync_ReportContainsStrategyComparison()
    {
        var bars = TestHelpers.GenerateWarmupBars(120, 5000m);
        var parameters = new BacktestParameters
        {
            EnableTimeFilter = false,
            EnableHourlyBias = false,
            EnableBreakEvenStop = false
        };
        var config = DefaultConfig();
        var options = new AnalysisOptions
        {
            RunMonteCarlo = false,
            RunSensitivity = false,
            ReportOutputPath = ""
        };

        var report = await AnalyticalRunner.RunAsync(bars, parameters, config, options);

        Assert.Contains("Executive Summary", report.ReportMarkdown);
    }
}
