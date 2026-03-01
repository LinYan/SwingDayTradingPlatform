using SwingDayTradingPlatform.Risk;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Tests;

public class RiskEngineTests
{
    private static RiskEngine CreateEngine(RiskConfig? config = null) =>
        new(config ?? TestHelpers.DefaultRiskConfig());

    private static RiskConfig ConfigWith(
        decimal? maxDailyLoss = null, int? maxTradesPerDay = null, int? maxLossesPerDay = null,
        int? cooldownSeconds = null, decimal? maxStopPoints = null,
        int? fixedContracts = null, int? maxContracts = null,
        decimal? riskPerTradePct = null, bool? useUnrealizedPnL = null)
    {
        var d = TestHelpers.DefaultRiskConfig();
        return new RiskConfig
        {
            MaxDailyLoss = maxDailyLoss ?? d.MaxDailyLoss,
            MaxTradesPerDay = maxTradesPerDay ?? d.MaxTradesPerDay,
            MaxLossesPerDay = maxLossesPerDay ?? d.MaxLossesPerDay,
            CooldownSeconds = cooldownSeconds ?? d.CooldownSeconds,
            MaxStopPoints = maxStopPoints ?? d.MaxStopPoints,
            FixedContracts = fixedContracts ?? d.FixedContracts,
            MaxContracts = maxContracts ?? d.MaxContracts,
            RiskPerTradePct = riskPerTradePct ?? d.RiskPerTradePct,
            UseUnrealizedPnLForDailyLimit = useUnrealizedPnL ?? d.UseUnrealizedPnLForDailyLimit
        };
    }

    [Fact]
    public void InitialState_Ready()
    {
        var engine = CreateEngine();
        Assert.False(engine.KillSwitchArmed);
        Assert.False(engine.TradingHalted);
        Assert.Equal(0, engine.TradeCountToday);
        Assert.Equal(0, engine.LossCountToday);
    }

    [Fact]
    public void CanOpenNewPosition_NoConstraints_ReturnsTrue()
    {
        var engine = CreateEngine();
        Assert.True(engine.CanOpenNewPosition(DateTimeOffset.UtcNow, false, false));
    }

    [Fact]
    public void CanOpenNewPosition_HasPosition_ReturnsFalse()
    {
        var engine = CreateEngine();
        Assert.False(engine.CanOpenNewPosition(DateTimeOffset.UtcNow, true, false));
    }

    [Fact]
    public void CanOpenNewPosition_AlreadyFlattened_ReturnsFalse()
    {
        var engine = CreateEngine();
        Assert.False(engine.CanOpenNewPosition(DateTimeOffset.UtcNow, false, true));
    }

    [Fact]
    public void CanOpenNewPosition_KillSwitchArmed_ReturnsFalse()
    {
        var engine = CreateEngine();
        engine.ArmKillSwitch("test");
        Assert.False(engine.CanOpenNewPosition(DateTimeOffset.UtcNow, false, false));
    }

    [Fact]
    public void CanOpenNewPosition_MaxTradesHit_ReturnsFalse()
    {
        var config = ConfigWith(maxTradesPerDay: 2);
        var engine = CreateEngine(config);
        var now = DateTimeOffset.UtcNow;
        engine.MarkEntryAttempt(now.AddMinutes(-120), "trade1");
        engine.MarkEntryAttempt(now.AddMinutes(-60), "trade2");
        Assert.False(engine.CanOpenNewPosition(now, false, false));
        Assert.True(engine.TradingHalted);
    }

    [Fact]
    public void CanOpenNewPosition_MaxLossesHit_ArmsKillSwitch()
    {
        var config = ConfigWith(maxLossesPerDay: 2);
        var engine = CreateEngine(config);
        engine.RegisterClosedTrade(-100m);
        engine.RegisterClosedTrade(-100m);
        Assert.True(engine.KillSwitchArmed);
        Assert.False(engine.CanOpenNewPosition(DateTimeOffset.UtcNow, false, false));
    }

    [Fact]
    public void CanOpenNewPosition_CooldownActive_ReturnsFalse()
    {
        var config = ConfigWith(cooldownSeconds: 60);
        var engine = CreateEngine(config);
        var now = DateTimeOffset.UtcNow;
        engine.MarkEntryAttempt(now.AddSeconds(-30), "recent trade");
        Assert.False(engine.CanOpenNewPosition(now, false, false));
        Assert.Equal("Cooldown active", engine.LastReason);
    }

    [Fact]
    public void CanOpenNewPosition_CooldownExpired_ReturnsTrue()
    {
        var config = ConfigWith(cooldownSeconds: 60);
        var engine = CreateEngine(config);
        var now = DateTimeOffset.UtcNow;
        engine.MarkEntryAttempt(now.AddSeconds(-120), "old trade");
        Assert.True(engine.CanOpenNewPosition(now, false, false));
    }

    [Fact]
    public void CalculateContracts_ZeroStopDistance_ReturnsZero()
    {
        var engine = CreateEngine();
        Assert.Equal(0, engine.CalculateContracts(5000m, 5000m, 25000m, 50m));
        Assert.Contains("zero", engine.LastReason);
    }

    [Fact]
    public void CalculateContracts_ExceedsMaxStopPoints_ReturnsZero()
    {
        var config = ConfigWith(maxStopPoints: 5m);
        var engine = CreateEngine(config);
        Assert.Equal(0, engine.CalculateContracts(5000m, 4990m, 25000m, 50m)); // 10 pts > 5 max
    }

    [Fact]
    public void CalculateContracts_FixedContracts_ReturnsFixed()
    {
        var config = ConfigWith(fixedContracts: 2, maxContracts: 3);
        var engine = CreateEngine(config);
        Assert.Equal(2, engine.CalculateContracts(5000m, 4995m, 25000m, 50m));
    }

    [Fact]
    public void CalculateContracts_FixedContracts_CappedByMax()
    {
        var config = ConfigWith(fixedContracts: 5, maxContracts: 2);
        var engine = CreateEngine(config);
        Assert.Equal(2, engine.CalculateContracts(5000m, 4995m, 25000m, 50m));
    }

    [Fact]
    public void CalculateContracts_RiskBased_CalculatesCorrectly()
    {
        var config = new RiskConfig
        {
            FixedContracts = 0,
            RiskPerTradePct = 2m,
            MaxContracts = 10,
            MaxStopPoints = 20m,
            MaxDailyLoss = 5000m,
            MaxTradesPerDay = 10,
            MaxLossesPerDay = 5,
            CooldownSeconds = 0
        };
        var engine = CreateEngine(config);
        // Budget = 50000 * 2% = 1000. ContractRisk = 5 * 50 = 250. Contracts = 4.
        var result = engine.CalculateContracts(5000m, 4995m, 50000m, 50m);
        Assert.Equal(4, result);
    }

    [Fact]
    public void EvaluateDailyLoss_NullAccount_DoesNothing()
    {
        var engine = CreateEngine();
        engine.EvaluateDailyLoss(null);
        Assert.False(engine.KillSwitchArmed);
    }

    [Fact]
    public void EvaluateDailyLoss_WithUnrealized_ArmsKillSwitch()
    {
        var config = ConfigWith(maxDailyLoss: 500m, useUnrealizedPnL: true);
        var engine = CreateEngine(config);
        var snapshot = new AccountSnapshot(25000, 24000, 50000, -200, -400, "USD");
        engine.EvaluateDailyLoss(snapshot); // -200 + -400 = -600 > -500
        Assert.True(engine.KillSwitchArmed);
    }

    [Fact]
    public void EvaluateDailyLoss_WithoutUnrealized_OnlyRealizedCounts()
    {
        var config = ConfigWith(maxDailyLoss: 500m, useUnrealizedPnL: false);
        var engine = CreateEngine(config);
        var snapshot = new AccountSnapshot(25000, 24000, 50000, -200, -800, "USD");
        engine.EvaluateDailyLoss(snapshot); // only -200, under -500 limit
        Assert.False(engine.KillSwitchArmed);
    }

    [Fact]
    public void ResetForNewDay_ClearsAllState()
    {
        var engine = CreateEngine();
        engine.ArmKillSwitch("test");
        engine.MarkEntryAttempt(DateTimeOffset.UtcNow, "trade");
        engine.RegisterClosedTrade(-100m);

        engine.ResetForNewDay();

        Assert.False(engine.KillSwitchArmed);
        Assert.False(engine.TradingHalted);
        Assert.Equal(0, engine.TradeCountToday);
        Assert.Equal(0, engine.LossCountToday);
    }

    [Fact]
    public void RestoreDayState_RestoresCounts()
    {
        var engine = CreateEngine();
        engine.RestoreDayState(3, 2);
        Assert.Equal(3, engine.TradeCountToday);
        Assert.Equal(2, engine.LossCountToday);
    }

    [Fact]
    public void RegisterClosedTrade_WinningTrade_DoesNotIncrementLosses()
    {
        var engine = CreateEngine();
        engine.RegisterClosedTrade(500m);
        Assert.Equal(0, engine.LossCountToday);
    }

    [Fact]
    public void RegisterClosedTrade_LosingTrade_IncrementsLosses()
    {
        var engine = CreateEngine();
        engine.RegisterClosedTrade(-100m);
        Assert.Equal(1, engine.LossCountToday);
    }

    [Fact]
    public void MarkEntryAttempt_IncrementsTradeCount()
    {
        var engine = CreateEngine();
        engine.MarkEntryAttempt(DateTimeOffset.UtcNow, "test");
        Assert.Equal(1, engine.TradeCountToday);
        engine.MarkEntryAttempt(DateTimeOffset.UtcNow.AddMinutes(61), "test2");
        Assert.Equal(2, engine.TradeCountToday);
    }

    [Fact]
    public void ConsecutiveLosses_ArmsKillSwitch()
    {
        var config = new RiskConfig
        {
            MaxDailyLoss = 999_999m,
            MaxTradesPerDay = 99,
            MaxLossesPerDay = 99,
            MaxStopPoints = 50m,
            CooldownSeconds = 0,
            FixedContracts = 1,
            MaxContracts = 1,
            MaxConsecutiveLossesPerDay = 3
        };
        var engine = CreateEngine(config);
        engine.RegisterClosedTrade(-100m, -1m);
        Assert.False(engine.KillSwitchArmed);
        engine.RegisterClosedTrade(-100m, -1m);
        Assert.False(engine.KillSwitchArmed);
        engine.RegisterClosedTrade(-100m, -1m);
        Assert.True(engine.KillSwitchArmed);
    }

    [Fact]
    public void ConsecutiveLosses_WinResetsStreak()
    {
        var config = new RiskConfig
        {
            MaxDailyLoss = 999_999m,
            MaxTradesPerDay = 99,
            MaxLossesPerDay = 99,
            MaxStopPoints = 50m,
            CooldownSeconds = 0,
            FixedContracts = 1,
            MaxContracts = 1,
            MaxConsecutiveLossesPerDay = 3
        };
        var engine = CreateEngine(config);
        engine.RegisterClosedTrade(-100m, -1m);
        engine.RegisterClosedTrade(-100m, -1m);
        engine.RegisterClosedTrade(200m, 2m); // win resets streak
        Assert.Equal(0, engine.ConsecutiveLosses);
        Assert.False(engine.KillSwitchArmed);
        engine.RegisterClosedTrade(-100m, -1m);
        engine.RegisterClosedTrade(-100m, -1m);
        Assert.False(engine.KillSwitchArmed); // only 2 consecutive
    }

    [Fact]
    public void DailyRLossLimit_ArmsKillSwitch()
    {
        var config = new RiskConfig
        {
            MaxDailyLoss = 999_999m,
            MaxTradesPerDay = 99,
            MaxLossesPerDay = 99,
            MaxStopPoints = 50m,
            CooldownSeconds = 0,
            FixedContracts = 1,
            MaxContracts = 1,
            MaxDailyLossR = 2.0m
        };
        var engine = CreateEngine(config);
        engine.RegisterClosedTrade(-100m, -1.5m);
        Assert.False(engine.KillSwitchArmed);
        engine.RegisterClosedTrade(-100m, -1.0m); // cumR = -2.5 > -2.0 limit
        Assert.True(engine.KillSwitchArmed);
    }

    [Fact]
    public void MaxStopTicks_RejectsLargeStop()
    {
        var config = new RiskConfig
        {
            MaxDailyLoss = 999_999m,
            MaxTradesPerDay = 99,
            MaxLossesPerDay = 99,
            MaxStopPoints = 50m,
            CooldownSeconds = 0,
            FixedContracts = 1,
            MaxContracts = 1,
            MaxStopTicks = 30
        };
        var engine = CreateEngine(config);
        // 10 point stop / 0.25 tick = 40 ticks > 30 max
        var contracts = engine.CalculateContracts(5000m, 4990m, 25000m, 50m, 0.25m);
        Assert.Equal(0, contracts);
        Assert.Contains("Stop ticks", engine.LastReason);
    }

    [Fact]
    public void MaxStopTicks_Zero_DisablesCheck()
    {
        var config = new RiskConfig
        {
            MaxDailyLoss = 999_999m,
            MaxTradesPerDay = 99,
            MaxLossesPerDay = 99,
            MaxStopPoints = 50m,
            CooldownSeconds = 0,
            FixedContracts = 1,
            MaxContracts = 1,
            MaxStopTicks = 0 // disabled
        };
        var engine = CreateEngine(config);
        var contracts = engine.CalculateContracts(5000m, 4990m, 25000m, 50m, 0.25m);
        Assert.Equal(1, contracts); // should pass
    }

    [Fact]
    public void ResetForNewDay_ClearsRState()
    {
        var config = new RiskConfig
        {
            MaxDailyLoss = 999_999m,
            MaxTradesPerDay = 99,
            MaxLossesPerDay = 99,
            MaxStopPoints = 50m,
            CooldownSeconds = 0,
            FixedContracts = 1,
            MaxContracts = 1,
            MaxConsecutiveLossesPerDay = 99,
            MaxDailyLossR = 99m
        };
        var engine = CreateEngine(config);
        engine.RegisterClosedTrade(-100m, -1m);
        engine.RegisterClosedTrade(-100m, -1m);
        Assert.Equal(2, engine.ConsecutiveLosses);
        Assert.Equal(-2m, engine.DailyCumulativeR);

        engine.ResetForNewDay();
        Assert.Equal(0, engine.ConsecutiveLosses);
        Assert.Equal(0m, engine.DailyCumulativeR);
    }
}
