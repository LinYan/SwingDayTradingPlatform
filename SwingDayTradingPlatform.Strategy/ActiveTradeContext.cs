using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy;

public sealed class ActiveTradeContext
{
    public required string OwningStrategyName { get; init; }
    public required PositionSide Direction { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal StopPrice { get; init; }
    public decimal? TargetPrice { get; init; }
    public required MarketBar EntryBar { get; init; }
    public MarketBar PreviousBar { get; set; } = null!;

    // Trailing stop state
    public decimal TrailingStopLevel { get; set; }
    public decimal HighestHighSinceEntry { get; set; }
    public decimal LowestLowSinceEntry { get; set; }
    public int BarsSinceEntry { get; set; }

    // Break-even stop
    public decimal RiskAmount { get; init; }
    public bool BreakEvenActivated { get; set; }
}
