namespace SwingDayTradingPlatform.AlertService;

/// <summary>
/// A single 5-minute OHLC bar. Timestamp is the bar close time in Eastern Time.
/// </summary>
public sealed record Bar(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close);

/// <summary>
/// Produces a continuous stream of completed 5-minute bars.
/// Implementations: CsvBarFeed (replay), IbBarFeed (realtime via IB Gateway).
/// </summary>
public interface IBarFeed : IAsyncDisposable
{
    event Action<Bar>? BarClosed;
    Task RunAsync(CancellationToken ct);
}
