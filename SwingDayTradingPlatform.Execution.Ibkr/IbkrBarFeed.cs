using System.Globalization;
using IBApi;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Execution.Ibkr;

public sealed class IbkrBarFeed : DefaultEWrapper, IBarFeed
{
    private readonly IbkrConfig _config;
    private readonly TradingConfig _trading;
    private readonly TimeZoneInfo _tradingTimeZone;
    private readonly object _connectionGate = new();

    private EReaderMonitorSignal? _readerSignal;
    private EClientSocket? _client;
    private EReader? _reader;
    private Thread? _readerThread;
    private TaskCompletionSource<bool>? _connectedTcs;
    private CancellationTokenSource? _reconnectCts;
    private volatile bool _disposed;
    private volatile bool _manualDisconnect;
    private int _requestId = 4001;
    private MarketBar? _pendingLiveBar;

    public IbkrBarFeed(IbkrConfig config, TradingConfig trading)
    {
        _config = config;
        _trading = trading;
        _tradingTimeZone = ResolveTimeZone(trading.Timezone);
    }

    public event EventHandler<MarketBar>? BarClosed;

    public bool IsRunning { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
            return;

        if (_disposed)
            throw new ObjectDisposedException(nameof(IbkrBarFeed));

        lock (_connectionGate)
        {
            if (IsRunning)
                return;

            // Clean up old connection before creating new one
            if (_client?.IsConnected() == true)
            {
                try { _client.eDisconnect(); } catch { /* ignore */ }
            }

            IsRunning = true;
            _manualDisconnect = false;
            _connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _readerSignal = new EReaderMonitorSignal();
            _client = new EClientSocket(this, _readerSignal);
            _client.SetConnectOptions("+PACEAPI");
            _client.eConnect(_config.Host, _config.Port, _config.ClientId + 1000);
            StartReaderLoop();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
        await _connectedTcs!.Task.WaitAsync(timeoutCts.Token);
        RequestBars();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        IsRunning = false;
        _manualDisconnect = true;
        _reconnectCts?.Cancel();
        if (_client?.IsConnected() == true)
        {
            _client.cancelHistoricalData(_requestId);
            _client.eDisconnect();
        }

        _pendingLiveBar = null;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _manualDisconnect = true;
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        if (_client?.IsConnected() == true)
            _client.eDisconnect();
        return ValueTask.CompletedTask;
    }

    public override void nextValidId(int orderId)
    {
        _connectedTcs?.TrySetResult(true);
    }

    public override void historicalData(int reqId, Bar bar)
    {
        if (reqId != _requestId)
            return;

        if (TryConvertBar(bar, out var marketBar))
            BarClosed?.Invoke(this, marketBar);
    }

    public override void historicalDataUpdate(int reqId, Bar bar)
    {
        if (reqId != _requestId || !TryConvertBar(bar, out var marketBar))
            return;

        if (_pendingLiveBar is null)
        {
            _pendingLiveBar = marketBar;
            return;
        }

        if (marketBar.CloseTimeUtc == _pendingLiveBar.CloseTimeUtc)
        {
            _pendingLiveBar = marketBar;
            return;
        }

        if (marketBar.CloseTimeUtc > _pendingLiveBar.CloseTimeUtc)
        {
            BarClosed?.Invoke(this, _pendingLiveBar);
            _pendingLiveBar = marketBar;
        }
    }

    public override void historicalDataEnd(int reqId, string start, string end)
    {
        // Initial backfill is complete; live updates continue via historicalDataUpdate.
    }

    public override void connectAck()
    {
        if (_client?.AsyncEConnect == true)
            _client.startApi();
    }

    public override void connectionClosed()
    {
        if (!_manualDisconnect)
            ScheduleReconnect();
    }

    public override void error(Exception e)
    {
        if (!_manualDisconnect)
            ScheduleReconnect();
    }

    public override void error(string str)
    {
    }

    public override void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
    {
        switch (errorCode)
        {
            case 2104:
            case 2106:
            case 2158:
                return;
            case 1100:
            case 1101:
            case 1102:
            case 1300:
            case 504:
                if (!_manualDisconnect)
                    ScheduleReconnect();
                break;
        }
    }

    private void RequestBars()
    {
        if (_client?.IsConnected() != true)
            return;

        _pendingLiveBar = null;
        var reqId = Interlocked.Increment(ref _requestId);
        _client.reqHistoricalData(
            reqId,
            CreateFuturesContract(_config.ContractMonth),
            string.Empty,
            "2 D",
            "5 mins",
            "TRADES",
            1,
            1,
            true,
            []);
    }

    private void StartReaderLoop()
    {
        if (_client is null || _readerSignal is null)
            return;

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
                    if (!_manualDisconnect)
                        ScheduleReconnect();
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "IbkrBarFeedReader"
        };

        _readerThread.Start();
    }

    private void ScheduleReconnect()
    {
        if (_disposed || !IsRunning)
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
            var attempt = 0;
            while (!token.IsCancellationRequested && !_disposed && IsRunning)
            {
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 4))));

                try
                {
                    await Task.Delay(delay, token);

                    // Disconnect without cancelling our own reconnect CTS
                    if (_client?.IsConnected() == true)
                    {
                        _client.cancelHistoricalData(_requestId);
                        _client.eDisconnect();
                    }
                    _pendingLiveBar = null;

                    if (token.IsCancellationRequested)
                        return;

                    _manualDisconnect = false;
                    IsRunning = false; // Must be false so StartAsync proceeds
                    await StartAsync(token);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                }
            }
        }, token);
    }

    private bool TryConvertBar(Bar bar, out MarketBar marketBar)
    {
        marketBar = default!;
        if (!TryParseIbBarTime(bar.Time, out var localOpen))
            return false;

        DateTime openUtc;
        try
        {
            openUtc = TimeZoneInfo.ConvertTimeToUtc(localOpen, _tradingTimeZone);
        }
        catch (ArgumentException)
        {
            // Ambiguous or invalid time during DST transition — skip this bar
            return false;
        }

        // IBKR formatDate=1 returns bar START time; add 5 min for close
        var closeUtc = openUtc.AddMinutes(5);
        marketBar = new MarketBar(
            new DateTimeOffset(openUtc, TimeSpan.Zero),
            new DateTimeOffset(closeUtc, TimeSpan.Zero),
            (decimal)bar.Open,
            (decimal)bar.High,
            (decimal)bar.Low,
            (decimal)bar.Close,
            bar.Volume);
        return true;
    }

    private bool TryParseIbBarTime(string raw, out DateTime localClose)
    {
        if (DateTime.TryParseExact(raw, "yyyyMMdd  HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out localClose))
            return true;

        if (DateTime.TryParseExact(raw, "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out localClose))
            return true;

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            localClose = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), _tradingTimeZone).DateTime;
            return true;
        }

        localClose = default;
        return false;
    }

    private Contract CreateFuturesContract(string contractMonth)
    {
        return new Contract
        {
            Symbol = _trading.Symbol,
            SecType = "FUT",
            Exchange = _trading.Exchange,
            Currency = _trading.Currency,
            Multiplier = ((int)_trading.PointValue).ToString(),
            LastTradeDateOrContractMonth = string.IsNullOrWhiteSpace(contractMonth) ? ResolveFrontMonth(DateTimeOffset.UtcNow) : contractMonth
        };
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

    private static TimeZoneInfo ResolveTimeZone(string configured)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(configured);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }
}
