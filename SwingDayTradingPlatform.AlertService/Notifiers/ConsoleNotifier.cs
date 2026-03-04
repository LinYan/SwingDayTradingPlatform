namespace SwingDayTradingPlatform.AlertService.Notifiers;

/// <summary>
/// Always-on notifier that prints alerts to the console with color coding.
/// </summary>
public sealed class ConsoleNotifier : INotifier
{
    public Task NotifyAsync(Alert alert)
    {
        var color = alert.Type == ExtremaType.MAX ? ConsoleColor.Red : ConsoleColor.Green;
        var prev = Console.ForegroundColor;

        Console.ForegroundColor = color;
        Console.WriteLine(FormatAlert(alert));
        Console.ForegroundColor = prev;

        return Task.CompletedTask;
    }

    private static string FormatAlert(Alert a)
    {
        return $"[ALERT] [{a.EventTime:yyyy-MM-dd HH:mm}] {a.Type} at {a.PivotTime:HH:mm} " +
               $"| close={a.Close} high={a.High} low={a.Low} " +
               $"| EMA{a.EmaPeriod}={a.EmaValue} ({a.EmaDir}) " +
               $"| symbol={a.Symbol}";
    }
}
