using System.Globalization;
using IBApi;

namespace SwingDayTradingPlatform.AlertService.Feeds;

/// <summary>
/// Connects to IB Gateway / TWS and streams 5-minute bars via reqHistoricalData with keepUpToDate=true.
/// Historical backfill is included automatically. Handles disconnect with exponential-backoff reconnect.
/// </summary>
public sealed class IbBarFeed : DefaultEWrapper, IBarFeed
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _clientId;
    private readonly string _symbol;
    private readonly string _exchange;
    private readonly TimeZoneInfo _easternTz;
    private readonly object _connectionGate = new();

    private EReaderMonitorSignal? _readerSignal;
    private EClientSocket? _client;
    private EReader? _reader;
    private Thread? _readerThread;
    private TaskCompletionSource<bool>? _connectedTcs;
    private CancellationTokenSource? _reconnectCts;
    private CancellationTokenSource? _runCts;
    private volatile bool _disposed;
    private volatile bool _manualDisconnect;
    private volatile bool _isReconnecting;
    private int _requestId = 5001;
    private Bar? _pendingBar;
    private string _connectionStatus = "Disconnected";

    public IbBarFeed(string host, int port, int clientId, string symbol, string exchange)
    {
        _host = host;
        _port = port;
        _clientId = clientId;
        _symbol = symbol;
        _exchange = exchange;
        _easternTz = ResolveTimeZone("America/New_York");
    }

    public event Action<Bar>? BarClosed;
    public event Action<string>? StatusChanged;

    public string ConnectionStatus => _connectionStatus;

    public async Task RunAsync(CancellationToken ct)
    {
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await ConnectAndSubscribeAsync(ct);

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, _runCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _manualDisconnect = true;
        _runCts?.Cancel();
        _runCts?.Dispose();
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        if (_client?.IsConnected() == true)
        {
            try { _client.cancelHistoricalData(_requestId); } catch { /* ignore */ }
            _client.eDisconnect();
        }

        return ValueTask.CompletedTask;
    }

    private async Task ConnectAndSubscribeAsync(CancellationToken ct)
    {
        lock (_connectionGate)
        {
            if (_client?.IsConnected() == true)
            {
                _manualDisconnect = true; // suppress connectionClosed from old reader
                try { _client.eDisconnect(); } catch { /* ignore */ }
            }

            _manualDisconnect = false;
            _connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _readerSignal = new EReaderMonitorSignal();
            _client = new EClientSocket(this, _readerSignal);
            _client.SetConnectOptions("+PACEAPI");

            UpdateStatus($"Connecting to {_host}:{_port} (clientId={_clientId})...");
            _client.eConnect(_host, _port, _clientId);
            StartReaderLoop();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await _connectedTcs.Task.WaitAsync(timeoutCts.Token);
            UpdateStatus($"Connected to IB Gateway at {_host}:{_port}");
            RequestBars();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            UpdateStatus("Connection timed out (15s). Is IB Gateway running?");
            throw new TimeoutException($"Cannot connect to IB Gateway at {_host}:{_port}. Make sure IB Gateway is running and API is enabled.");
        }
    }

    private void RequestBars()
    {
        if (_client?.IsConnected() != true)
            return;

        _pendingBar = null;
        var reqId = Interlocked.Increment(ref _requestId);

        var contract = new Contract
        {
            Symbol = _symbol,
            SecType = "FUT",
            Exchange = _exchange,
            Currency = "USD",
            LastTradeDateOrContractMonth = ResolveFrontMonth(DateTimeOffset.UtcNow)
        };

        // Request 2 days of 5-min bars with keepUpToDate=true for live streaming
        _client.reqHistoricalData(
            reqId,
            contract,
            string.Empty,    // endDateTime: empty = now
            "2 D",           // durationStr: 2 days for backfill + gap recovery
            "5 mins",        // barSizeSetting
            "TRADES",        // whatToShow
            1,               // useRTH: 1 = regular trading hours only
            1,               // formatDate: 1 = yyyyMMdd HH:mm:ss format
            true,            // keepUpToDate: enables live streaming
            []);

        UpdateStatus($"Subscribed to {_symbol} 5-min bars (contract: {contract.LastTradeDateOrContractMonth})");
    }

    // --- EWrapper callbacks ---

    public override void nextValidId(int orderId)
    {
        _connectedTcs?.TrySetResult(true);
    }

    public override void historicalData(int reqId, IBApi.Bar bar)
    {
        // Historical backfill bars
        if (TryConvertBar(bar, out var marketBar))
            BarClosed?.Invoke(marketBar);
    }

    public override void historicalDataUpdate(int reqId, IBApi.Bar bar)
    {
        // Live streaming: each update is the current (in-progress) bar.
        // When a new timestamp appears, the previous bar is confirmed closed.
        if (!TryConvertBar(bar, out var marketBar))
            return;

        if (_pendingBar is null)
        {
            _pendingBar = marketBar;
            return;
        }

        if (marketBar.Timestamp == _pendingBar.Timestamp)
        {
            // Same bar, update in place
            _pendingBar = marketBar;
            return;
        }

        if (marketBar.Timestamp > _pendingBar.Timestamp)
        {
            // New bar started, previous bar is now closed
            BarClosed?.Invoke(_pendingBar);
            _pendingBar = marketBar;
        }
    }

    public override void historicalDataEnd(int reqId, string start, string end)
    {
        UpdateStatus($"Historical backfill complete ({start} to {end}). Streaming live bars...");
    }

    public override void connectAck()
    {
        if (_client?.AsyncEConnect == true)
            _client.startApi();
    }

    public override void connectionClosed()
    {
        UpdateStatus("Connection closed");
        if (!_manualDisconnect && !_isReconnecting)
            ScheduleReconnect();
    }

    public override void error(Exception e)
    {
        UpdateStatus($"Error: {e.Message}");
        if (!_manualDisconnect && _client?.IsConnected() != true)
            ScheduleReconnect();
    }

    public override void error(string str)
    {
        UpdateStatus($"IB: {str}");
    }

    public override void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
    {
        // Log all errors for diagnostics
        UpdateStatus($"IB error {errorCode}: {errorMsg}");

        switch (errorCode)
        {
            case 2104: // Market data farm connection OK
            case 2106: // HMDS data farm connection OK
            case 2107: // HMDS data farm inactive but should be OK
            case 2108: // Market data farm inactive
            case 2158: // Sec-def data farm connection OK
                break;
            case 1100: // Connectivity lost
            case 1300: // Socket dropped
            case 504:  // Not connected
                if (!_manualDisconnect)
                    ScheduleReconnect();
                break;
            case 1101: // Connectivity restored (data lost) — resubscribe
                RequestBars();
                break;
            case 1102: // Connectivity restored (data maintained)
                break;
            default:
                break;
        }
    }

    // --- Reconnect logic ---

    private void ScheduleReconnect()
    {
        if (_disposed)
            return;

        CancellationToken token;
        lock (_connectionGate)
        {
            if (_reconnectCts is { IsCancellationRequested: false })
                return;

            _reconnectCts?.Dispose();
            _reconnectCts = new CancellationTokenSource();
            token = _reconnectCts.Token;
        }

        _ = Task.Run(async () =>
        {
            _isReconnecting = true;
            var attempt = 0;
            try
            {
                while (!token.IsCancellationRequested && !_disposed)
                {
                    attempt++;
                    var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 4))));
                    UpdateStatus($"Reconnecting in {delay.TotalSeconds:0}s (attempt {attempt})...");

                    try
                    {
                        await Task.Delay(delay, token);

                        if (_client?.IsConnected() == true)
                        {
                            try { _client.cancelHistoricalData(_requestId); } catch { /* ignore */ }
                            _client.eDisconnect();
                            await Task.Delay(500, token); // let old reader thread exit
                        }

                        _pendingBar = null;

                        if (token.IsCancellationRequested)
                            return;

                        await ConnectAndSubscribeAsync(token);
                        UpdateStatus("Reconnected successfully");
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"Reconnect attempt {attempt} failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                _isReconnecting = false;
            }
        }, token);
    }

    // --- Helpers ---

    private void StartReaderLoop()
    {
        if (_client is null || _readerSignal is null)
            return;

        _reader = new EReader(_client, _readerSignal);
        _reader.Start();

        var client = _client; // capture for this thread's lifetime
        _readerThread = new Thread(() =>
        {
            while (!_disposed && client.IsConnected())
            {
                try
                {
                    _readerSignal.waitForSignal();
                    _reader.processMsgs();
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Reader thread error: {ex.Message}");
                    if (!_manualDisconnect && !_isReconnecting)
                        ScheduleReconnect();
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "AlertService-IbReader"
        };

        _readerThread.Start();
    }

    private bool TryConvertBar(IBApi.Bar bar, out Bar result)
    {
        result = default!;
        if (!TryParseIbBarTime(bar.Time, out var localTime))
            return false;

        // IBKR returns bar START time; add 5 min for close time
        var closeTime = localTime.AddMinutes(5);

        result = new Bar(
            closeTime,
            (decimal)bar.Open,
            (decimal)bar.High,
            (decimal)bar.Low,
            (decimal)bar.Close);
        return true;
    }

    private bool TryParseIbBarTime(string raw, out DateTime localTime)
    {
        if (DateTime.TryParseExact(raw, "yyyyMMdd  HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out localTime))
            return true;

        if (DateTime.TryParseExact(raw, "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out localTime))
            return true;

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            localTime = TimeZoneInfo.ConvertTime(
                DateTimeOffset.FromUnixTimeSeconds(unixSeconds), _easternTz).DateTime;
            return true;
        }

        localTime = default;
        return false;
    }

    private static string ResolveFrontMonth(DateTimeOffset nowUtc)
    {
        var month = nowUtc.Month;
        var quarterMonth = month <= 3 ? 3 : month <= 6 ? 6 : month <= 9 ? 9 : 12;
        var year = nowUtc.Year;
        if (month == quarterMonth && nowUtc.Day > 20)
        {
            quarterMonth += 3;
            if (quarterMonth > 12)
            {
                quarterMonth = 3;
                year += 1;
            }
        }

        return $"{year}{quarterMonth:00}";
    }

    private void UpdateStatus(string status)
    {
        _connectionStatus = status;
        Console.WriteLine($"[IB] {status}");
        StatusChanged?.Invoke(status);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
            catch { return TimeZoneInfo.Local; }
        }
    }
}
