using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using SwingDayTradingPlatform.AlertService;
using SwingDayTradingPlatform.AlertService.Feeds;
using SwingDayTradingPlatform.AlertService.Notifiers;
using SwingDayTradingPlatform.AlertService.Stores;
using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Strategy;
using SwingDayTradingPlatform.UI.Wpf.Infrastructure;
using IBarFeed = SwingDayTradingPlatform.AlertService.IBarFeed;

namespace SwingDayTradingPlatform.UI.Wpf.ViewModels;

public sealed class AlertsViewModel : ObservableObject, IAsyncDisposable
{
    private IBarFeed? _feed;
    private ExtremaDetector? _detector;
    private CsvAlertStore? _store;
    private TelegramNotifier? _telegramNotifier;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private volatile bool _hasError;

    // Config fields
    private string _mode = "replay";
    private string _symbol = "MES";
    private string _filePath = "";
    private string _exchange = "CME";
    private int _lookback = 5;
    private int _emaPeriod = 20;
    private string _ibHost = "127.0.0.1";
    private int _ibPort = 4001;
    private int _clientId = 12;
    private string _telegramToken = string.Empty;
    private string _telegramChatId = string.Empty;
    private string _outputPath = "./alerts.csv";

    // Bar/alert history for day chart
    private readonly Dictionary<DateOnly, List<Bar>> _barsByDate = [];
    private readonly Dictionary<DateOnly, List<Alert>> _alertsByDate = [];

    // Status fields
    private bool _isRunning;
    private string _status = "Stopped";
    private string _ibConnectionStatus = "";
    private int _barCount;
    private string _currentBarWindow = "--:-- ~ --:--";
    private string _latestBarInfo = "Waiting for bars...";

    public AlertsViewModel()
    {
        // Default file path: look for sample_data.csv next to the exe
        var samplePath = Path.Combine(AppContext.BaseDirectory, "sample_data.csv");
        _filePath = File.Exists(samplePath) ? samplePath : "./data.csv";

        StartCommand = new RelayCommand(_ => StartAsync(), _ => !IsRunning);
        StopCommand = new RelayCommand(_ => StopAsync(), _ => IsRunning);
        ClearCommand = new RelayCommand(_ => ClearAlerts(), _ => true);
    }

    // Commands
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ClearCommand { get; }

    // Alert rows
    public ObservableCollection<AlertRowViewModel> Alerts { get; } = [];

    // Connection log entries (newest first)
    public ObservableCollection<string> ConnectionLogs { get; } = [];

    // Dates that have bars (for day chart selection)
    public ObservableCollection<DateOnly> AvailableDates { get; } = [];

    // Config properties
    public string Mode { get => _mode; set { if (SetProperty(ref _mode, value)) { Raise(nameof(IsReplayMode)); Raise(nameof(IsRealtimeMode)); } } }
    public string Symbol { get => _symbol; set => SetProperty(ref _symbol, value); }
    public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }
    public string Exchange { get => _exchange; set => SetProperty(ref _exchange, value); }
    public int Lookback { get => _lookback; set => SetProperty(ref _lookback, value); }
    public int EmaPeriod { get => _emaPeriod; set => SetProperty(ref _emaPeriod, value); }
    public string IbHost { get => _ibHost; set => SetProperty(ref _ibHost, value); }
    public int IbPort { get => _ibPort; set => SetProperty(ref _ibPort, value); }
    public int ClientId { get => _clientId; set => SetProperty(ref _clientId, value); }
    public string TelegramToken { get => _telegramToken; set => SetProperty(ref _telegramToken, value); }
    public string TelegramChatId { get => _telegramChatId; set => SetProperty(ref _telegramChatId, value); }
    public string OutputPath { get => _outputPath; set => SetProperty(ref _outputPath, value); }

    public bool IsReplayMode => string.Equals(Mode, "replay", StringComparison.OrdinalIgnoreCase);
    public bool IsRealtimeMode => !IsReplayMode;

    // Status properties
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                StartCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string IbConnectionStatus { get => _ibConnectionStatus; private set => SetProperty(ref _ibConnectionStatus, value); }
    public int BarCount { get => _barCount; private set => SetProperty(ref _barCount, value); }
    public string CurrentBarWindow { get => _currentBarWindow; private set => SetProperty(ref _currentBarWindow, value); }
    public string LatestBarInfo { get => _latestBarInfo; private set => SetProperty(ref _latestBarInfo, value); }

    // Mode options for ComboBox
    public string[] ModeOptions { get; } = ["replay", "realtime"];

    private async Task StartAsync()
    {
        if (IsRunning) return;

        // Validate inputs
        if (IsReplayMode)
        {
            var resolvedPath = Path.IsPathRooted(FilePath) ? FilePath : Path.Combine(AppContext.BaseDirectory, FilePath);
            if (!File.Exists(resolvedPath) && !File.Exists(FilePath))
            {
                Status = $"File not found: {FilePath}";
                return;
            }
        }

        try
        {
            _cts = new CancellationTokenSource();
            _hasError = false;
            var ct = _cts.Token;

            // Create detector
            _detector = new ExtremaDetector(Lookback, EmaPeriod, Symbol);

            // Create store
            _store = new CsvAlertStore(OutputPath);

            // Create telegram notifier
            _telegramNotifier = new TelegramNotifier(TelegramToken, TelegramChatId);

            // Create feed
            if (string.Equals(Mode, "realtime", StringComparison.OrdinalIgnoreCase))
            {
                _feed = new IbBarFeed(IbHost, IbPort, ClientId, Symbol, Exchange);
                Status = $"Connecting to IB Gateway {IbHost}:{IbPort}...";
            }
            else
            {
                var path = Path.IsPathRooted(FilePath) ? FilePath : Path.Combine(AppContext.BaseDirectory, FilePath);
                if (!File.Exists(path)) path = FilePath; // fallback to original
                _feed = new CsvBarFeed(path);
                Status = $"Replaying {Path.GetFileName(path)}...";
            }

            // Wire events
            _feed.BarClosed += OnBarClosed;
            _detector.AlertDetected += OnAlertDetected;

            // Subscribe to IB connection status if realtime
            if (_feed is IbBarFeed ibFeed)
            {
                ibFeed.StatusChanged += OnIbStatusChanged;
            }

            IsRunning = true;
            BarCount = 0;
            Alerts.Clear();
            ConnectionLogs.Clear();
            AvailableDates.Clear();
            _barsByDate.Clear();
            _alertsByDate.Clear();
            IbConnectionStatus = "";

            // Run on background thread
            _runTask = Task.Run(async () =>
            {
                try
                {
                    await _feed.RunAsync(ct);
                }
                catch (OperationCanceledException) { /* normal stop */ }
                catch (Exception ex)
                {
                    _hasError = true;
                    DispatchUI(() => Status = $"Error: {ex.Message}");
                }
                finally
                {
                    DispatchUI(() =>
                    {
                        IsRunning = false;
                        // Don't overwrite error status
                        if (!_hasError)
                        {
                            var bars = _detector?.BarCount ?? 0;
                            Status = bars > 0
                                ? $"Complete. {bars} bars processed, {Alerts.Count} alerts detected."
                                : "No bars processed. Check your file path or connection.";
                        }
                    });
                }
            }, ct);
        }
        catch (Exception ex)
        {
            Status = $"Start failed: {ex.Message}";
            IsRunning = false;
        }
    }

    private async Task StopAsync()
    {
        if (!IsRunning) return;

        _cts?.Cancel();

        try
        {
            if (_runTask is not null)
                await _runTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { /* timeout or already done */ }

        await CleanupAsync();
        IsRunning = false;
        Status = $"Stopped. {BarCount} bars processed, {Alerts.Count} alerts.";
    }

    private Task ClearAlerts()
    {
        Alerts.Clear();
        AvailableDates.Clear();
        _barsByDate.Clear();
        _alertsByDate.Clear();
        BarCount = 0;
        CurrentBarWindow = "--:-- ~ --:--";
        LatestBarInfo = "Waiting for bars...";
        Status = "Cleared";
        return Task.CompletedTask;
    }

    private void OnIbStatusChanged(string status)
    {
        DispatchUI(() =>
        {
            IbConnectionStatus = status;
            Status = status;
            ConnectionLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {status}");
            while (ConnectionLogs.Count > 50)
                ConnectionLogs.RemoveAt(ConnectionLogs.Count - 1);
        });
    }

    private void OnBarClosed(Bar bar)
    {
        _detector?.OnBar(bar);

        DispatchUI(() =>
        {
            BarCount = _detector?.BarCount ?? 0;
            var start = bar.Timestamp.AddMinutes(-5);
            CurrentBarWindow = $"{start:HH:mm} ~ {bar.Timestamp:HH:mm}";
            LatestBarInfo = $"O={bar.Open}  H={bar.High}  L={bar.Low}  C={bar.Close}";
            Status = string.Equals(Mode, "realtime", StringComparison.OrdinalIgnoreCase)
                ? $"Live | Bars: {BarCount} | {CurrentBarWindow}"
                : $"Replaying | Bar #{BarCount} | {CurrentBarWindow}";

            // Accumulate bars by date for day chart
            var date = DateOnly.FromDateTime(bar.Timestamp);
            if (!_barsByDate.TryGetValue(date, out var list))
            {
                list = [];
                _barsByDate[date] = list;
                AvailableDates.Add(date);
            }
            list.Add(bar);
        });
    }

    private void OnAlertDetected(Alert alert)
    {
        // Write to CSV and send Telegram in background
        _ = Task.Run(async () =>
        {
            try { if (_store is not null) await _store.WriteAsync(alert); } catch { /* ignore */ }
            try { if (_telegramNotifier is not null) await _telegramNotifier.NotifyAsync(alert); } catch { /* ignore */ }
        });

        // Update UI
        DispatchUI(() =>
        {
            Alerts.Insert(0, new AlertRowViewModel(alert));

            // Accumulate alerts by date for day chart markers
            var date = DateOnly.FromDateTime(alert.PivotTime);
            if (!_alertsByDate.TryGetValue(date, out var alertList))
            {
                alertList = [];
                _alertsByDate[date] = alertList;
            }
            alertList.Add(alert);
        });
    }

    public AlertsDayChartViewModel? OpenDayChart(DateOnly date)
    {
        if (!_barsByDate.TryGetValue(date, out var bars) || bars.Count == 0)
            return null;

        // Eastern Time zone for Bar.Timestamp conversion
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        // Convert AlertService.Bar → MarketBar (Eastern DateTime → DateTimeOffset UTC)
        var marketBars = new List<MarketBar>(bars.Count);
        foreach (var b in bars)
        {
            var closeEastern = new DateTimeOffset(b.Timestamp, eastern.GetUtcOffset(b.Timestamp));
            var openEastern = closeEastern.AddMinutes(-5);
            marketBars.Add(new MarketBar(
                openEastern.ToUniversalTime(),
                closeEastern.ToUniversalTime(),
                b.Open, b.High, b.Low, b.Close, 0m));
        }

        // Compute EMA fresh for this day
        var closes = bars.Select(b => b.Close).ToList();
        var emaValues = Indicators.EmaSeries(closes, EmaPeriod);

        // Build trade markers from alerts
        var markers = new List<TradeMarker>();
        if (_alertsByDate.TryGetValue(date, out var alerts))
        {
            foreach (var a in alerts)
            {
                var pivotEastern = new DateTimeOffset(a.PivotTime, eastern.GetUtcOffset(a.PivotTime));
                var pivotUtc = pivotEastern.ToUniversalTime();

                if (a.Type == ExtremaType.MAX)
                {
                    // Peak: red down-arrow at High
                    markers.Add(new TradeMarker(pivotUtc, a.High, true, false,
                        $"PEAK @ {a.PivotTime:HH:mm}\nHigh={a.High:F2}  Close={a.Close:F2}\nEMA={a.EmaValue:F2} ({a.EmaDir})"));
                }
                else
                {
                    // Valley: green up-arrow at Low
                    markers.Add(new TradeMarker(pivotUtc, a.Low, true, true,
                        $"VALLEY @ {a.PivotTime:HH:mm}\nLow={a.Low:F2}  Close={a.Close:F2}\nEMA={a.EmaValue:F2} ({a.EmaDir})"));
                }
            }
        }

        var dt = date.ToDateTime(TimeOnly.MinValue);
        var vm = new AlertsDayChartViewModel
        {
            DayBars = marketBars,
            EmaValues = emaValues,
            TradeMarkers = markers,
            DateDisplay = $"{dt:ddd, MMM dd yyyy}",
            AlertCount = markers.Count
        };

        return vm;
    }

    private async Task CleanupAsync()
    {
        if (_feed is not null)
        {
            _feed.BarClosed -= OnBarClosed;
            if (_feed is IbBarFeed ibFeed)
                ibFeed.StatusChanged -= OnIbStatusChanged;
            await _feed.DisposeAsync();
            _feed = null;
        }

        if (_detector is not null)
        {
            _detector.AlertDetected -= OnAlertDetected;
            _detector = null;
        }

        _telegramNotifier?.Dispose();
        _telegramNotifier = null;
        _store = null;

        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        try
        {
            if (_runTask is not null)
                await _runTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch { /* ignore */ }

        await CleanupAsync();
    }

    private static void DispatchUI(Action action)
    {
        var app = Application.Current;
        if (app is null) return;

        if (app.Dispatcher.CheckAccess())
            action();
        else
            app.Dispatcher.Invoke(action);
    }
}

public sealed class AlertRowViewModel
{
    public AlertRowViewModel(Alert alert)
    {
        EventTime = alert.EventTime.ToString("yyyy-MM-dd HH:mm");
        PivotTime = alert.PivotTime.ToString("HH:mm");
        Type = alert.Type.ToString();
        Symbol = alert.Symbol;
        Close = alert.Close.ToString("F2");
        High = alert.High.ToString("F2");
        Low = alert.Low.ToString("F2");
        EmaValue = alert.EmaValue.ToString("F2");
        EmaDir = alert.EmaDir.ToString();
        IsMax = alert.Type == ExtremaType.MAX;
    }

    public string EventTime { get; }
    public string PivotTime { get; }
    public string Type { get; }
    public string Symbol { get; }
    public string Close { get; }
    public string High { get; }
    public string Low { get; }
    public string EmaValue { get; }
    public string EmaDir { get; }
    public bool IsMax { get; }
}
