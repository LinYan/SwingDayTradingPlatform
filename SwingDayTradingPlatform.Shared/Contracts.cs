namespace SwingDayTradingPlatform.Shared;

public interface IBrokerAdapter : IAsyncDisposable
{
    event EventHandler<ConnectionChangedEventArgs>? ConnectionChanged;
    event EventHandler<OrderStatusEventArgs>? OrderStatusChanged;
    event EventHandler<ExecutionEventArgs>? ExecutionReceived;
    event EventHandler<PositionEventArgs>? PositionChanged;
    event EventHandler<AccountSummaryEventArgs>? AccountSummaryReceived;

    BrokerConnectionState ConnectionState { get; }
    IReadOnlyCollection<OrderTicket> ActiveOrders { get; }
    IReadOnlyCollection<PositionSnapshot> Positions { get; }
    AccountSnapshot? LatestAccountSnapshot { get; }

    Task ConnectAsync(CancellationToken cancellationToken);
    Task SyncAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OrderTicket>> PlaceBracketAsync(PlaceBracketRequest request, CancellationToken cancellationToken);
    Task CancelAllAsync(CancellationToken cancellationToken);
    Task FlattenAsync(string symbol, string contractMonth, CancellationToken cancellationToken);
}

public interface IBarFeed : IAsyncDisposable
{
    event EventHandler<MarketBar>? BarClosed;
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IOrderStateStore
{
    Task<PersistedState> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(PersistedState state, CancellationToken cancellationToken);
    Task AppendLogAsync(LogEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<LogEntry>> LoadLogsAsync(CancellationToken cancellationToken);
}
