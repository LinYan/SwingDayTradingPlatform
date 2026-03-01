using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.Tests;

public class SensitivityAnalyzerTests
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
    public void Analyze_SingleParam_ReturnsPerturbations()
    {
        var bars = TestHelpers.GenerateWarmupBars(120, 5000m);
        var parameters = new BacktestParameters
        {
            EnableTimeFilter = false,
            EnableHourlyBias = false,
            EnableBreakEvenStop = false
        };
        var config = DefaultConfig();

        var result = SensitivityAnalyzer.Analyze(
            bars, parameters, config,
            ["AtrMultiplier"],
            [- 20, 20]);

        Assert.Single(result.Parameters);
        Assert.Equal("AtrMultiplier", result.Parameters[0].ParameterName);
        Assert.Equal(2, result.Parameters[0].Perturbations.Count);
        Assert.Equal("AtrMultiplier", result.MostSensitiveParameter);
    }

    [Fact]
    public void Analyze_MultipleParams_IdentifiesMostSensitive()
    {
        var bars = TestHelpers.GenerateWarmupBars(120, 5000m);
        var parameters = new BacktestParameters
        {
            EnableTimeFilter = false,
            EnableHourlyBias = false,
            EnableBreakEvenStop = false
        };
        var config = DefaultConfig();

        var result = SensitivityAnalyzer.Analyze(
            bars, parameters, config,
            ["AtrMultiplier", "RewardRiskRatio"],
            [-20, 20]);

        Assert.Equal(2, result.Parameters.Count);
        Assert.False(string.IsNullOrEmpty(result.MostSensitiveParameter));
        Assert.False(string.IsNullOrEmpty(result.LeastSensitiveParameter));
    }

    [Fact]
    public void Analyze_UnknownParam_Skipped()
    {
        var bars = TestHelpers.GenerateWarmupBars(120, 5000m);
        var parameters = new BacktestParameters();
        var config = DefaultConfig();

        var result = SensitivityAnalyzer.Analyze(
            bars, parameters, config,
            ["NonExistentParam"],
            [-10, 10]);

        Assert.Empty(result.Parameters);
    }

    [Fact]
    public void Analyze_CancellationRespected()
    {
        var bars = TestHelpers.GenerateWarmupBars(120, 5000m);
        var parameters = new BacktestParameters();
        var config = DefaultConfig();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            SensitivityAnalyzer.Analyze(
                bars, parameters, config,
                ["AtrMultiplier"],
                [-10, 10],
                ct: cts.Token));
    }
}
