using System.Globalization;
using Microsoft.Data.Sqlite;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Backtesting;

public static class SqliteBarStore
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS Bars (
            OpenTimeUtc TEXT PRIMARY KEY,
            CloseTimeUtc TEXT NOT NULL,
            Open REAL NOT NULL,
            High REAL NOT NULL,
            Low REAL NOT NULL,
            Close REAL NOT NULL,
            Volume REAL NOT NULL
        )
        """;

    public static void EnsureCreated(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = CreateTableSql;
        cmd.ExecuteNonQuery();
    }

    public static async Task ImportCsvAsync(string csvPath, string dbPath, CancellationToken ct = default)
    {
        EnsureCreated(dbPath);

        var lines = await File.ReadAllLinesAsync(csvPath, ct);
        if (lines.Length < 2) return;

        var headers = lines[0].Split(',').Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var mapping = DetectColumnMapping(headers);

        using var conn = Open(dbPath);
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO Bars (OpenTimeUtc, CloseTimeUtc, Open, High, Low, Close, Volume)
            VALUES ($open, $close, $o, $h, $l, $c, $v)
            """;
        var pOpen = cmd.Parameters.Add("$open", SqliteType.Text);
        var pClose = cmd.Parameters.Add("$close", SqliteType.Text);
        var pO = cmd.Parameters.Add("$o", SqliteType.Real);
        var pH = cmd.Parameters.Add("$h", SqliteType.Real);
        var pL = cmd.Parameters.Add("$l", SqliteType.Real);
        var pC = cmd.Parameters.Add("$c", SqliteType.Real);
        var pV = cmd.Parameters.Add("$v", SqliteType.Real);

        for (var i = 1; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var fields = lines[i].Split(',');
            if (fields.Length < mapping.MinColumns) continue;

            var (openTime, closeTime) = ParseTimestamps(fields, mapping);
            if (openTime == default) continue;

            pOpen.Value = openTime.ToString("o");
            pClose.Value = closeTime.ToString("o");
            pO.Value = (double)ParseDecimal(fields[mapping.OpenIdx]);
            pH.Value = (double)ParseDecimal(fields[mapping.HighIdx]);
            pL.Value = (double)ParseDecimal(fields[mapping.LowIdx]);
            pC.Value = (double)ParseDecimal(fields[mapping.CloseIdx]);
            pV.Value = mapping.VolumeIdx >= 0 && mapping.VolumeIdx < fields.Length
                ? (double)ParseDecimal(fields[mapping.VolumeIdx])
                : 0.0;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public static async Task<List<MarketBar>> LoadRangeAsync(string dbPath, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        if (!File.Exists(dbPath)) return [];

        var start = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToString("o");
        var end = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).ToString("o");

        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT OpenTimeUtc, CloseTimeUtc, Open, High, Low, Close, Volume FROM Bars WHERE OpenTimeUtc >= $s AND OpenTimeUtc <= $e ORDER BY OpenTimeUtc";
        cmd.Parameters.AddWithValue("$s", start);
        cmd.Parameters.AddWithValue("$e", end);

        var bars = new List<MarketBar>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            bars.Add(new MarketBar(
                DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                (decimal)reader.GetDouble(2),
                (decimal)reader.GetDouble(3),
                (decimal)reader.GetDouble(4),
                (decimal)reader.GetDouble(5),
                (decimal)reader.GetDouble(6)));
        }

        return bars;
    }

    public static int GetBarCount(string dbPath)
    {
        if (!File.Exists(dbPath)) return 0;
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Bars";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static (DateOnly min, DateOnly max) GetDateRange(string dbPath)
    {
        if (!File.Exists(dbPath)) return (default, default);
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MIN(OpenTimeUtc), MAX(OpenTimeUtc) FROM Bars";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0)) return (default, default);

        var minDt = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        var maxDt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
        return (DateOnly.FromDateTime(minDt.DateTime), DateOnly.FromDateTime(maxDt.DateTime));
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
    }

    private sealed record ColumnMapping(
        int OpenTimeIdx, int CloseTimeIdx, int OpenIdx, int HighIdx,
        int LowIdx, int CloseIdx, int VolumeIdx, CsvFormat Format, int MinColumns);

    private enum CsvFormat { Standard, TradingView }

    private static ColumnMapping DetectColumnMapping(string[] headers)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < headers.Length; i++)
            map[headers[i]] = i;

        // Standard format: OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume
        if (map.ContainsKey("opentimeutc") && map.ContainsKey("closetimeutc"))
        {
            return new ColumnMapping(
                map["opentimeutc"], map["closetimeutc"],
                map["open"], map["high"], map["low"], map["close"],
                map.GetValueOrDefault("volume", -1),
                CsvFormat.Standard, 6);
        }

        // TradingView format: time,open,high,low,close,Volume
        if (map.ContainsKey("time"))
        {
            return new ColumnMapping(
                map["time"], -1,
                map["open"], map["high"], map["low"], map["close"],
                map.GetValueOrDefault("volume", -1),
                CsvFormat.TradingView, 5);
        }

        // Fallback: assume positional 0=time,1=open,2=high,3=low,4=close,5=volume
        return new ColumnMapping(0, -1, 1, 2, 3, 4, 5, CsvFormat.TradingView, 5);
    }

    private static (DateTimeOffset openTime, DateTimeOffset closeTime) ParseTimestamps(string[] fields, ColumnMapping mapping)
    {
        if (mapping.Format == CsvFormat.Standard)
        {
            if (DateTimeOffset.TryParse(fields[mapping.OpenTimeIdx].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ot) &&
                DateTimeOffset.TryParse(fields[mapping.CloseTimeIdx].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ct))
            {
                return (ot.ToUniversalTime(), ct.ToUniversalTime());
            }
            return default;
        }

        // TradingView: could be Unix timestamp or datetime string
        var timeStr = fields[mapping.OpenTimeIdx].Trim();

        // Try Unix timestamp (seconds)
        if (long.TryParse(timeStr, out var unix))
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(unix);
            return (dt, dt.AddMinutes(5));
        }

        // Try ISO/datetime parse - assume ET if no offset
        if (DateTimeOffset.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            var utc = parsed.ToUniversalTime();
            return (utc, utc.AddMinutes(5));
        }

        // Try datetime without offset (assume ET)
        if (DateTime.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
        {
            var et = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var offset = et.GetUtcOffset(dt2);
            var dto = new DateTimeOffset(dt2, offset);
            return (dto.ToUniversalTime(), dto.ToUniversalTime().AddMinutes(5));
        }

        return default;
    }

    private static decimal ParseDecimal(string value) =>
        decimal.TryParse(value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
