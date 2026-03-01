namespace SwingDayTradingPlatform.Shared;

public sealed class MultiStrategyConfig
{
    public int FastEmaPeriod { get; init; } = 20;
    public int SlowEmaPeriod { get; init; } = 50;
    public int AtrPeriod { get; init; } = 14;

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

    // RSI
    public int RsiPeriod { get; init; } = 14;

    // EmaPullback enhancements
    public decimal EmaPullbackRewardRatio { get; init; } = 2.0m;
    public decimal EmaPullbackTolerance { get; init; } = 0.5m;
    public decimal EmaMinSlopeAtr { get; init; } = 0.05m;
    public decimal EmaBodyMinAtrRatio { get; init; } = 0.3m;
    public decimal EmaRsiLongMin { get; init; } = 40m;
    public decimal EmaRsiLongMax { get; init; } = 70m;
    public decimal EmaRsiShortMin { get; init; } = 30m;
    public decimal EmaRsiShortMax { get; init; } = 60m;
    public decimal EmaStopAtrBuffer { get; init; } = 0.25m;

    // SRReversal enhancements
    public int SRMinTouches { get; init; } = 3;
    public decimal SRReversalRewardRatio { get; init; } = 2.0m;
    public int SRMaxFreshnessBarsSinceTouch { get; init; } = 40;
    public decimal SRMinReversalRangeAtr { get; init; } = 0.3m;
    public int SRCounterTrendMinTouches { get; init; } = 5;

    // Momentum enhancements
    public decimal MomentumRewardRatio { get; init; } = 2.5m;
    public int MomentumPullbackWindowBars { get; init; } = 10;
    public decimal MomentumAvgBodyAtrRatio { get; init; } = 0.6m;
    public int MomentumMaxModerate { get; init; } = 1;
    public decimal MomentumModerateMinRatio { get; init; } = 0.4m;
    public decimal MomentumPullbackMinRetrace { get; init; } = 0.25m;
    public decimal MomentumPullbackMaxRetrace { get; init; } = 0.75m;

    // FiftyPctPullback
    public decimal MaxStopPoints { get; init; } = 10m;
    public int BigMoveStaleBars { get; init; } = 20;
    public decimal FiftyPctRetracementMin { get; init; } = 0.30m;
    public decimal FiftyPctRetracementMax { get; init; } = 0.55m;
    public decimal FiftyPctEntryBodyMinAtr { get; init; } = 0.25m;

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
    public decimal BreakEvenActivationR { get; init; } = 1.0m;
}
