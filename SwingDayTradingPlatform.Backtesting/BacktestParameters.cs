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
    public bool EnableStrategy9 { get; init; } = true;
    public bool EnableHourlyBias { get; init; } = true;
    public int HourlyRangeLookback { get; init; } = 10;
    public int RangeTopPct { get; init; } = 75;
    public int RangeBottomPct { get; init; } = 25;
    public int SwingLookback { get; init; } = 3;
    public decimal SRClusterAtrFactor { get; init; } = 0.5m;
    public decimal BigMoveAtrFactor { get; init; } = 3.0m;
    public decimal TickSize { get; init; } = 0.25m;

    // Trailing stop
    public decimal TrailingStopAtrMultiplier { get; init; } = 2.0m;
    public int TrailingStopActivationBars { get; init; } = 4;
    public bool UseBarBreakExit { get; init; } = false;
    public int MinBarBreakHoldBars { get; init; } = 2;
    public bool UseReversalBarExit { get; init; } = false;

    // RSI
    public int RsiPeriod { get; init; } = 14;

    // EmaPullback enhancements
    public decimal EmaPullbackRewardRatio { get; init; } = 2.0m;
    public decimal EmaPullbackTolerance { get; init; } = 0.5m;
    public decimal EmaMinSlopeAtr { get; init; } = 0.10m;
    public decimal EmaBodyMinAtrRatio { get; init; } = 0.4m;
    public decimal EmaRsiLongMin { get; init; } = 45m;
    public decimal EmaRsiLongMax { get; init; } = 65m;
    public decimal EmaRsiShortMin { get; init; } = 35m;
    public decimal EmaRsiShortMax { get; init; } = 55m;
    public decimal EmaStopAtrBuffer { get; init; } = 0.25m;

    // BrooksPA
    public decimal BrooksPA_SignalBarBodyRatio { get; init; } = 0.5m;
    public decimal BrooksPA_MinBarRangeAtr { get; init; } = 0.3m;
    public int BrooksPA_PullbackLookback { get; init; } = 20;
    public decimal BrooksPA_EmaToleranceAtr { get; init; } = 0.75m;
    public decimal BrooksPA_RewardRatio { get; init; } = 2.0m;
    public int BrooksPA_MaxStopTicks { get; init; } = 40;

    // R-based risk
    public decimal MaxDailyLossR { get; init; } = 99m;
    public int MaxConsecutiveLossesPerDay { get; init; } = 99;
    public int MaxStopTicks { get; init; } = 0;

    // Global filters
    public bool EnableTimeFilter { get; init; } = true;
    public int LunchStartHour { get; init; } = 11;
    public int LunchStartMinute { get; init; } = 30;
    public int LunchEndHour { get; init; } = 13;
    public int LunchEndMinute { get; init; } = 0;
    public int LateCutoffHour { get; init; } = 15;
    public int LateCutoffMinute { get; init; } = 45;
    public int MaxDailyTrades { get; init; } = 6;

    // Break-even stop
    public bool EnableBreakEvenStop { get; init; } = true;
    public decimal BreakEvenActivationR { get; init; } = 1.2m;

    // SlopeInflection (Strategy 12)
    public bool EnableStrategy12 { get; init; } = false;
    public int SI_SmoothingPeriod { get; init; } = 9;
    public int SI_FlatCrossWindow { get; init; } = 6;
    public decimal SI_SlopeEpsTicks { get; init; } = 1.0m;
    public int SI_StrongTrendLookback { get; init; } = 12;
    public decimal SI_StrongTrendPct { get; init; } = 0.75m;
    public int SI_CooldownBars { get; init; } = 6;
    public bool SI_UseTightStop { get; init; } = true;

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
        UseUnrealizedPnLForDailyLimit = false,
        MaxDailyLossR = MaxDailyLossR,
        MaxConsecutiveLossesPerDay = MaxConsecutiveLossesPerDay,
        MaxStopTicks = MaxStopTicks
    };

    public MultiStrategyConfig ToMultiStrategyConfig() => new()
    {
        FastEmaPeriod = FastEmaPeriod,
        SlowEmaPeriod = SlowEmaPeriod,
        AtrPeriod = AtrPeriod,
        EnableStrategy1 = EnableStrategy1,
        EnableStrategy9 = EnableStrategy9,
        EnableHourlyBias = EnableHourlyBias,
        HourlyRangeLookback = HourlyRangeLookback,
        RangeTopPct = RangeTopPct,
        RangeBottomPct = RangeBottomPct,
        SwingLookback = SwingLookback,
        SRClusterAtrFactor = SRClusterAtrFactor,
        BigMoveAtrFactor = BigMoveAtrFactor,
        TickSize = TickSize,
        TrailingStopAtrMultiplier = TrailingStopAtrMultiplier,
        TrailingStopActivationBars = TrailingStopActivationBars,
        UseBarBreakExit = UseBarBreakExit,
        MinBarBreakHoldBars = MinBarBreakHoldBars,
        UseReversalBarExit = UseReversalBarExit,
        RsiPeriod = RsiPeriod,
        EmaPullbackRewardRatio = EmaPullbackRewardRatio,
        EmaPullbackTolerance = EmaPullbackTolerance,
        EmaMinSlopeAtr = EmaMinSlopeAtr,
        EmaBodyMinAtrRatio = EmaBodyMinAtrRatio,
        EmaRsiLongMin = EmaRsiLongMin,
        EmaRsiLongMax = EmaRsiLongMax,
        EmaRsiShortMin = EmaRsiShortMin,
        EmaRsiShortMax = EmaRsiShortMax,
        EmaStopAtrBuffer = EmaStopAtrBuffer,
        BrooksPA_SignalBarBodyRatio = BrooksPA_SignalBarBodyRatio,
        BrooksPA_MinBarRangeAtr = BrooksPA_MinBarRangeAtr,
        BrooksPA_PullbackLookback = BrooksPA_PullbackLookback,
        BrooksPA_EmaToleranceAtr = BrooksPA_EmaToleranceAtr,
        BrooksPA_RewardRatio = BrooksPA_RewardRatio,
        BrooksPA_MaxStopTicks = BrooksPA_MaxStopTicks,
        MaxStopPoints = MaxStopPoints,
        EnableTimeFilter = EnableTimeFilter,
        LunchStartHour = LunchStartHour,
        LunchStartMinute = LunchStartMinute,
        LunchEndHour = LunchEndHour,
        LunchEndMinute = LunchEndMinute,
        LateCutoffHour = LateCutoffHour,
        LateCutoffMinute = LateCutoffMinute,
        MaxDailyTrades = MaxDailyTrades,
        EnableBreakEvenStop = EnableBreakEvenStop,
        BreakEvenActivationR = BreakEvenActivationR,
        EnableStrategy12 = EnableStrategy12,
        SI_SmoothingPeriod = SI_SmoothingPeriod,
        SI_FlatCrossWindow = SI_FlatCrossWindow,
        SI_SlopeEpsTicks = SI_SlopeEpsTicks,
        SI_StrongTrendLookback = SI_StrongTrendLookback,
        SI_StrongTrendPct = SI_StrongTrendPct,
        SI_CooldownBars = SI_CooldownBars,
        SI_UseTightStop = SI_UseTightStop
    };

    public string Label => $"F{FastEmaPeriod}/S{SlowEmaPeriod} ATR{AtrPeriod}x{AtrMultiplier} RR{RewardRiskRatio}";
}
