using SwingDayTradingPlatform.Backtesting;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Tests;

public class BacktestParametersTests
{
    [Fact]
    public void ToRiskConfig_MapsCorrectly()
    {
        var p = new BacktestParameters
        {
            MaxTradesPerDay = 3,
            MaxLossesPerDay = 2,
            MaxStopPoints = 8m,
            CooldownSeconds = 30
        };

        var risk = p.ToRiskConfig();

        Assert.Equal(3, risk.MaxTradesPerDay);
        Assert.Equal(2, risk.MaxLossesPerDay);
        Assert.Equal(8m, risk.MaxStopPoints);
        Assert.Equal(30, risk.CooldownSeconds);
        Assert.Equal(1, risk.FixedContracts);
        Assert.Equal(1, risk.MaxContracts);
    }

    [Fact]
    public void ToMultiStrategyConfig_MapsCorrectly()
    {
        var p = new BacktestParameters
        {
            FastEmaPeriod = 15,
            SlowEmaPeriod = 40,
            AtrPeriod = 10,
            EnableStrategy1 = false,
            EnableHourlyBias = true,
            SwingLookback = 5,
            TickSize = 0.5m,
            TrailingStopAtrMultiplier = 3.0m,
            TrailingStopActivationBars = 5,
            UseBarBreakExit = true,
            EmaPullbackRewardRatio = 1.5m,
            EmaPullbackTolerance = 0.5m,
            MaxStopPoints = 20m
        };

        var multi = p.ToMultiStrategyConfig();

        Assert.Equal(15, multi.FastEmaPeriod);
        Assert.Equal(40, multi.SlowEmaPeriod);
        Assert.Equal(10, multi.AtrPeriod);
        Assert.False(multi.EnableStrategy1);
        Assert.True(multi.EnableHourlyBias);
        Assert.Equal(5, multi.SwingLookback);
        Assert.Equal(0.5m, multi.TickSize);
        Assert.Equal(3.0m, multi.TrailingStopAtrMultiplier);
        Assert.Equal(5, multi.TrailingStopActivationBars);
        Assert.True(multi.UseBarBreakExit);
        Assert.Equal(1.5m, multi.EmaPullbackRewardRatio);
        Assert.Equal(0.5m, multi.EmaPullbackTolerance);
        Assert.Equal(20m, multi.MaxStopPoints);

        // SlopeInflection defaults should map through
        Assert.False(multi.EnableStrategy12);
        Assert.Equal(9, multi.SI_SmoothingPeriod);
        Assert.Equal(6, multi.SI_FlatCrossWindow);
        Assert.Equal(1.0m, multi.SI_SlopeEpsTicks);
        Assert.Equal(12, multi.SI_StrongTrendLookback);
        Assert.Equal(0.75m, multi.SI_StrongTrendPct);
        Assert.Equal(6, multi.SI_CooldownBars);
        Assert.True(multi.SI_UseTightStop);
    }

    [Fact]
    public void ToStrategyConfig_MapsCorrectly()
    {
        var p = new BacktestParameters
        {
            FastEmaPeriod = 15,
            SlowEmaPeriod = 40,
            AtrPeriod = 10,
            AtrMultiplier = 2.0m,
            RewardRiskRatio = 2.5m,
            PullbackTolerancePct = 0.002m
        };

        var sc = p.ToStrategyConfig();

        Assert.Equal(15, sc.FastEmaPeriod);
        Assert.Equal(40, sc.SlowEmaPeriod);
        Assert.Equal(10, sc.AtrPeriod);
        Assert.Equal(2.0m, sc.AtrMultiplier);
        Assert.Equal(2.5m, sc.RewardRiskRatio);
        Assert.True(sc.EnableTrailingStop);
    }

    [Fact]
    public void Label_FormatsCorrectly()
    {
        var p = new BacktestParameters
        {
            FastEmaPeriod = 20,
            SlowEmaPeriod = 50,
            AtrPeriod = 14,
            AtrMultiplier = 1.5m,
            RewardRiskRatio = 1.5m
        };

        Assert.Equal("F20/S50 ATR14x1.5 RR1.5", p.Label);
    }

    [Fact]
    public void Defaults_AreReasonable()
    {
        var p = new BacktestParameters();

        Assert.Equal(20, p.FastEmaPeriod);
        Assert.Equal(50, p.SlowEmaPeriod);
        Assert.Equal(14, p.AtrPeriod);
        Assert.True(p.EnableStrategy1);
        Assert.True(p.EnableStrategy9);
        Assert.Equal(0.25m, p.TickSize);
        Assert.Equal(10m, p.MaxStopPoints);
        Assert.Equal(2.0m, p.TrailingStopAtrMultiplier);
        Assert.Equal(4, p.TrailingStopActivationBars);
        Assert.False(p.UseBarBreakExit);
        Assert.Equal(2, p.MinBarBreakHoldBars);
        Assert.Equal(0.5m, p.EmaPullbackTolerance);
        Assert.True(p.EnableTimeFilter);
        Assert.True(p.EnableBreakEvenStop);
        Assert.Equal(1.2m, p.BreakEvenActivationR);
        Assert.Equal(6, p.MaxDailyTrades);

        // SlopeInflection defaults
        Assert.False(p.EnableStrategy12);
        Assert.Equal(9, p.SI_SmoothingPeriod);
        Assert.Equal(6, p.SI_FlatCrossWindow);
        Assert.Equal(1.0m, p.SI_SlopeEpsTicks);
        Assert.Equal(12, p.SI_StrongTrendLookback);
        Assert.Equal(0.75m, p.SI_StrongTrendPct);
        Assert.Equal(6, p.SI_CooldownBars);
        Assert.True(p.SI_UseTightStop);
    }
}
