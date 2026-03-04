namespace SwingDayTradingPlatform.AlertService;

public enum ExtremaType { MAX, MIN }

public enum EmaDirection { UP, DOWN, FLAT, NA }

/// <summary>
/// An extrema alert emitted when a local MAX or MIN is confirmed.
/// </summary>
public sealed record Alert(
    DateTime EventTime,
    DateTime PivotTime,
    ExtremaType Type,
    string Symbol,
    decimal Close,
    decimal High,
    decimal Low,
    int EmaPeriod,
    decimal EmaValue,
    EmaDirection EmaDir,
    string Mode);

/// <summary>
/// Consumes bars and emits Alert events when an extremum is detected.
/// </summary>
public interface IExtremaDetector
{
    event Action<Alert>? AlertDetected;
    void OnBar(Bar bar);
    int BarCount { get; }
}

/// <summary>
/// Sends alert notifications (console, Telegram, etc.).
/// </summary>
public interface INotifier
{
    Task NotifyAsync(Alert alert);
}
