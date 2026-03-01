using System.Globalization;
using IBApi;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Execution.Ibkr;

public sealed class DownloadProgressEventArgs(int completed, int total, string message) : EventArgs
{
    public int Completed { get; } = completed;
    public int Total { get; } = total;
    public string Message { get; } = message;
}

public sealed class IbkrHistoricalDownloader : DefaultEWrapper, IDisposable
{
    private const int MaxRequestsPer10Min = 55;
    private const int MaxRetries = 3;
    private static readonly TimeSpan PacingWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);

    private readonly IbkrConfig _config;
    private EReaderMonitorSignal? _readerSignal;
    private EClientSocket? _client;
    private EReader? _reader;
    private Thread? _readerThread;
    private volatile bool _disposed;
    private TaskCompletionSource<bool>? _connectedTcs;

    private TaskCompletionSource<List<MarketBar>>? _currentRequestTcs;
    private List<MarketBar> _currentBars = [];
    private readonly object _barsLock = new();
    private int _nextReqId = 5000;
    private readonly Queue<DateTimeOffset> _requestTimestamps = new();

    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    public IbkrHistoricalDownloader(IbkrConfig config)
    {
        _config = config;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _readerSignal = new EReaderMonitorSignal();
        _client = new EClientSocket(this, _readerSignal);
        _client.SetConnectOptions("+PACEAPI");

        // Use a different client ID to avoid conflicts with the trading client
        var downloadClientId = _config.ClientId + 10;

        try
        {
            _client.eConnect(_config.Host, _config.Port, downloadClientId);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to IBKR at {_config.Host}:{_config.Port}. " +
                "Ensure TWS or IB Gateway is running with API connections enabled.", ex);
        }

        if (!_client.IsConnected())
        {
            throw new InvalidOperationException(
                $"Could not connect to IBKR at {_config.Host}:{_config.Port}. " +
                "Ensure TWS or IB Gateway is running and API connections are enabled " +
                "(File > Global Configuration > API > Settings > Enable ActiveX and Socket Clients).");
        }

        _reader = new EReader(_client, _readerSignal);
        _reader.Start();
        _readerThread = new Thread(() =>
        {
            while (!_disposed && _client.IsConnected())
            {
                try
                {
                    _readerSignal.waitForSignal();
                    _reader.processMsgs();
                }
                catch
                {
                    if (_disposed) break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "IbkrHistDownloaderReader"
        };
        _readerThread.Start();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            await _connectedTcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Connected to {_config.Host}:{_config.Port} but IBKR did not respond within 15 seconds. " +
                "Check that the API port is correct and no other client is using ID " + downloadClientId + ".");
        }
    }

    public async Task<List<MarketBar>> DownloadAsync(
        DateOnly startDate,
        DateOnly endDate,
        string csvPath,
        CancellationToken ct)
    {
        if (_client?.IsConnected() != true)
            throw new InvalidOperationException("Not connected to IBKR.");

        // Check for existing data (incremental download)
        DateTimeOffset? latestExisting = null;
        if (File.Exists(csvPath))
        {
            var existingBars = await ReadLatestBarTimeAsync(csvPath, ct);
            latestExisting = existingBars;
            if (latestExisting.HasValue)
            {
                var existingDate = DateOnly.FromDateTime(latestExisting.Value.DateTime);
                if (existingDate >= endDate)
                {
                    ReportProgress(1, 1, "Data already up to date.");
                    return [];
                }
                startDate = existingDate.AddDays(1);
            }
        }

        // Build list of monthly chunks instead of individual days
        var monthChunks = GetMonthlyChunks(startDate, endDate);
        var totalRequests = monthChunks.Count;
        var allBars = new List<MarketBar>();
        var completed = 0;

        ReportProgress(0, totalRequests, $"Downloading {totalRequests} monthly chunks of ES data...");

        foreach (var chunkEnd in monthChunks)
        {
            ct.ThrowIfCancellationRequested();
            await EnsureConnectedAsync(ct);
            await EnforcePacingAsync(ct);

            // Use FUT contract with the active contract month for this chunk date
            var contract = CreateFuturesContract(chunkEnd);

            var endDateTime = chunkEnd.ToDateTime(new TimeOnly(23, 59, 59))
                .ToString("yyyyMMdd-HH:mm:ss", CultureInfo.InvariantCulture);

            var bars = await RequestChunkWithRetryAsync(contract, endDateTime, "1 M", ct);
            if (bars.Count > 0)
                allBars.AddRange(bars);

            completed++;
            ReportProgress(completed, totalRequests, $"Downloaded {completed}/{totalRequests} months. {bars.Count} bars for chunk ending {chunkEnd}.");
        }

        // Sort and deduplicate
        allBars = allBars
            .OrderBy(b => b.OpenTimeUtc)
            .GroupBy(b => b.OpenTimeUtc)
            .Select(g => g.First())
            .ToList();

        // Filter to requested range (use DateTimeOffset in UTC for consistent comparison)
        var rangeStart = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(endDate.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);
        allBars = allBars
            .Where(b => b.OpenTimeUtc >= rangeStart && b.OpenTimeUtc <= rangeEnd)
            .ToList();

        // Write to CSV
        if (allBars.Count > 0)
        {
            if (latestExisting.HasValue)
            {
                // Append mode - filter out bars we already have
                var newBars = allBars.Where(b => b.CloseTimeUtc > latestExisting.Value).ToList();
                await AppendBarsToCsvAsync(csvPath, newBars, ct);
                ReportProgress(totalRequests, totalRequests, $"Appended {newBars.Count} new bars to {csvPath}.");
            }
            else
            {
                await WriteBarsToCsvAsync(csvPath, allBars, ct);
                ReportProgress(totalRequests, totalRequests, $"Wrote {allBars.Count} bars to {csvPath}.");
            }
        }

        return allBars;
    }

    private async Task<List<MarketBar>> RequestChunkWithRetryAsync(
        Contract contract, string endDateTime, string duration, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await RequestBarsAsync(contract, endDateTime, duration, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout — retry with backoff
                if (attempt < MaxRetries)
                {
                    var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    ReportProgress(-1, -1, $"Request timed out, retrying in {backoff.TotalSeconds:0}s (attempt {attempt}/{MaxRetries})...");
                    await Task.Delay(backoff, ct);
                    await EnsureConnectedAsync(ct);
                }
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                ReportProgress(-1, -1, $"Error: {ex.Message}. Retrying in {backoff.TotalSeconds:0}s (attempt {attempt}/{MaxRetries})...");
                await Task.Delay(backoff, ct);
                await EnsureConnectedAsync(ct);
            }
        }

        // Final attempt failed — return empty
        return [];
    }

    private async Task<List<MarketBar>> RequestBarsAsync(
        Contract contract, string endDateTime, string duration, CancellationToken ct)
    {
        lock (_barsLock) { _currentBars = []; }
        _currentRequestTcs = new TaskCompletionSource<List<MarketBar>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reqId = Interlocked.Increment(ref _nextReqId);

        _client!.reqHistoricalData(
            reqId,
            contract,
            endDateTime,
            duration,
            "5 mins",
            "TRADES",
            1, // useRTH
            1, // formatDate
            false,
            []);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RequestTimeout);

        var result = await _currentRequestTcs.Task.WaitAsync(timeoutCts.Token);

        // Only count successful requests with data toward the pacing limit
        if (result.Count > 0)
            _requestTimestamps.Enqueue(DateTimeOffset.UtcNow);

        return result;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client?.IsConnected() == true)
            return;

        ReportProgress(-1, -1, "Reconnecting to IBKR...");
        // Clean up old connection
        if (_client is not null)
        {
            try { _client.eDisconnect(); } catch { /* ignore */ }
        }

        await ConnectAsync(ct);
        ReportProgress(-1, -1, "Reconnected.");
    }

    private async Task EnforcePacingAsync(CancellationToken ct)
    {
        // Remove timestamps older than the pacing window
        var cutoff = DateTimeOffset.UtcNow - PacingWindow;
        while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < cutoff)
            _requestTimestamps.Dequeue();

        if (_requestTimestamps.Count >= MaxRequestsPer10Min)
        {
            var oldest = _requestTimestamps.Peek();
            var waitUntil = oldest + PacingWindow;
            var delay = waitUntil - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                ReportProgress(-1, -1, $"Pacing limit reached. Waiting {delay.TotalSeconds:0}s...");
                await Task.Delay(delay, ct);
            }
        }
    }

    public override void nextValidId(int orderId)
    {
        _connectedTcs?.TrySetResult(true);
    }

    public override void historicalData(int reqId, Bar bar)
    {
        DateTimeOffset openTime;
        try { openTime = ParseBarTime(bar.Time); }
        catch (FormatException) { return; } // Skip bars with unparseable timestamps

        var closeTime = openTime.AddMinutes(5);

        var marketBar = new MarketBar(
            openTime,
            closeTime,
            (decimal)bar.Open,
            (decimal)bar.High,
            (decimal)bar.Low,
            (decimal)bar.Close,
            (decimal)bar.Volume);

        lock (_barsLock) { _currentBars.Add(marketBar); }
    }

    public override void historicalDataEnd(int reqId, string start, string end)
    {
        List<MarketBar> snapshot;
        lock (_barsLock) { snapshot = new List<MarketBar>(_currentBars); }
        _currentRequestTcs?.TrySetResult(snapshot);
    }

    public override void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
    {
        // Informational / warning messages
        if (errorCode is 2104 or 2106 or 2158 or 2174)
            return;

        // Historical data errors or recoverable errors — return empty
        // 200 = no security definition (expired/unavailable contract)
        if (errorCode is 162 or 200 or 321 or 366 or 504 or 1100 or 1300)
        {
            _currentRequestTcs?.TrySetResult([]);
            return;
        }

        _currentRequestTcs?.TrySetException(new Exception($"IBKR error {errorCode}: {errorMsg}"));
    }

    public override void error(Exception e)
    {
        _currentRequestTcs?.TrySetException(e);
    }

    public override void error(string str)
    {
        if (!string.IsNullOrEmpty(str))
            _connectedTcs?.TrySetException(new InvalidOperationException($"IBKR connection error: {str}"));
    }

    public override void connectAck()
    {
        if (_client?.AsyncEConnect == true)
            _client.startApi();
    }

    public override void connectionClosed()
    {
        _connectedTcs?.TrySetException(new Exception("Connection closed during download."));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_client?.IsConnected() == true)
            _client.eDisconnect();
    }

    private void ReportProgress(int completed, int total, string message) =>
        ProgressChanged?.Invoke(this, new DownloadProgressEventArgs(completed, total, message));

    /// <summary>
    /// Creates a FUT contract for the ES quarterly expiry that was active on the given date.
    /// ES quarterly months: March (H), June (M), September (U), December (Z).
    /// Each contract is the front month until roughly the 3rd Friday of its expiry month,
    /// so we roll to the next quarter if we're past the 15th of the expiry month.
    /// IncludeExpired = true because we download historical data for past contracts.
    /// </summary>
    private static Contract CreateFuturesContract(DateOnly date)
    {
        var month = date.Month;
        var year = date.Year;

        // Find the current or next quarterly month (3, 6, 9, 12)
        int quarterMonth = month <= 3 ? 3 : month <= 6 ? 6 : month <= 9 ? 9 : 12;

        // If we're past the 15th of the expiry month, the next quarter is the front month
        if (month == quarterMonth && date.Day > 15)
        {
            quarterMonth += 3;
            if (quarterMonth > 12)
            {
                quarterMonth = 3;
                year++;
            }
        }

        return new Contract
        {
            Symbol = "ES",
            SecType = "FUT",
            Exchange = "CME",
            Currency = "USD",
            Multiplier = "50",
            LastTradeDateOrContractMonth = $"{year}{quarterMonth:00}",
            IncludeExpired = true
        };
    }

    private static List<DateOnly> GetMonthlyChunks(DateOnly start, DateOnly end)
    {
        var chunks = new List<DateOnly>();
        // Each chunk ends at the last day of a month (or the end date)
        var current = start;
        while (current <= end)
        {
            var monthEnd = new DateOnly(current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month));
            if (monthEnd > end)
                monthEnd = end;
            chunks.Add(monthEnd);
            // Move to first day of next month
            current = monthEnd.AddDays(1);
        }
        return chunks;
    }

    // Map IBKR IANA-style timezone IDs to .NET TimeZoneInfo IDs
    private static readonly Dictionary<string, string> IbkrTzMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US/Central"] = "America/Chicago",
        ["US/Eastern"] = "America/New_York",
        ["US/Pacific"] = "America/Los_Angeles",
        ["US/Mountain"] = "America/Denver",
        ["UTC"] = "UTC",
        ["GMT"] = "UTC",
        ["Europe/London"] = "Europe/London",
        ["Asia/Tokyo"] = "Asia/Tokyo",
        ["Asia/Hong_Kong"] = "Asia/Hong_Kong",
    };

    private static DateTimeOffset ParseBarTime(string time)
    {
        if (DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return dto;

        // Handle IBKR format: "yyyyMMdd HH:mm:ss TIMEZONE" (e.g., "20260102 08:30:00 US/Central")
        var parts = time.Split(' ', 3);
        if (parts.Length == 3 && parts[0].Length == 8)
        {
            var dateTimePart = parts[0] + " " + parts[1];
            var tzPart = parts[2].Trim();

            if (DateTime.TryParseExact(dateTimePart, "yyyyMMdd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                var tzId = IbkrTzMap.GetValueOrDefault(tzPart, tzPart);
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
                    var offset = tz.GetUtcOffset(dt);
                    return new DateTimeOffset(dt, offset);
                }
                catch (TimeZoneNotFoundException)
                {
                    // Unknown timezone — assume UTC
                    return new DateTimeOffset(dt, TimeSpan.Zero);
                }
            }
        }

        if (DateTime.TryParseExact(time, "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
            return new DateTimeOffset(local);

        if (DateTime.TryParseExact(time, "yyyyMMdd  HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var localDouble))
            return new DateTimeOffset(localDouble);

        if (DateTime.TryParseExact(time, "yyyyMMdd-HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var utc))
            return new DateTimeOffset(utc, TimeSpan.Zero);

        throw new FormatException($"Unable to parse IBKR bar time: '{time}'");
    }

    private static async Task<DateTimeOffset?> ReadLatestBarTimeAsync(string path, CancellationToken ct)
    {
        DateTimeOffset? latest = null;
        using var reader = new StreamReader(path);
        await reader.ReadLineAsync(ct); // skip header
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var parts = line.Split(',');
            if (parts.Length >= 2 && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var closeTime))
                latest = closeTime;
        }
        return latest;
    }

    private static async Task WriteBarsToCsvAsync(string path, List<MarketBar> bars, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var writer = new StreamWriter(path, append: false);
        await writer.WriteLineAsync("OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume");
        foreach (var bar in bars)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"{bar.OpenTimeUtc:O},{bar.CloseTimeUtc:O},{bar.Open},{bar.High},{bar.Low},{bar.Close},{bar.Volume:0}"));
        }
    }

    private static async Task AppendBarsToCsvAsync(string path, List<MarketBar> bars, CancellationToken ct)
    {
        await using var writer = new StreamWriter(path, append: true);
        foreach (var bar in bars)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"{bar.OpenTimeUtc:O},{bar.CloseTimeUtc:O},{bar.Open},{bar.High},{bar.Low},{bar.Close},{bar.Volume:0}"));
        }
    }
}
