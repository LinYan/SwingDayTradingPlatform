using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Backtesting;

public sealed class BacktestParameters
{
    public int FastEmaPeriod { get; init; } = 20;
    public int SlowEmaPeriod { get; init; } = 50;
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrMultiplier { get; init; } = 1.5m;
    public decimal RewardRiskRatio { get; init; } = 1.5m;
    public decimal PullbackTolerancePct { get; init; } = 0.0015m;
    public int MaxTradesPerDay { get; init; } = 5;
    public int MaxLossesPerDay { get; init; } = 3;
    public decimal MaxStopPoints { get; init; } = 10m;
    public int CooldownSeconds { get; init; } = 60;

    // Multi-strategy config fields
    public bool EnableStrategy1 { get; init; } = true;
    public bool EnableStrategy2 { get; init; } = true;
    public bool EnableStrategy3 { get; init; } = true;
    public bool EnableStrategy4 { get; init; } = true;
    public bool EnableHourlyBias { get; init; } = true;
    public int HourlyRangeLookback { get; init; } = 10;
    public int RangeTopPct { get; init; } = 75;
    public int RangeBottomPct { get; init; } = 25;
    public int SwingLookback { get; init; } = 3;
    public decimal SRClusterAtrFactor { get; init; } = 0.5m;
    public decimal BigMoveAtrFactor { get; init; } = 3.0m;
    public int MomentumBars { get; init; } = 3;
    public decimal MomentumBodyAtrRatio { get; init; } = 0.7m;
    public decimal TickSize { get; init; } = 0.25m;

    // Trailing stop
    public decimal TrailingStopAtrMultiplier { get; init; } = 2.0m;
    public int TrailingStopActivationBars { get; init; } = 3;
    public bool UseBarBreakExit { get; init; } = false;
    public bool UseReversalBarExit { get; init; } = false;

    // EmaPullback enhancements
    public decimal EmaPullbackRewardRatio { get; init; } = 2.0m;
    public decimal EmaPullbackTolerance { get; init; } = 0.75m;

    // SRReversal enhancements
    public int SRMinTouches { get; init; } = 3;
    public decimal SRReversalRewardRatio { get; init; } = 2.0m;

    // Momentum enhancements
    public decimal MomentumRewardRatio { get; init; } = 2.5m;
    public int MomentumPullbackWindowBars { get; init; } = 6;

    // FiftyPctPullback
    public int BigMoveStaleBars { get; init; } = 30;

    public StrategyConfig ToStrategyConfig() => new()
    {
        FastEmaPeriod = FastEmaPeriod,
        SlowEmaPeriod = SlowEmaPeriod,
        AtrPeriod = AtrPeriod,
        AtrMultiplier = AtrMultiplier,
        RewardRiskRatio = RewardRiskRatio,
        PullbackTolerancePct = PullbackTolerancePct,
        EnableTrailingStop = true,
        BarResolution = "5m"
    };

    public RiskConfig ToRiskConfig() => new()
    {
        MaxDailyLoss = 999_999m,
        RiskPerTradePct = 0m,
        MaxContracts = 1,
        FixedContracts = 1,
        MaxTradesPerDay = MaxTradesPerDay,
        MaxLossesPerDay = MaxLossesPerDay,
        MaxStopPoints = MaxStopPoints,
        CooldownSeconds = CooldownSeconds,
        UseUnrealizedPnLForDailyLimit = false
    };

    public MultiStrategyConfig ToMultiStrategyConfig() => new()
    {
        FastEmaPeriod = FastEmaPeriod,
        SlowEmaPeriod = SlowEmaPeriod,
        AtrPeriod = AtrPeriod,
        EnableStrategy1 = EnableStrategy1,
        EnableStrategy2 = EnableStrategy2,
        EnableStrategy3 = EnableStrategy3,
        EnableStrategy4 = EnableStrategy4,
        EnableHourlyBias = EnableHourlyBias,
        HourlyRangeLookback = HourlyRangeLookback,
        RangeTopPct = RangeTopPct,
        RangeBottomPct = RangeBottomPct,
        SwingLookback = SwingLookback,
        SRClusterAtrFactor = SRClusterAtrFactor,
        BigMoveAtrFactor = BigMoveAtrFactor,
        MomentumBars = MomentumBars,
        MomentumBodyAtrRatio = MomentumBodyAtrRatio,
        TickSize = TickSize,
        TrailingStopAtrMultiplier = TrailingStopAtrMultiplier,
        TrailingStopActivationBars = TrailingStopActivationBars,
        UseBarBreakExit = UseBarBreakExit,
        UseReversalBarExit = UseReversalBarExit,
        EmaPullbackRewardRatio = EmaPullbackRewardRatio,
        EmaPullbackTolerance = EmaPullbackTolerance,
        SRMinTouches = SRMinTouches,
        SRReversalRewardRatio = SRReversalRewardRatio,
        MomentumRewardRatio = MomentumRewardRatio,
        MomentumPullbackWindowBars = MomentumPullbackWindowBars,
        MaxStopPoints = MaxStopPoints,
        BigMoveStaleBars = BigMoveStaleBars
    };

    public string Label => $"F{FastEmaPeriod}/S{SlowEmaPeriod} ATR{AtrPeriod}x{AtrMultiplier} RR{RewardRiskRatio}";
}
