using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using SwingDayTradingPlatform.Execution.Ibkr;
using SwingDayTradingPlatform.Risk;
using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Storage;
using SwingDayTradingPlatform.Strategy;
using SwingDayTradingPlatform.UI.Wpf.Infrastructure;
using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.UI.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly string _configPath;
    private readonly AppConfig _config;
    private readonly TimeZoneInfo _tradingTimeZone;
    private readonly JsonOrderStateStore _store;
    private readonly IbkrBrokerClient _broker;
    private readonly IBarFeed _barFeed;
    private readonly MultiStrategyEngine _engine;
    private readonly RiskEngine _riskEngine;
    private readonly DispatcherTimer _clockTimer;
    private const int MaxChartBars = 500;
    private PersistedState _persistedState = new();
    private StrategyRunState _runState = StrategyRunState.Stopped;
    private string _connectionStatus = "Disconnected";
    private string _strategyStatus = "Stopped";
    private string _latestSignal = "None";
    private string _latestReason = "Waiting";
    private string _accountSummary = "No account data";
    private string _currentPacificTime = "--:--:--";
    private string _nextFlattenTime = "--";
    private bool _dayTradingComplete;
    private bool _isBusy;
    private DateOnly _currentTradingDate;
    private decimal _lastRealizedPnL;
    private bool _realizedPnLInitialized;
    private string _lastPrice = "--";
    private string _sessionHigh = "--";
    private string _sessionLow = "--";
    private int _chartBarCount;
    private decimal _sessionHighValue;
    private decimal _sessionLowValue = decimal.MaxValue;
    private List<decimal> _fastEmaValues = [];
    private List<decimal> _slowEmaValues = [];
    private List<MarketBar> _chartBarsList = [];
    private List<StrategySignal> _chartSignalsList = [];
    private readonly SemaphoreSlim _barProcessingLock = new(1, 1);

    private readonly ObservableCollection<PositionSnapshot> _positions;
    private readonly ObservableCollection<OrderTicket> _orders;
    private readonly ObservableCollection<LogEntryViewModel> _logs = [];
    private const int MaxLogEntries = 200;

    public MainViewModel(string configPath)
    {
        _configPath = configPath;
        _config = AppConfigLoader.Load(configPath);
        _tradingTimeZone = ResolveTimeZone(_config.Trading.Timezone);
        _store = new JsonOrderStateStore(new StorageConfig
        {
            BasePath = Path.Combine(AppContext.BaseDirectory, _config.Storage.BasePath),
            OrdersFile = _config.Storage.OrdersFile,
            ExecutionsFile = _config.Storage.ExecutionsFile,
            StrategyStateFile = _config.Storage.StrategyStateFile,
            LogsFile = _config.Storage.LogsFile
        });
        _broker = new IbkrBrokerClient(_config.Ibkr, _config.Trading.PointValue, _config.Trading.Symbol);
        _barFeed = new IbkrBarFeed(_config.Ibkr, _config.Trading);
        _engine = new MultiStrategyEngine(_config.MultiStrategy);
        _riskEngine = new RiskEngine(_config.Risk);

        _positions = [];
        _orders = [];

        StartCommand = new RelayCommand(_ => StartAsync(), _ => !IsBusy && RunState != StrategyRunState.Running);
        StopCommand = new RelayCommand(_ => StopAsync(), _ => !IsBusy && RunState != StrategyRunState.Stopped);
        KillSwitchCommand = new RelayCommand(_ => ActivateKillSwitchAsync("Manual kill switch"), _ => !IsBusy);
        RefreshCommand = new RelayCommand(_ => ReloadConfigAsync(), _ => !IsBusy);

        _broker.ConnectionChanged += OnConnectionChanged;
        _broker.OrderStatusChanged += OnOrderStatusChanged;
        _broker.ExecutionReceived += OnExecutionReceived;
        _broker.PositionChanged += OnPositionChanged;
        _broker.AccountSummaryReceived += OnAccountSummaryReceived;
        _barFeed.BarClosed += OnBarClosed;

        Backtest = new BacktestViewModel(_config);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        _ = InitializeAsync().ContinueWith(t =>
        {
            if (t.Exception is not null)
                AppendLog(LogCategory.System, $"Initialization failed: {t.Exception.InnerException?.Message ?? t.Exception.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public BacktestViewModel Backtest { get; }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand KillSwitchCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public ObservableCollection<PositionSnapshot> Positions => _positions;
    public ObservableCollection<OrderTicket> Orders => _orders;
    public ObservableCollection<LogEntryViewModel> Logs => _logs;

    public string ConnectionStatus { get => _connectionStatus; private set => SetProperty(ref _connectionStatus, value); }
    public string StrategyStatus { get => _strategyStatus; private set => SetProperty(ref _strategyStatus, value); }
    public string LatestSignal { get => _latestSignal; private set => SetProperty(ref _latestSignal, value); }
    public string LatestReason { get => _latestReason; private set => SetProperty(ref _latestReason, value); }
    public string AccountSummary { get => _accountSummary; private set => SetProperty(ref _accountSummary, value); }
    public string CurrentPacificTime { get => _currentPacificTime; private set => SetProperty(ref _currentPacificTime, value); }
    public string NextFlattenTime { get => _nextFlattenTime; private set => SetProperty(ref _nextFlattenTime, value); }

    public IReadOnlyList<MarketBar> ChartBars { get => _chartBarsList; private set { _chartBarsList = (List<MarketBar>)value; Raise(); } }
    public IReadOnlyList<decimal> FastEmaValues { get => _fastEmaValues; private set { _fastEmaValues = (List<decimal>)value; Raise(); } }
    public IReadOnlyList<decimal> SlowEmaValues { get => _slowEmaValues; private set { _slowEmaValues = (List<decimal>)value; Raise(); } }
    public IReadOnlyList<StrategySignal> ChartSignals { get => _chartSignalsList; private set { _chartSignalsList = (List<StrategySignal>)value; Raise(); } }
    public string LastPrice { get => _lastPrice; private set => SetProperty(ref _lastPrice, value); }
    public string SessionHigh { get => _sessionHigh; private set => SetProperty(ref _sessionHigh, value); }
    public string SessionLow { get => _sessionLow; private set => SetProperty(ref _sessionLow, value); }
    public int ChartBarCount { get => _chartBarCount; private set => SetProperty(ref _chartBarCount, value); }

    public StrategyRunState RunState
    {
        get => _runState;
        private set
        {
            if (SetProperty(ref _runState, value))
            {
                StrategyStatus = value.ToString();
                StartCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                StartCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
                KillSwitchCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _clockTimer.Stop();
        _broker.ConnectionChanged -= OnConnectionChanged;
        _broker.OrderStatusChanged -= OnOrderStatusChanged;
        _broker.ExecutionReceived -= OnExecutionReceived;
        _broker.PositionChanged -= OnPositionChanged;
        _broker.AccountSummaryReceived -= OnAccountSummaryReceived;
        _barFeed.BarClosed -= OnBarClosed;
        await _barFeed.DisposeAsync();
        await _broker.DisposeAsync();
        // Acquire the lock to ensure no in-flight OnBarClosed is holding it before disposing
        await _barProcessingLock.WaitAsync();
        _barProcessingLock.Dispose();
        _store.Dispose();
    }

    private async Task InitializeAsync()
    {
        _persistedState = await _store.LoadAsync(CancellationToken.None);
        _dayTradingComplete = _persistedState.LastFlattenDate == DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tradingTimeZone).DateTime).ToString("yyyy-MM-dd");
        _riskEngine.RestoreDayState(_persistedState.DailyTradeCount, _persistedState.DailyLossCount);

        if (_persistedState.KillSwitchArmed)
            _riskEngine.ArmKillSwitch("Kill switch restored from previous session");

        AppendLog(LogCategory.System, "Configuration loaded.");
        UpdateClock();
    }

    private async Task StartAsync()
    {
        IsBusy = true;
        try
        {
            RunState = StrategyRunState.Running;
            await _broker.ConnectAsync(CancellationToken.None);
            await _broker.SyncAsync(CancellationToken.None);
            await _barFeed.StartAsync(CancellationToken.None);
            AppendLog(LogCategory.System, "Strategy started.");
        }
        catch (Exception ex)
        {
            RunState = StrategyRunState.Stopped;
            AppendLog(LogCategory.System, $"Start failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopAsync()
    {
        IsBusy = true;
        try
        {
            await _barFeed.StopAsync(CancellationToken.None);
            RunState = StrategyRunState.Stopped;
            _realizedPnLInitialized = false;
            AppendLog(LogCategory.System, "Strategy stopped.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadConfigAsync()
    {
        AppConfigLoader.Save(_configPath, _config);
        AppendLog(LogCategory.System, "Config persisted to appsettings.json.");
        await Task.CompletedTask;
    }

    private async Task ActivateKillSwitchAsync(string reason)
    {
        _riskEngine.ArmKillSwitch(reason);
        RunState = StrategyRunState.Halted;
        AppendLog(LogCategory.Risk, reason);
        await _broker.CancelAllAsync(CancellationToken.None);
        await Task.Delay(500); // Allow IBKR time to process cancels before flattening
        await _broker.FlattenAsync(_config.Trading.Symbol, _config.Ibkr.ContractMonth, CancellationToken.None);
        await PersistStateAsync();
    }

    private async void OnBarClosed(object? sender, MarketBar bar)
    {
        if (!await _barProcessingLock.WaitAsync(0))
        {
            System.Diagnostics.Debug.WriteLine($"[BarDrop] Skipped bar {bar.CloseTimeUtc:HH:mm:ss} — still processing previous bar");
            return;
        }
        try
        {
        var app = App.Current;
        if (app is null) return;
        await await app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
            var localTime = TimeZoneInfo.ConvertTime(bar.CloseTimeUtc, _tradingTimeZone);
            CurrentPacificTime = $"{localTime:yyyy-MM-dd HH:mm:ss} {_tradingTimeZone.StandardName}";
            _broker.PushMark(bar.Close);

            var tradingDate = DateOnly.FromDateTime(localTime.DateTime);
            if (_currentTradingDate != tradingDate)
            {
                _currentTradingDate = tradingDate;
                _dayTradingComplete = false;
                _riskEngine.ResetForNewDay();
                _persistedState = _persistedState with
                {
                    DailyTradeCount = 0,
                    DailyLossCount = 0,
                    TriggeredSignalIds = []
                };
                // Reset session stats for new day
                _sessionHighValue = 0;
                _sessionLowValue = decimal.MaxValue;
                SessionHigh = "--";
                SessionLow = "--";
            }

            if (ShouldFlattenNow(localTime))
            {
                RunState = StrategyRunState.Flattening;
                AppendLog(LogCategory.Risk, "Flatten time reached. Cancelling orders and flattening.");
                await _broker.CancelAllAsync(CancellationToken.None);
                await Task.Delay(500); // Allow IBKR time to process cancels before flattening
                await _broker.FlattenAsync(_config.Trading.Symbol, _config.Ibkr.ContractMonth, CancellationToken.None);
                _dayTradingComplete = true;
                if (!_config.Trading.AllowReentryAfterFlatten)
                    _riskEngine.ArmKillSwitch("Trading day complete");
                await PersistStateAsync();
                // Still feed the bar to engine & chart even during flatten
                _engine.OnBarClosed(bar, null, localTime, false, _config.Trading);
                UpdateChart(bar, null);
                return;
            }

            if (RunState != StrategyRunState.Running || _riskEngine.KillSwitchArmed)
            {
                // Feed bar to engine & chart even when not actively trading
                _engine.OnBarClosed(bar, null, localTime, false, _config.Trading);
                UpdateChart(bar, null);
                return;
            }

            var currentPosition = _positions.FirstOrDefault(x => x.Symbol == _config.Trading.Symbol && x.Quantity != 0);
            var isLiveDecisionBar = bar.CloseTimeUtc >= DateTimeOffset.UtcNow.AddMinutes(-6);
            var tradingBlocked = _dayTradingComplete && !_config.Trading.AllowReentryAfterFlatten;
            var canOpen = isLiveDecisionBar &&
                          IsInsideEntryWindow(localTime) &&
                          _riskEngine.CanOpenNewPosition(bar.CloseTimeUtc, currentPosition is not null && currentPosition.Quantity != 0, tradingBlocked);

            var signal = _engine.OnBarClosed(bar, currentPosition, localTime, canOpen, _config.Trading);
            LatestSignal = _engine.LatestSignalText;
            LatestReason = _engine.LatestReason;

            UpdateChart(bar, signal);

            if (signal is null)
                return;

            // Handle flatten signals from bar-break exit
            if (signal.IsFlattenSignal)
            {
                await _broker.CancelAllAsync(CancellationToken.None);
                await Task.Delay(500); // Allow IBKR time to process cancels before flattening
                await _broker.FlattenAsync(_config.Trading.Symbol, _config.Ibkr.ContractMonth, CancellationToken.None);
                AppendLog(LogCategory.Strategy, $"Flatten: {signal.Reason}");
                await PersistStateAsync();
                return;
            }

            if (_persistedState.TriggeredSignalIds.Contains(signal.SignalId))
            {
                AppendLog(LogCategory.Strategy, $"Skipped duplicate signal {signal.SignalId}.");
                return;
            }

            var quantity = _riskEngine.CalculateContracts(signal.EntryPrice, signal.StopPrice, _broker.LatestAccountSnapshot?.NetLiquidation ?? 25_000m, _config.Trading.PointValue);
            if (quantity <= 0)
            {
                AppendLog(LogCategory.Risk, $"Skipped signal {signal.SignalId}: {_riskEngine.LastReason}");
                return;
            }

            _riskEngine.MarkEntryAttempt(signal.BarTimeUtc, signal.Reason);
            _persistedState.TriggeredSignalIds.Add(signal.SignalId);

            var side = signal.Direction == PositionSide.Long ? OrderSide.Buy : OrderSide.Sell;
            await _broker.PlaceBracketAsync(new PlaceBracketRequest
            {
                SignalId = signal.SignalId,
                Symbol = _config.Trading.Symbol,
                ContractMonth = _config.Ibkr.ContractMonth,
                Side = side,
                Quantity = quantity,
                EntryPrice = signal.EntryPrice,
                StopPrice = signal.StopPrice,
                TakeProfitPrice = signal.TargetPrice
            }, CancellationToken.None);

            AppendLog(LogCategory.Strategy, $"Placed {signal.Direction} bracket for {quantity} {_config.Trading.Symbol} contract. {signal.Reason}");
            await PersistStateAsync();
            }
            catch (Exception ex)
            {
                AppendLog(LogCategory.System, $"OnBarClosed error: {ex.Message}");
            }
        });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnBarClosed dispatch error: {ex}");
        }
        finally
        {
            _barProcessingLock.Release();
        }
    }

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e)
    {
        ConnectionStatus = $"{e.State}: {e.Message}";
        AppendLog(LogCategory.Connection, e.Message);
    }

    private async void OnOrderStatusChanged(object? sender, OrderStatusEventArgs e)
    {
        try
        {
            var app = App.Current;
            if (app is null) return;
            await await app.Dispatcher.InvokeAsync(async () =>
            {
                ReplaceItem(_orders, e.Order, x => x.LocalOrderId == e.Order.LocalOrderId);
                _persistedState = _persistedState with
                {
                    Orders = _orders.ToList()
                };
                AppendLog(LogCategory.Order, $"Order {e.Order.LocalOrderId} {e.Order.Intent} {e.Order.Status}");
                await PersistStateAsync();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnOrderStatusChanged error: {ex}");
        }
    }

    private async void OnExecutionReceived(object? sender, ExecutionEventArgs e)
    {
        try
        {
            var app = App.Current;
            if (app is null) return;
            await await app.Dispatcher.InvokeAsync(async () =>
            {
                _persistedState.Executions.Add(e.Fill);
                AppendLog(LogCategory.Execution, $"Execution {e.Fill.ExecutionId} {e.Fill.Side} {e.Fill.Quantity} @ {e.Fill.Price:0.00}");
                await PersistStateAsync();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnExecutionReceived error: {ex}");
        }
    }

    private void OnPositionChanged(object? sender, PositionEventArgs e)
    {
        var app = App.Current;
        if (app is null) return;
        app.Dispatcher.InvokeAsync(() =>
        {
            ReplaceItem(_positions, e.Position, x => x.Symbol == e.Position.Symbol);
        });
    }

    private void OnAccountSummaryReceived(object? sender, AccountSummaryEventArgs e)
    {
        var app = App.Current;
        if (app is null) return;
        app.Dispatcher.InvokeAsync(() =>
        {
            _riskEngine.EvaluateDailyLoss(e.Snapshot);

            if (!_realizedPnLInitialized)
            {
                // First snapshot: seed baseline without treating existing P&L as a new trade
                _lastRealizedPnL = e.Snapshot.RealizedPnL;
                _realizedPnLInitialized = true;
            }
            else
            {
                var realizedDelta = e.Snapshot.RealizedPnL - _lastRealizedPnL;
                if (realizedDelta != 0)
                {
                    _riskEngine.RegisterClosedTrade(realizedDelta);
                    _persistedState = _persistedState with { DailyLossCount = _riskEngine.LossCountToday };
                }
                _lastRealizedPnL = e.Snapshot.RealizedPnL;
            }
            AccountSummary = $"NetLiq ${e.Snapshot.NetLiquidation:N0} | Available ${e.Snapshot.AvailableFunds:N0} | Realized ${e.Snapshot.RealizedPnL:N0} | Unrealized ${e.Snapshot.UnrealizedPnL:N0} | Trades {_riskEngine.TradeCountToday}/{_config.Risk.MaxTradesPerDay} | Losses {_riskEngine.LossCountToday}/{_config.Risk.MaxLossesPerDay}";
            if (_riskEngine.KillSwitchArmed && RunState == StrategyRunState.Running)
            {
                RunState = StrategyRunState.Halted;
                _ = ActivateKillSwitchAsync(_riskEngine.LastReason).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        System.Diagnostics.Debug.WriteLine($"Kill switch error: {t.Exception?.InnerException?.Message}");
                }, TaskScheduler.Default);
            }
        });
    }

    private bool ShouldFlattenNow(DateTimeOffset localTime)
    {
        var flatten = ParseTime(_config.Trading.FlattenTime);
        NextFlattenTime = $"{localTime:yyyy-MM-dd} {flatten:hh\\:mm}";
        return localTime.TimeOfDay >= flatten && !_dayTradingComplete;
    }

    private bool IsInsideEntryWindow(DateTimeOffset localTime)
    {
        var start = ParseTime(_config.Trading.EntryWindowStart);
        var end = ParseTime(_config.Trading.EntryWindowEnd);
        return localTime.TimeOfDay >= start && localTime.TimeOfDay <= end;
    }

    private void UpdateClock()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tradingTimeZone);
        CurrentPacificTime = $"{now:yyyy-MM-dd HH:mm:ss} {_tradingTimeZone.StandardName}";
        NextFlattenTime = $"{now:yyyy-MM-dd} {ParseTime(_config.Trading.FlattenTime):hh\\:mm}";
    }

    private void UpdateChart(MarketBar bar, StrategySignal? signal)
    {
        // Update session stats
        LastPrice = bar.Close.ToString("F2");
        if (bar.High > _sessionHighValue)
        {
            _sessionHighValue = bar.High;
            SessionHigh = _sessionHighValue.ToString("F2");
        }
        if (bar.Low < _sessionLowValue)
        {
            _sessionLowValue = bar.Low;
            SessionLow = _sessionLowValue.ToString("F2");
        }

        // Add bar and cap at MaxChartBars
        _chartBarsList.Add(bar);
        if (_chartBarsList.Count > MaxChartBars)
            _chartBarsList.RemoveAt(0);
        ChartBarCount = _chartBarsList.Count;

        // Use engine's running EMA values (maintained incrementally, unaffected by bar trimming)
        var warmup = Math.Max(_config.MultiStrategy.SlowEmaPeriod, _config.MultiStrategy.AtrPeriod) + 5;
        if (_engine.Bars.Count >= warmup)
        {
            _fastEmaValues.Add(_engine.FastEma);
            _slowEmaValues.Add(_engine.SlowEma);

            while (_fastEmaValues.Count > MaxChartBars)
                _fastEmaValues.RemoveAt(0);
            while (_slowEmaValues.Count > MaxChartBars)
                _slowEmaValues.RemoveAt(0);
        }

        if (signal is not null)
            _chartSignalsList.Add(signal);

        // Trim signals outside the visible bar window
        if (_chartBarsList.Count > 0)
        {
            var oldestVisible = _chartBarsList[0].OpenTimeUtc;
            _chartSignalsList.RemoveAll(s => s.BarTimeUtc < oldestVisible);
        }

        // Trigger re-render with new list references
        ChartBars = new List<MarketBar>(_chartBarsList);
        FastEmaValues = new List<decimal>(_fastEmaValues);
        SlowEmaValues = new List<decimal>(_slowEmaValues);
        ChartSignals = new List<StrategySignal>(_chartSignalsList);
    }

    private async Task PersistStateAsync()
    {
        _persistedState = _persistedState with
        {
            TradingHalted = _riskEngine.TradingHalted,
            KillSwitchArmed = _riskEngine.KillSwitchArmed,
            LastFlattenDate = _dayTradingComplete ? _currentTradingDate.ToString("yyyy-MM-dd") : _persistedState.LastFlattenDate,
            Orders = _orders.ToList(),
            DailyTradeCount = _riskEngine.TradeCountToday,
            DailyLossCount = _riskEngine.LossCountToday
        };
        await _store.SaveAsync(_persistedState, CancellationToken.None);
    }

    private void AppendLog(LogCategory category, string message)
    {
        var entry = new LogEntry(DateTimeOffset.UtcNow, category, message);

        var vm = new LogEntryViewModel(entry);
        var app = App.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() =>
            {
                _logs.Insert(0, vm);
                while (_logs.Count > MaxLogEntries)
                    _logs.RemoveAt(_logs.Count - 1);
            });
        }
        else
        {
            _logs.Insert(0, vm);
            while (_logs.Count > MaxLogEntries)
                _logs.RemoveAt(_logs.Count - 1);
        }

        _ = _store.AppendLogAsync(entry, CancellationToken.None).ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"AppendLog error: {t.Exception?.InnerException?.Message}");
        }, TaskScheduler.Default);
    }

    private static void ReplaceItem<T>(ObservableCollection<T> collection, T item, Func<T, bool> matcher)
    {
        var existing = collection.FirstOrDefault(matcher);
        if (existing is not null)
            collection.Remove(existing);
        collection.Insert(0, item);
    }

    private static TimeSpan ParseTime(string value) => TimeSpan.TryParse(value, out var parsed) ? parsed : TimeSpan.Zero;

    private static string GetTimezoneAbbreviation(TimeZoneInfo tz)
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        return tz.IsDaylightSavingTime(now) ? tz.DaylightName : tz.StandardName;
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
