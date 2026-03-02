using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.Tests;

public class ValidationTests
{
    // --- BacktestConfig Validation ---

    [Fact]
    public void BacktestConfig_ValidDefaults_NoErrors()
    {
        var config = new BacktestConfig();
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void BacktestConfig_StartAfterEnd_HasError()
    {
        var config = new BacktestConfig
        {
            StartDate = new DateOnly(2025, 1, 1),
            EndDate = new DateOnly(2024, 1, 1)
        };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("StartDate"));
    }

    [Fact]
    public void BacktestConfig_NegativeCapital_HasError()
    {
        var config = new BacktestConfig { StartingCapital = -1000m };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("StartingCapital"));
    }

    [Fact]
    public void BacktestConfig_ZeroPointValue_HasError()
    {
        var config = new BacktestConfig { PointValue = 0m };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("PointValue"));
    }

    [Fact]
    public void BacktestConfig_InvalidTimeFormat_HasError()
    {
        var config = new BacktestConfig { EntryWindowStart = "invalid" };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("EntryWindowStart"));
    }

    [Fact]
    public void BacktestConfig_EntryStartAfterEnd_HasError()
    {
        var config = new BacktestConfig
        {
            EntryWindowStart = "15:00",
            EntryWindowEnd = "10:00"
        };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("EntryWindowStart") && e.Contains("before"));
    }

    [Fact]
    public void BacktestConfig_EntryEndAfterFlatten_HasError()
    {
        var config = new BacktestConfig
        {
            EntryWindowEnd = "16:00",
            FlattenTime = "15:55"
        };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("EntryWindowEnd") && e.Contains("FlattenTime"));
    }

    [Fact]
    public void BacktestConfig_InSampleCutoffOutOfRange_HasError()
    {
        var config = new BacktestConfig
        {
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2024, 12, 31),
            InSampleCutoff = new DateOnly(2025, 6, 1)
        };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("InSampleCutoff"));
    }

    // --- BacktestParameters Validation ---

    [Fact]
    public void BacktestParameters_ValidDefaults_NoErrors()
    {
        var parameters = new BacktestParameters();
        Assert.Empty(parameters.Validate());
    }

    [Fact]
    public void BacktestParameters_FastGteqSlow_HasError()
    {
        var parameters = new BacktestParameters
        {
            FastEmaPeriod = 50,
            SlowEmaPeriod = 20
        };
        var errors = parameters.Validate();
        Assert.Contains(errors, e => e.Contains("FastEmaPeriod") && e.Contains("SlowEmaPeriod"));
    }

    [Fact]
    public void BacktestParameters_NegativeAtrMultiplier_HasError()
    {
        var parameters = new BacktestParameters { AtrMultiplier = -1m };
        var errors = parameters.Validate();
        Assert.Contains(errors, e => e.Contains("AtrMultiplier"));
    }

    [Fact]
    public void BacktestParameters_RangeTopLessThanBottom_HasError()
    {
        var parameters = new BacktestParameters
        {
            RangeTopPct = 20,
            RangeBottomPct = 80
        };
        var errors = parameters.Validate();
        Assert.Contains(errors, e => e.Contains("RangeBottomPct") && e.Contains("RangeTopPct"));
    }

    [Fact]
    public void BacktestParameters_RangeOutOfBounds_HasError()
    {
        var parameters = new BacktestParameters { RangeTopPct = 150 };
        var errors = parameters.Validate();
        Assert.Contains(errors, e => e.Contains("RangeTopPct"));
    }

    // --- ResultCalculator Edge Cases ---

    [Fact]
    public void Calculate_NoTrades_AllZeros()
    {
        var config = new BacktestConfig();
        var parameters = new BacktestParameters();
        var equityCurve = new List<EquityPoint>
        {
            new(DateTimeOffset.UtcNow, 25000m, 0m)
        };

        var result = ResultCalculator.Calculate(parameters, config, [], equityCurve);

        Assert.Equal(0, result.TotalTrades);
        Assert.Equal(0m, result.NetPnL);
        Assert.Equal(0m, result.WinRate);
        Assert.Equal(0m, result.ProfitFactor);
        Assert.Equal(0m, result.SharpeRatio);
    }

    [Fact]
    public void Calculate_SingleWinningTrade_CorrectMetrics()
    {
        var config = new BacktestConfig();
        var parameters = new BacktestParameters();
        var trade = new BacktestTrade(
            1, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
            5000m, 5010m, "Long", "Target", 10m, 500m, 0m)
        {
            RMultiple = 2m, InitialStopDistance = 5m, MFE = 12m, MAE = 1m
        };
        var equityCurve = new List<EquityPoint>
        {
            new(DateTimeOffset.UtcNow.AddMinutes(-10), 25000m, 0m),
            new(DateTimeOffset.UtcNow, 25500m, 0m)
        };

        var result = ResultCalculator.Calculate(parameters, config, [trade], equityCurve);

        Assert.Equal(1, result.WinningTrades);
        Assert.Equal(0, result.LosingTrades);
        Assert.Equal(100m, result.WinRate);
        Assert.Equal(1, result.MaxConsecutiveWins);
        Assert.Equal(0, result.MaxConsecutiveLosses);
    }

    [Fact]
    public void Calculate_AllLosers_ZeroProfitFactor()
    {
        var config = new BacktestConfig();
        var parameters = new BacktestParameters();
        var trades = new List<BacktestTrade>
        {
            new(1, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
                5000m, 4990m, "Long", "StopLoss", -10m, -500m, 0m)
                { RMultiple = -1m, InitialStopDistance = 10m },
            new(2, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow,
                5000m, 4995m, "Long", "StopLoss", -5m, -250m, 0m)
                { RMultiple = -0.5m, InitialStopDistance = 10m }
        };
        var equityCurve = new List<EquityPoint>
        {
            new(DateTimeOffset.UtcNow.AddMinutes(-10), 25000m, 0m),
            new(DateTimeOffset.UtcNow.AddMinutes(-5), 24500m, 2m),
            new(DateTimeOffset.UtcNow, 24250m, 3m)
        };

        var result = ResultCalculator.Calculate(parameters, config, trades, equityCurve);

        Assert.Equal(0, result.WinningTrades);
        Assert.Equal(0m, result.ProfitFactor);
        Assert.True(result.MaxDrawdown > 0);
        Assert.True(result.UlcerIndex > 0);
    }

    [Fact]
    public void DrawdownRecovery_NoDrawdown_ReturnsZeros()
    {
        var curve = new List<EquityPoint>
        {
            new(DateTimeOffset.UtcNow.AddMinutes(-20), 25000m, 0),
            new(DateTimeOffset.UtcNow.AddMinutes(-10), 25500m, 0),
            new(DateTimeOffset.UtcNow, 26000m, 0),
        };

        var (maxRecovery, avgRecovery) = ResultCalculator.CalculateDrawdownRecovery(curve);
        Assert.Equal(0, maxRecovery);
        Assert.Equal(0m, avgRecovery);
    }

    [Fact]
    public void DrawdownRecovery_WithRecovery_CorrectBars()
    {
        var curve = new List<EquityPoint>
        {
            new(DateTimeOffset.UtcNow.AddMinutes(-40), 25000m, 0),    // peak
            new(DateTimeOffset.UtcNow.AddMinutes(-30), 24500m, 2m),   // drawdown starts (bar 1)
            new(DateTimeOffset.UtcNow.AddMinutes(-20), 24200m, 3.2m), // deeper
            new(DateTimeOffset.UtcNow.AddMinutes(-10), 24800m, 0.8m), // recovering
            new(DateTimeOffset.UtcNow, 25100m, 0),                    // recovered (4 bars to recover)
        };

        var (maxRecovery, avgRecovery) = ResultCalculator.CalculateDrawdownRecovery(curve);
        Assert.Equal(3, maxRecovery); // drawdown starts at bar 1, recovers at bar 4 => 3 bars
        Assert.Equal(3m, avgRecovery);
    }

    [Fact]
    public void BacktestEngine_InvalidConfig_ThrowsArgumentException()
    {
        var bars = TestHelpers.GenerateWarmupBars(60);
        var config = new BacktestConfig { StartingCapital = -1000m };
        var parameters = new BacktestParameters();

        var engine = new BacktestEngine();
        Assert.Throws<ArgumentException>(() => engine.Run(bars, parameters, config));
    }

    [Fact]
    public void BacktestEngine_InvalidParameters_ThrowsArgumentException()
    {
        var bars = TestHelpers.GenerateWarmupBars(60);
        var config = new BacktestConfig();
        var parameters = new BacktestParameters { FastEmaPeriod = 100, SlowEmaPeriod = 50 };

        var engine = new BacktestEngine();
        Assert.Throws<ArgumentException>(() => engine.Run(bars, parameters, config));
    }
}
