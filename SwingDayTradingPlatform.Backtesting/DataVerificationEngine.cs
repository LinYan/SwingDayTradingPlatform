using System.Globalization;
using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Strategy;

namespace SwingDayTradingPlatform.Backtesting;

public static class DataVerificationEngine
{
    public static async Task<VerificationReport> VerifyAgainstTradingView(
        string tvCsvPath,
        string dbPath,
        decimal toleranceOhlc = 0.25m,
        decimal toleranceIndicator = 1.0m,
        CancellationToken ct = default)
    {
        var tvBars = await LoadTradingViewCsv(tvCsvPath, ct);
        if (tvBars.Count == 0)
            return new VerificationReport(0, 0, 0, 0, 0, 0, false, []);

        var minDate = DateOnly.FromDateTime(tvBars[0].OpenTimeUtc.DateTime);
        var maxDate = DateOnly.FromDateTime(tvBars[^1].OpenTimeUtc.DateTime);
        var dbBars = await SqliteBarStore.LoadRangeAsync(dbPath, minDate, maxDate, ct);

        // Build lookup by open time (rounded to minute)
        var dbLookup = new Dictionary<long, MarketBar>();
        foreach (var bar in dbBars)
            dbLookup[RoundToMinute(bar.OpenTimeUtc)] = bar;

        var details = new List<BarMismatch>();
        var matched = 0;
        var missing = 0;
        var maxOhlcDiff = 0m;

        foreach (var tvBar in tvBars)
        {
            ct.ThrowIfCancellationRequested();
            var key = RoundToMinute(tvBar.OpenTimeUtc);
            if (!dbLookup.TryGetValue(key, out var dbBar))
            {
                missing++;
                continue;
            }

            matched++;
            CheckField(tvBar.OpenTimeUtc, "Open", tvBar.Open, dbBar.Open, toleranceOhlc, details, ref maxOhlcDiff);
            CheckField(tvBar.OpenTimeUtc, "High", tvBar.High, dbBar.High, toleranceOhlc, details, ref maxOhlcDiff);
            CheckField(tvBar.OpenTimeUtc, "Low", tvBar.Low, dbBar.Low, toleranceOhlc, details, ref maxOhlcDiff);
            CheckField(tvBar.OpenTimeUtc, "Close", tvBar.Close, dbBar.Close, toleranceOhlc, details, ref maxOhlcDiff);
        }

        // Compute VWAP/EMA comparison on a subset
        var et = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var tvVwap = Indicators.SessionVwap(tvBars, et);
        var dbVwap = Indicators.SessionVwap(dbBars, et);
        var maxVwapDiff = 0m;
        var maxEmaDiff = 0m;

        var tvCloses = tvBars.Select(b => b.Close).ToList();
        var dbCloses = dbBars.Select(b => b.Close).ToList();
        var tvEma = Indicators.EmaSeries(tvCloses, 20);
        var dbEma = Indicators.EmaSeries(dbCloses, 20);

        // Sample comparison at every 50th bar
        for (var i = 0; i < Math.Min(tvVwap.Count, dbVwap.Count); i += 50)
        {
            var vwapDiff = Math.Abs(tvVwap[i] - dbVwap[i]);
            if (vwapDiff > maxVwapDiff) maxVwapDiff = vwapDiff;

            if (i < tvEma.Count && i < dbEma.Count)
            {
                var emaDiff = Math.Abs(tvEma[i] - dbEma[i]);
                if (emaDiff > maxEmaDiff) maxEmaDiff = emaDiff;
            }
        }

        var mismatchedBarCount = details.Select(d => d.OpenTime).Distinct().Count();
        // Allow up to 5% missing bars before failing (different data sources may have slight gaps)
        var missingPct = tvBars.Count > 0 ? (decimal)missing / tvBars.Count * 100m : 0m;
        var passed = mismatchedBarCount == 0 && missingPct <= 5m &&
                     maxVwapDiff <= toleranceIndicator && maxEmaDiff <= toleranceIndicator;

        return new VerificationReport(
            tvBars.Count, matched, mismatchedBarCount,
            maxOhlcDiff, maxVwapDiff, maxEmaDiff, passed, details);
    }

    private static void CheckField(DateTimeOffset time, string field, decimal expected, decimal actual,
        decimal tolerance, List<BarMismatch> details, ref decimal maxDiff)
    {
        var diff = Math.Abs(expected - actual);
        if (diff > maxDiff) maxDiff = diff;
        if (diff > tolerance)
            details.Add(new BarMismatch(time, field, expected, actual, diff));
    }

    private static long RoundToMinute(DateTimeOffset dt) =>
        new DateTimeOffset(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Offset).ToUnixTimeSeconds();

    private static async Task<List<MarketBar>> LoadTradingViewCsv(string path, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(path, ct);
        if (lines.Length < 2) return [];

        var bars = new List<MarketBar>();
        var et = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        for (var i = 1; i < lines.Length; i++)
        {
            var fields = lines[i].Split(',');
            if (fields.Length < 5) continue;

            var timeStr = fields[0].Trim();
            DateTimeOffset openTime;

            if (long.TryParse(timeStr, out var unix))
            {
                openTime = DateTimeOffset.FromUnixTimeSeconds(unix);
            }
            else if (DateTime.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                var offset = et.GetUtcOffset(dt);
                openTime = new DateTimeOffset(dt, offset).ToUniversalTime();
            }
            else continue;

            if (!decimal.TryParse(fields[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var o)) continue;
            if (!decimal.TryParse(fields[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var h)) continue;
            if (!decimal.TryParse(fields[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l)) continue;
            if (!decimal.TryParse(fields[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var c)) continue;
            var vol = fields.Length > 5 && decimal.TryParse(fields[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

            bars.Add(new MarketBar(openTime, openTime.AddMinutes(5), o, h, l, c, vol));
        }

        return bars;
    }
}
