namespace SwingDayTradingPlatform.Shared;

public sealed class MultiStrategyConfig
{
    public int FastEmaPeriod { get; init; } = 20;
    public int SlowEmaPeriod { get; init; } = 50;
    public int AtrPeriod { get; init; } = 14;

    public bool EnableStrategy1 { get; init; } = true;
    public bool EnableStrategy5 { get; init; } = true;
    public bool EnableStrategy7 { get; init; } = true;
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

    // SecondLeg
    public int SL_FirstLegMaxBars { get; init; } = 15;
    public decimal SL_MinFirstLegAtr { get; init; } = 2.0m;
    public decimal SL_AnchorToleranceAtr { get; init; } = 0.5m;
    public decimal SL_MinPullbackRetrace { get; init; } = 0.33m;
    public decimal SL_MaxPullbackRetrace { get; init; } = 0.62m;
    public bool SL_EnableFakeBreakout { get; init; } = true;
    public decimal SL_EntryBodyMinAtr { get; init; } = 0.25m;
    public decimal SL_StopAtrBuffer { get; init; } = 0.3m;
    public decimal SL_RewardRatio { get; init; } = 2.0m;

    // BrooksPA
    public decimal BrooksPA_SignalBarBodyRatio { get; init; } = 0.5m;
    public decimal BrooksPA_MinBarRangeAtr { get; init; } = 0.3m;
    public int BrooksPA_PullbackLookback { get; init; } = 20;
    public decimal BrooksPA_EmaToleranceAtr { get; init; } = 0.75m;
    public decimal BrooksPA_RewardRatio { get; init; } = 2.0m;
    public int BrooksPA_MaxStopTicks { get; init; } = 40;

    // Shared risk
    public decimal MaxStopPoints { get; init; } = 10m;

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
}
