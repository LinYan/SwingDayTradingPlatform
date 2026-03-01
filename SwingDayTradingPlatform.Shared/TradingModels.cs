using System.Text.Json.Serialization;

namespace SwingDayTradingPlatform.Shared;

public enum BrokerConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Faulted
}

public enum StrategyRunState
{
    Stopped,
    Running,
    Halted,
    Flattening
}

public enum OrderSide
{
    Buy,
    Sell
}

public enum OrderIntent
{
    Entry,
    StopLoss,
    TakeProfit,
    Flatten,
    Cancel
}

public enum PositionSide
{
    Flat,
    Long,
    Short
}

public enum LogCategory
{
    System,
    Connection,
    Strategy,
    Risk,
    Order,
    Execution,
    MarketData
}

public sealed record MarketBar(
    DateTimeOffset OpenTimeUtc,
    DateTimeOffset CloseTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

public sealed record AccountSnapshot(
    decimal NetLiquidation,
    decimal AvailableFunds,
    decimal BuyingPower,
    decimal RealizedPnL,
    decimal UnrealizedPnL,
    string Currency);

public sealed record StrategySignal(
    string SignalId,
    DateTimeOffset BarTimeUtc,
    PositionSide Direction,
    decimal EntryPrice,
    decimal StopPrice,
    decimal? TargetPrice,
    string Reason,
    bool IsFlattenSignal = false);

public sealed record OrderTicket(
    int LocalOrderId,
    string SignalId,
    string Symbol,
    string ContractMonth,
    OrderSide Side,
    OrderIntent Intent,
    int Quantity,
    decimal? LimitPrice,
    decimal? StopPrice,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string? BrokerOrderId = null,
    string? ParentOrderKey = null);

public sealed record ExecutionFill(
    string ExecutionId,
    int LocalOrderId,
    string Symbol,
    int Quantity,
    decimal Price,
    DateTimeOffset TimestampUtc,
    string Side);

public sealed record PositionSnapshot(
    string Symbol,
    string ContractMonth,
    PositionSide Side,
    int Quantity,
    decimal AveragePrice,
    decimal MarketPrice,
    decimal UnrealizedPnL,
    DateTimeOffset UpdatedAtUtc);

public sealed record StrategyStatusSnapshot(
    StrategyRunState RunState,
    string LatestSignal,
    string LatestReason,
    bool TradingHalted,
    bool KillSwitchArmed,
    DateTimeOffset? NextFlattenUtc,
    bool DayTradingComplete,
    int BarsProcessed,
    DateOnly TradingDate);

public sealed record LogEntry(
    DateTimeOffset TimestampUtc,
    LogCategory Category,
    string Message);

public sealed class AppConfig
{
    public IbkrConfig Ibkr { get; init; } = new();
    public TradingConfig Trading { get; init; } = new();
    public StrategyConfig Strategy { get; init; } = new();
    public RiskConfig Risk { get; init; } = new();
    public StorageConfig Storage { get; init; } = new();
    public SimulationConfig Simulation { get; init; } = new();
    public MultiStrategyConfig MultiStrategy { get; init; } = new();
}

public sealed class IbkrConfig
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 7497;
    public int ClientId { get; init; } = 101;
    public bool PaperMode { get; init; } = true;
    public bool UseSimulator { get; init; } = true;
    public string ContractMonth { get; init; } = "";
    public string OptionalIbApiPath { get; init; } = "lib\\IBApi.dll";
}

public sealed class TradingConfig
{
    public string Symbol { get; init; } = "ES";
    public string Exchange { get; init; } = "CME";
    public string Currency { get; init; } = "USD";
    public string Timezone { get; init; } = "America/New_York";
    public string EntryWindowStart { get; init; } = "09:40";
    public string EntryWindowEnd { get; init; } = "15:50";
    public string FlattenTime { get; init; } = "15:55";
    public bool AllowReentryAfterFlatten { get; init; } = false;
    public decimal PointValue { get; init; } = 50m;
    public string BarResolution { get; init; } = "5m";
}

public sealed class StrategyConfig
{
    public int FastEmaPeriod { get; init; } = 20;
    public int SlowEmaPeriod { get; init; } = 50;
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrMultiplier { get; init; } = 1.5m;
    public decimal RewardRiskRatio { get; init; } = 1.5m;
    public decimal PullbackTolerancePct { get; init; } = 0.0015m;
    public bool EnableTrailingStop { get; init; } = true;
    public string BarResolution { get; init; } = "5m";
}

public sealed class RiskConfig
{
    public decimal MaxDailyLoss { get; init; } = 150m;
    public decimal RiskPerTradePct { get; init; } = 0m;
    public int MaxContracts { get; init; } = 1;
    public int FixedContracts { get; init; } = 1;
    public int MaxTradesPerDay { get; init; } = 5;
    public int MaxLossesPerDay { get; init; } = 3;
    public decimal MaxStopPoints { get; init; } = 10m;
    public int CooldownSeconds { get; init; } = 60;
    public bool UseUnrealizedPnLForDailyLimit { get; init; } = true;
}

public sealed class StorageConfig
{
    public string BasePath { get; init; } = "data";
    public string OrdersFile { get; init; } = "orders.json";
    public string ExecutionsFile { get; init; } = "executions.json";
    public string StrategyStateFile { get; init; } = "strategy-state.json";
    public string LogsFile { get; init; } = "logs.json";
}

public sealed class SimulationConfig
{
    public decimal StartingPrice { get; init; } = 5000m;
    public decimal BarVolatility { get; init; } = 8m;
    public int TickSeconds { get; init; } = 1;
}

public sealed record class PersistedState
{
    public List<OrderTicket> Orders { get; init; } = [];
    public List<ExecutionFill> Executions { get; init; } = [];
    public HashSet<string> TriggeredSignalIds { get; init; } = [];
    public bool TradingHalted { get; init; }
    public bool KillSwitchArmed { get; init; }
    public string LastFlattenDate { get; init; } = "";
    public int DailyTradeCount { get; init; }
    public int DailyLossCount { get; init; }
}

public sealed class PlaceBracketRequest
{
    public required string SignalId { get; init; }
    public required string Symbol { get; init; }
    public required string ContractMonth { get; init; }
    public required OrderSide Side { get; init; }
    public required int Quantity { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal StopPrice { get; init; }
    public decimal? TakeProfitPrice { get; init; }
    public bool UseMarketEntry { get; init; } = true;
}

public sealed class ConnectionChangedEventArgs(BrokerConnectionState state, string message)
    : EventArgs
{
    public BrokerConnectionState State { get; } = state;
    public string Message { get; } = message;
}

public sealed class OrderStatusEventArgs(OrderTicket order) : EventArgs
{
    public OrderTicket Order { get; } = order;
}

public sealed class ExecutionEventArgs(ExecutionFill fill) : EventArgs
{
    public ExecutionFill Fill { get; } = fill;
}

public sealed class PositionEventArgs(PositionSnapshot position) : EventArgs
{
    public PositionSnapshot Position { get; } = position;
}

public sealed class AccountSummaryEventArgs(AccountSnapshot snapshot) : EventArgs
{
    public AccountSnapshot Snapshot { get; } = snapshot;
}

public sealed record TradeMarker(
    DateTimeOffset Time,
    decimal Price,
    bool IsEntry,
    bool IsLong,
    string Tooltip);
