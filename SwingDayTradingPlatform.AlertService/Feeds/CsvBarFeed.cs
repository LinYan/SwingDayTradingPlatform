using System.Globalization;

namespace SwingDayTradingPlatform.AlertService.Feeds;

/// <summary>
/// Replays bars from a local CSV file for debugging / backtesting the extrema detector.
/// Expected CSV format (with header): timestamp,open,high,low,close
/// Timestamp formats accepted: "yyyy-MM-dd HH:mm:ss", "MM/dd/yyyy HH:mm:ss", ISO 8601.
/// </summary>
public sealed class CsvBarFeed : IBarFeed
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssZ",
        "M/d/yyyy H:mm:ss",
        "M/d/yyyy H:mm",
        "MM/dd/yyyy HH:mm:ss",
        "MM/dd/yyyy HH:mm",
        "yyyyMMdd HH:mm:ss",
        "yyyyMMdd  HH:mm:ss"
    ];

    private readonly string _filePath;

    public CsvBarFeed(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public event Action<Bar>? BarClosed;

    public Task RunAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            throw new FileNotFoundException($"Replay CSV not found: {_filePath}");

        Console.WriteLine($"[REPLAY] Loading bars from {Path.GetFullPath(_filePath)}");

        var lineNumber = 0;
        var barCount = 0;

        foreach (var line in File.ReadLines(_filePath))
        {
            if (ct.IsCancellationRequested)
                break;

            lineNumber++;

            // Skip header or empty lines
            if (lineNumber == 1 && line.Contains("timestamp", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 5)
            {
                Console.WriteLine($"[REPLAY] Skipping line {lineNumber}: insufficient columns");
                continue;
            }

            if (!TryParseTimestamp(parts[0].Trim(), out var timestamp))
            {
                Console.WriteLine($"[REPLAY] Skipping line {lineNumber}: cannot parse timestamp '{parts[0].Trim()}'");
                continue;
            }

            if (!decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var open) ||
                !decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var high) ||
                !decimal.TryParse(parts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var low) ||
                !decimal.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
            {
                Console.WriteLine($"[REPLAY] Skipping line {lineNumber}: cannot parse OHLC values");
                continue;
            }

            barCount++;
            var bar = new Bar(timestamp, open, high, low, close);
            BarClosed?.Invoke(bar);
        }

        Console.WriteLine($"[REPLAY] Complete. {barCount} bars replayed from {lineNumber} lines.");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static bool TryParseTimestamp(string raw, out DateTime result)
    {
        // Try standard formats first
        if (DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out result))
            return true;

        // Fallback: general parse
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;

        // Unix timestamp (seconds)
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            result = DateTimeOffset.FromUnixTimeSeconds(unix).DateTime;
            return true;
        }

        result = default;
        return false;
    }
}
