using System.Globalization;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Backtesting;

public static class CsvBarStorage
{
    private const string Header = "OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume";

    public static async Task WriteAsync(string path, IEnumerable<MarketBar> bars, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var writer = new StreamWriter(path, append: false);
        await writer.WriteLineAsync(Header);
        foreach (var bar in bars)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(FormatBar(bar));
        }
    }

    public static async Task AppendAsync(string path, IEnumerable<MarketBar> bars, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var fileExists = File.Exists(path) && new FileInfo(path).Length > 0;
        await using var writer = new StreamWriter(path, append: true);
        if (!fileExists)
            await writer.WriteLineAsync(Header);

        foreach (var bar in bars)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(FormatBar(bar));
        }
    }

    public static async Task<List<MarketBar>> ReadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return [];

        var bars = new List<MarketBar>();
        using var reader = new StreamReader(path);
        var header = await reader.ReadLineAsync(ct);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (TryParseLine(line, out var bar))
                bars.Add(bar);
        }

        return bars;
    }

    public static async Task<List<MarketBar>> ReadRangeAsync(string path, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return [];

        var startDto = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endDto = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var bars = new List<MarketBar>();
        using var reader = new StreamReader(path);
        var header = await reader.ReadLineAsync(ct);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (TryParseLine(line, out var bar) && bar.OpenTimeUtc >= startDto && bar.OpenTimeUtc <= endDto)
                bars.Add(bar);
        }

        return bars;
    }

    public static DateTimeOffset? GetLatestBarTime(string path)
    {
        if (!File.Exists(path))
            return null;

        DateTimeOffset? latest = null;
        using var reader = new StreamReader(path);
        reader.ReadLine(); // skip header
        while (reader.ReadLine() is { } line)
        {
            if (TryParseLine(line, out var bar))
                latest = bar.CloseTimeUtc;
        }

        return latest;
    }

    private static string FormatBar(MarketBar bar) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{bar.OpenTimeUtc:O},{bar.CloseTimeUtc:O},{bar.Open},{bar.High},{bar.Low},{bar.Close},{bar.Volume:0}");

    private static bool TryParseLine(string line, out MarketBar bar)
    {
        bar = default!;
        var parts = line.Split(',');
        if (parts.Length < 7)
            return false;

        if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var openTime))
            return false;
        if (!DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var closeTime))
            return false;
        if (!decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var open))
            return false;
        if (!decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var high))
            return false;
        if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var low))
            return false;
        if (!decimal.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
            return false;
        if (!decimal.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
            return false;

        bar = new MarketBar(openTime, closeTime, open, high, low, close, volume);
        return true;
    }
}
