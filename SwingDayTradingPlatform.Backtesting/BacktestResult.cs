namespace SwingDayTradingPlatform.Backtesting;

public sealed class BacktestResult
{
    public required BacktestParameters Parameters { get; init; }
    public required decimal NetPnL { get; init; }
    public required decimal GrossPnL { get; init; }
    public required decimal TotalCommissions { get; init; }
    public required decimal ReturnPct { get; init; }
    public required int TotalTrades { get; init; }
    public required int WinningTrades { get; init; }
    public required int LosingTrades { get; init; }
    public required int FlattenedTrades { get; init; }
    public required decimal WinRate { get; init; }
    public required decimal ProfitFactor { get; init; }
    public required decimal AvgWinPoints { get; init; }
    public required decimal AvgLossPoints { get; init; }
    public required decimal MaxDrawdown { get; init; }
    public required decimal MaxDrawdownPct { get; init; }
    public required decimal SharpeRatio { get; init; }
    public required decimal SortinoRatio { get; init; }
    public required decimal StartingCapital { get; init; }
    public required decimal EndingCapital { get; init; }

    public required List<EquityPoint> EquityCurve { get; init; }
    public required List<DailyReturn> DailyReturns { get; init; }
    public required List<MonthlyReturn> MonthlyReturns { get; init; }
    public required List<BacktestTrade> Trades { get; init; }

    public string StrategyName { get; init; } = "All";

    // R-based metrics
    public decimal AvgRPerTrade { get; init; }
    public decimal ExpectancyR { get; init; }
    public decimal MaxDrawdownR { get; init; }
    public List<RDistributionBucket> RDistribution { get; init; } = [];

    // Advanced metrics
    public decimal CalmarRatio { get; init; }
    public decimal RecoveryFactor { get; init; }
    public decimal CAGR { get; init; }
    public decimal PayoffRatio { get; init; }
    public int MaxConsecutiveWins { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal AvgHoldTimeMinutes { get; init; }
    public decimal MaxHoldTimeMinutes { get; init; }
    public decimal UlcerIndex { get; init; }
    public decimal TailRatio { get; init; }
    public decimal MfeEfficiency { get; init; }
    public decimal MaeRatio { get; init; }
    public int MaxDrawdownRecoveryBars { get; init; }
    public decimal AvgDrawdownRecoveryBars { get; init; }
}

public sealed record EquityPoint(
    DateTimeOffset Timestamp,
    decimal Equity,
    decimal DrawdownPct);

public sealed record DailyReturn(
    DateOnly Date,
    decimal PnL,
    int TradeCount);

public sealed record MonthlyReturn(
    int Year,
    int Month,
    decimal PnL,
    decimal ReturnPct);

public sealed record BacktestTrade(
    int TradeNumber,
    DateTimeOffset EntryTime,
    DateTimeOffset ExitTime,
    decimal EntryPrice,
    decimal ExitPrice,
    string Direction,
    string ExitReason,
    decimal PnLPoints,
    decimal PnLDollars,
    decimal Commission)
{
    public string StrategyName { get; init; } = "";
    public decimal MAE { get; init; }
    public decimal MFE { get; init; }
    public string EntryReason { get; init; } = "";
    public string ExitReasonDetail { get; init; } = "";
    public decimal RMultiple { get; init; }
    public decimal InitialStopDistance { get; init; }
    public TimeSpan HoldTime => ExitTime - EntryTime;
    public string HoldTimeFormatted => HoldTime.TotalHours >= 1
        ? $"{(int)HoldTime.TotalHours}h {HoldTime.Minutes}m"
        : $"{(int)HoldTime.TotalMinutes}m";
}

public sealed record RDistributionBucket(
    decimal BucketMin,
    decimal BucketMax,
    int Count,
    decimal Pct);

public sealed record DailySummary(
    DateOnly Date,
    int TradeCount,
    int Wins,
    int Losses,
    decimal PnLPoints,
    decimal PnLDollars,
    string StrategyName);

public sealed record VerificationReport(
    int TotalBarsCompared,
    int MatchedBars,
    int MismatchedBars,
    decimal MaxOhlcDiff,
    decimal MaxVwapDiff,
    decimal MaxEmaDiff,
    bool Passed,
    List<BarMismatch> Details);

public sealed record BarMismatch(
    DateTimeOffset OpenTime,
    string Field,
    decimal Expected,
    decimal Actual,
    decimal Diff);
