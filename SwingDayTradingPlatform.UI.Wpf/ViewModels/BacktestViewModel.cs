using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using SwingDayTradingPlatform.Backtesting;
using SwingDayTradingPlatform.Execution.Ibkr;
using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.UI.Wpf.Infrastructure;

namespace SwingDayTradingPlatform.UI.Wpf.ViewModels;

public sealed record StrategyOverviewRow(
    string Name, decimal NetPnL, decimal WinRate, decimal ProfitFactor,
    int TotalTrades, int Wins, int Losses, decimal Sharpe, decimal Sortino,
    decimal MaxDrawdown, decimal AvgWinPts, decimal AvgLossPts, decimal AvgR);

public sealed class BacktestViewModel : ObservableObject
{
    private readonly AppConfig _appConfig;
    private CancellationTokenSource? _cts;
    private List<MarketBar> _loadedBars = [];

    // Download fields
    private DateTime _downloadStartDate = new(2016, 1, 1);
    private DateTime _downloadEndDate = DateTime.Today;
    private double _downloadProgress;
    private string _downloadStatus = "Ready";
    private bool _isDownloading;

    // DB status
    private string _dbStatus = "No database loaded";
    private int _dbBarCount;

    // Backtest date range (separate from download range)
    private DateTime _backtestStartDate = DateTime.Today.AddMonths(-3);
    private DateTime _backtestEndDate = DateTime.Today;

    // Backtest parameter fields
    private int _fastEma = 20;
    private int _slowEma = 50;
    private int _atrPeriod = 14;
    private decimal _atrMultiplier = 1.5m;
    private decimal _rewardRiskRatio = 1.5m;
    private decimal _pullbackTolerance = 0.0015m;
    private int _maxTradesPerDay = 5;
    private int _maxLossesPerDay = 3;
    private decimal _maxStopPoints = 10m;
    private decimal _startingCapital = 25_000m;
    private decimal _commissionPerTrade = 0m;
    private decimal _slippagePoints = 0m;
    private decimal _maxDailyLossPoints = 20m;

    // MultiStrategy config fields (4 strategies only)
    private bool _enableStrategy1 = true;
    private bool _enableStrategy5 = true;
    private bool _enableStrategy7 = true;
    private bool _enableStrategy9 = true;
    private bool _enableHourlyBias = true;
    private int _hourlyRangeLookback = 10;
    private int _rangeTopPct = 75;
    private int _rangeBottomPct = 25;
    private int _swingLookback = 3;
    private decimal _srClusterAtrFactor = 0.5m;
    private decimal _bigMoveAtrFactor = 3.0m;
    private decimal _tickSize = 0.25m;

    // Walk-forward
    private bool _enableWalkForward;
    private DateTime _oosCutoffDate = DateTime.Today.AddMonths(-12);

    // Run state
    private bool _isRunning;
    private double _runProgress;
    private string _runStatus = "Ready";

    // Results
    private int _selectedTabIndex;
    private IReadOnlyList<EquityPoint>? _equityCurve;
    private string _netPnL = "--";
    private string _winRate = "--";
    private string _sharpe = "--";
    private string _maxDrawdown = "--";
    private string _profitFactor = "--";
    private string _totalTrades = "--";

    // Per-strategy results
    private string _isNetPnL = "--";
    private string _oosNetPnL = "--";

    // Verification
    private string _verificationStatus = "";

    // Strategy tab results
    private Dictionary<string, BacktestResult> _strategyResults = new();
    private Dictionary<string, List<DailySummary>> _strategySummaries = new();

    // Calendar (4 strategies: S1, S5, S7, S9)
    private IReadOnlyList<DailySummary>? _calendarSummaries1;
    private IReadOnlyList<DailySummary>? _calendarSummaries5;
    private IReadOnlyList<DailySummary>? _calendarSummaries7;
    private IReadOnlyList<DailySummary>? _calendarSummaries9;

    // Strategy stat strings
    private string _stats1 = "--";
    private string _stats5 = "--";
    private string _stats7 = "--";
    private string _stats9 = "--";

    // Per-strategy detail stats
    private string _statsNetPnL1 = "--", _statsNetPnL5 = "--", _statsNetPnL7 = "--", _statsNetPnL9 = "--";
    private string _statsWinRate1 = "--", _statsWinRate5 = "--", _statsWinRate7 = "--", _statsWinRate9 = "--";
    private string _statsSharpe1 = "--", _statsSharpe5 = "--", _statsSharpe7 = "--", _statsSharpe9 = "--";
    private string _statsMaxDD1 = "--", _statsMaxDD5 = "--", _statsMaxDD7 = "--", _statsMaxDD9 = "--";
    private string _statsPF1 = "--", _statsPF5 = "--", _statsPF7 = "--", _statsPF9 = "--";
    private string _statsTrades1 = "--", _statsTrades5 = "--", _statsTrades7 = "--", _statsTrades9 = "--";

    // Per-strategy equity curves
    private IReadOnlyList<EquityPoint>? _equityCurve1, _equityCurve5, _equityCurve7, _equityCurve9;

    public BacktestViewModel(AppConfig appConfig)
    {
        _appConfig = appConfig;
        Trades = [];
        SweepResults = [];
        MonthlyReturns = [];
        StrategyOverview = [];
        DayDetail = new DayDetailViewModel();

        DownloadCommand = new RelayCommand(_ => DownloadDataAsync(), _ => !IsDownloading && !IsRunning);
        ImportCsvCommand = new RelayCommand(_ => ImportCsvAsync(), _ => !IsRunning);
        VerifyCommand = new RelayCommand(_ => VerifyDataAsync(), _ => !IsRunning);
        RunBacktestCommand = new RelayCommand(_ => RunBacktestAsync(), _ => !IsRunning && !IsDownloading);
        RunAllStrategiesCommand = new RelayCommand(_ => RunAllStrategiesAsync(), _ => !IsRunning && !IsDownloading);
        CancelCommand = new RelayCommand(_ => CancelAsync(), _ => IsRunning || IsDownloading);

        // Initialize from appConfig multiStrategy defaults
        FastEma = appConfig.MultiStrategy.FastEmaPeriod;
        SlowEma = appConfig.MultiStrategy.SlowEmaPeriod;
        AtrPeriod = appConfig.MultiStrategy.AtrPeriod;
        EnableStrategy1 = appConfig.MultiStrategy.EnableStrategy1;
        EnableStrategy5 = appConfig.MultiStrategy.EnableStrategy5;
        EnableStrategy7 = appConfig.MultiStrategy.EnableStrategy7;
        EnableStrategy9 = appConfig.MultiStrategy.EnableStrategy9;
        EnableHourlyBias = appConfig.MultiStrategy.EnableHourlyBias;
        HourlyRangeLookback = appConfig.MultiStrategy.HourlyRangeLookback;
        RangeTopPct = appConfig.MultiStrategy.RangeTopPct;
        RangeBottomPct = appConfig.MultiStrategy.RangeBottomPct;
        SwingLookback = appConfig.MultiStrategy.SwingLookback;
        SRClusterAtrFactor = appConfig.MultiStrategy.SRClusterAtrFactor;
        BigMoveAtrFactor = appConfig.MultiStrategy.BigMoveAtrFactor;
        TickSize = appConfig.MultiStrategy.TickSize;

        // Try to load DB status
        RefreshDbStatus();
    }

    // Commands
    public RelayCommand DownloadCommand { get; }
    public RelayCommand ImportCsvCommand { get; }
    public RelayCommand VerifyCommand { get; }
    public RelayCommand RunBacktestCommand { get; }
    public RelayCommand RunAllStrategiesCommand { get; }
    public RelayCommand CancelCommand { get; }

    // Day detail
    public DayDetailViewModel DayDetail { get; }

    // Collections
    public ObservableCollection<BacktestTrade> Trades { get; }
    public ObservableCollection<BacktestResult> SweepResults { get; }
    public ObservableCollection<MonthlyReturn> MonthlyReturns { get; }
    public ObservableCollection<StrategyOverviewRow> StrategyOverview { get; }

    // Download properties
    public DateTime DownloadStartDate { get => _downloadStartDate; set => SetProperty(ref _downloadStartDate, value); }
    public DateTime DownloadEndDate { get => _downloadEndDate; set => SetProperty(ref _downloadEndDate, value); }
    public double DownloadProgress { get => _downloadProgress; set => SetProperty(ref _downloadProgress, value); }
    public string DownloadStatus { get => _downloadStatus; set => SetProperty(ref _downloadStatus, value); }
    public bool IsDownloading
    {
        get => _isDownloading;
        set { if (SetProperty(ref _isDownloading, value)) NotifyCommandStates(); }
    }

    // DB status
    public string DbStatus { get => _dbStatus; set => SetProperty(ref _dbStatus, value); }
    public int DbBarCount { get => _dbBarCount; set => SetProperty(ref _dbBarCount, value); }

    // Backtest date range
    public DateTime BacktestStartDate { get => _backtestStartDate; set => SetProperty(ref _backtestStartDate, value); }
    public DateTime BacktestEndDate { get => _backtestEndDate; set => SetProperty(ref _backtestEndDate, value); }

    // Parameter properties
    public int FastEma { get => _fastEma; set => SetProperty(ref _fastEma, value); }
    public int SlowEma { get => _slowEma; set => SetProperty(ref _slowEma, value); }
    public int AtrPeriod { get => _atrPeriod; set => SetProperty(ref _atrPeriod, value); }
    public decimal AtrMultiplier { get => _atrMultiplier; set => SetProperty(ref _atrMultiplier, value); }
    public decimal RewardRiskRatio { get => _rewardRiskRatio; set => SetProperty(ref _rewardRiskRatio, value); }
    public decimal PullbackTolerance { get => _pullbackTolerance; set => SetProperty(ref _pullbackTolerance, value); }
    public int MaxTradesPerDay { get => _maxTradesPerDay; set => SetProperty(ref _maxTradesPerDay, value); }
    public int MaxLossesPerDay { get => _maxLossesPerDay; set => SetProperty(ref _maxLossesPerDay, value); }
    public decimal MaxStopPoints { get => _maxStopPoints; set => SetProperty(ref _maxStopPoints, value); }
    public decimal StartingCapital { get => _startingCapital; set => SetProperty(ref _startingCapital, value); }
    public decimal CommissionPerTrade { get => _commissionPerTrade; set => SetProperty(ref _commissionPerTrade, value); }
    public decimal SlippagePoints { get => _slippagePoints; set => SetProperty(ref _slippagePoints, value); }
    public decimal MaxDailyLossPoints { get => _maxDailyLossPoints; set => SetProperty(ref _maxDailyLossPoints, value); }

    // Multi-strategy config
    public bool EnableStrategy1 { get => _enableStrategy1; set => SetProperty(ref _enableStrategy1, value); }
    public bool EnableStrategy5 { get => _enableStrategy5; set => SetProperty(ref _enableStrategy5, value); }
    public bool EnableStrategy7 { get => _enableStrategy7; set => SetProperty(ref _enableStrategy7, value); }
    public bool EnableStrategy9 { get => _enableStrategy9; set => SetProperty(ref _enableStrategy9, value); }
    public bool EnableHourlyBias { get => _enableHourlyBias; set => SetProperty(ref _enableHourlyBias, value); }
    public int HourlyRangeLookback { get => _hourlyRangeLookback; set => SetProperty(ref _hourlyRangeLookback, value); }
    public int RangeTopPct { get => _rangeTopPct; set => SetProperty(ref _rangeTopPct, value); }
    public int RangeBottomPct { get => _rangeBottomPct; set => SetProperty(ref _rangeBottomPct, value); }
    public int SwingLookback { get => _swingLookback; set => SetProperty(ref _swingLookback, value); }
    public decimal SRClusterAtrFactor { get => _srClusterAtrFactor; set => SetProperty(ref _srClusterAtrFactor, value); }
    public decimal BigMoveAtrFactor { get => _bigMoveAtrFactor; set => SetProperty(ref _bigMoveAtrFactor, value); }
    public decimal TickSize { get => _tickSize; set => SetProperty(ref _tickSize, value); }

    // Walk-forward
    public bool EnableWalkForward { get => _enableWalkForward; set => SetProperty(ref _enableWalkForward, value); }
    public DateTime OosCutoffDate { get => _oosCutoffDate; set => SetProperty(ref _oosCutoffDate, value); }

    // Run state
    public bool IsRunning
    {
        get => _isRunning;
        set { if (SetProperty(ref _isRunning, value)) NotifyCommandStates(); }
    }
    public double RunProgress { get => _runProgress; set => SetProperty(ref _runProgress, value); }
    public string RunStatus { get => _runStatus; set => SetProperty(ref _runStatus, value); }

    // Results
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }
    public IReadOnlyList<EquityPoint>? EquityCurve { get => _equityCurve; set => SetProperty(ref _equityCurve, value); }
    public string NetPnL { get => _netPnL; set => SetProperty(ref _netPnL, value); }
    public string WinRate { get => _winRate; set => SetProperty(ref _winRate, value); }
    public string Sharpe { get => _sharpe; set => SetProperty(ref _sharpe, value); }
    public string MaxDrawdown { get => _maxDrawdown; set => SetProperty(ref _maxDrawdown, value); }
    public string ProfitFactor { get => _profitFactor; set => SetProperty(ref _profitFactor, value); }
    public string TotalTrades { get => _totalTrades; set => SetProperty(ref _totalTrades, value); }
    public string IsNetPnL { get => _isNetPnL; set => SetProperty(ref _isNetPnL, value); }
    public string OosNetPnL { get => _oosNetPnL; set => SetProperty(ref _oosNetPnL, value); }

    // Verification
    public string VerificationStatus { get => _verificationStatus; set => SetProperty(ref _verificationStatus, value); }

    // Calendar summaries per strategy tab
    public IReadOnlyList<DailySummary>? CalendarSummaries1 { get => _calendarSummaries1; set => SetProperty(ref _calendarSummaries1, value); }
    public IReadOnlyList<DailySummary>? CalendarSummaries5 { get => _calendarSummaries5; set => SetProperty(ref _calendarSummaries5, value); }
    public IReadOnlyList<DailySummary>? CalendarSummaries7 { get => _calendarSummaries7; set => SetProperty(ref _calendarSummaries7, value); }
    public IReadOnlyList<DailySummary>? CalendarSummaries9 { get => _calendarSummaries9; set => SetProperty(ref _calendarSummaries9, value); }

    // Strategy stats
    public string Stats1 { get => _stats1; set => SetProperty(ref _stats1, value); }
    public string Stats5 { get => _stats5; set => SetProperty(ref _stats5, value); }
    public string Stats7 { get => _stats7; set => SetProperty(ref _stats7, value); }
    public string Stats9 { get => _stats9; set => SetProperty(ref _stats9, value); }

    // Per-strategy detail stat properties
    public string StatsNetPnL1 { get => _statsNetPnL1; set => SetProperty(ref _statsNetPnL1, value); }
    public string StatsNetPnL5 { get => _statsNetPnL5; set => SetProperty(ref _statsNetPnL5, value); }
    public string StatsNetPnL7 { get => _statsNetPnL7; set => SetProperty(ref _statsNetPnL7, value); }
    public string StatsNetPnL9 { get => _statsNetPnL9; set => SetProperty(ref _statsNetPnL9, value); }

    public string StatsWinRate1 { get => _statsWinRate1; set => SetProperty(ref _statsWinRate1, value); }
    public string StatsWinRate5 { get => _statsWinRate5; set => SetProperty(ref _statsWinRate5, value); }
    public string StatsWinRate7 { get => _statsWinRate7; set => SetProperty(ref _statsWinRate7, value); }
    public string StatsWinRate9 { get => _statsWinRate9; set => SetProperty(ref _statsWinRate9, value); }

    public string StatsSharpe1 { get => _statsSharpe1; set => SetProperty(ref _statsSharpe1, value); }
    public string StatsSharpe5 { get => _statsSharpe5; set => SetProperty(ref _statsSharpe5, value); }
    public string StatsSharpe7 { get => _statsSharpe7; set => SetProperty(ref _statsSharpe7, value); }
    public string StatsSharpe9 { get => _statsSharpe9; set => SetProperty(ref _statsSharpe9, value); }

    public string StatsMaxDD1 { get => _statsMaxDD1; set => SetProperty(ref _statsMaxDD1, value); }
    public string StatsMaxDD5 { get => _statsMaxDD5; set => SetProperty(ref _statsMaxDD5, value); }
    public string StatsMaxDD7 { get => _statsMaxDD7; set => SetProperty(ref _statsMaxDD7, value); }
    public string StatsMaxDD9 { get => _statsMaxDD9; set => SetProperty(ref _statsMaxDD9, value); }

    public string StatsPF1 { get => _statsPF1; set => SetProperty(ref _statsPF1, value); }
    public string StatsPF5 { get => _statsPF5; set => SetProperty(ref _statsPF5, value); }
    public string StatsPF7 { get => _statsPF7; set => SetProperty(ref _statsPF7, value); }
    public string StatsPF9 { get => _statsPF9; set => SetProperty(ref _statsPF9, value); }

    public string StatsTrades1 { get => _statsTrades1; set => SetProperty(ref _statsTrades1, value); }
    public string StatsTrades5 { get => _statsTrades5; set => SetProperty(ref _statsTrades5, value); }
    public string StatsTrades7 { get => _statsTrades7; set => SetProperty(ref _statsTrades7, value); }
    public string StatsTrades9 { get => _statsTrades9; set => SetProperty(ref _statsTrades9, value); }

    // Per-strategy equity curves
    public IReadOnlyList<EquityPoint>? EquityCurve1 { get => _equityCurve1; set => SetProperty(ref _equityCurve1, value); }
    public IReadOnlyList<EquityPoint>? EquityCurve5 { get => _equityCurve5; set => SetProperty(ref _equityCurve5, value); }
    public IReadOnlyList<EquityPoint>? EquityCurve7 { get => _equityCurve7; set => SetProperty(ref _equityCurve7, value); }
    public IReadOnlyList<EquityPoint>? EquityCurve9 { get => _equityCurve9; set => SetProperty(ref _equityCurve9, value); }

    public void OnCalendarDateSelected(string strategyName, DateOnly? date)
    {
        if (date is null || _loadedBars.Count == 0) return;

        List<BacktestTrade> trades = [];
        if (_strategyResults.TryGetValue(strategyName, out var result))
            trades = result.Trades;

        DayDetail.LoadDay(date.Value, _loadedBars, trades, _appConfig.Trading.Timezone);
    }

    private void RefreshDbStatus()
    {
        var dbPath = GetDbPath();
        if (File.Exists(dbPath))
        {
            var count = SqliteBarStore.GetBarCount(dbPath);
            var (min, max) = SqliteBarStore.GetDateRange(dbPath);
            DbBarCount = count;
            DbStatus = count > 0
                ? $"DB: {count:N0} bars ({min:yyyy-MM-dd} to {max:yyyy-MM-dd})"
                : "DB exists but empty";
        }
        else
        {
            DbBarCount = 0;
            DbStatus = "No database found";
        }
    }

    private string GetDbPath() =>
        Path.Combine(AppContext.BaseDirectory, _appConfig.Storage.BasePath, "es_bars.db");

    private async Task ImportCsvAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Import CSV Data"
        };
        if (dialog.ShowDialog() != true) return;

        IsRunning = true;
        try
        {
            RunStatus = "Importing CSV to SQLite...";
            RunProgress = 0;
            var dbPath = GetDbPath();
            await SqliteBarStore.ImportCsvAsync(dialog.FileName, dbPath);
            RefreshDbStatus();
            RunStatus = $"Import complete. {DbBarCount:N0} bars in database.";
            RunProgress = 100;
        }
        catch (Exception ex)
        {
            RunStatus = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task VerifyDataAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            Title = "Import TradingView CSV for Verification"
        };
        if (dialog.ShowDialog() != true) return;

        IsRunning = true;
        try
        {
            VerificationStatus = "Verifying...";
            var dbPath = GetDbPath();
            var report = await Task.Run(() =>
                DataVerificationEngine.VerifyAgainstTradingView(dialog.FileName, dbPath));

            var missingBars = report.TotalBarsCompared - report.MatchedBars;
            var missingInfo = missingBars > 0 ? $", {missingBars} missing" : "";
            VerificationStatus = report.Passed
                ? $"PASSED: {report.MatchedBars}/{report.TotalBarsCompared} bars matched{missingInfo}, max OHLC diff: {report.MaxOhlcDiff:F3}"
                : $"FAILED: {report.MismatchedBars} mismatches, {report.MatchedBars}/{report.TotalBarsCompared} matched{missingInfo}, max diff: {report.MaxOhlcDiff:F3}";
        }
        catch (Exception ex)
        {
            VerificationStatus = $"Verification error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task DownloadDataAsync()
    {
        IsDownloading = true;
        Interlocked.Exchange(ref _cts, new CancellationTokenSource())?.Dispose();
        try
        {
            var csvPath = Path.Combine(AppContext.BaseDirectory, _appConfig.Storage.BasePath, "historical", "ES_5min_RTH.csv");
            DownloadStatus = $"Connecting to IBKR at {_appConfig.Ibkr.Host}:{_appConfig.Ibkr.Port}...";
            DownloadProgress = 0;

            var startDate = DateOnly.FromDateTime(DownloadStartDate);
            var endDate = DateOnly.FromDateTime(DownloadEndDate);
            var token = _cts.Token;

            // Run entire download on background thread to avoid UI thread deadlocks
            var barCount = await Task.Run(async () =>
            {
                using var downloader = new IbkrHistoricalDownloader(_appConfig.Ibkr);
                downloader.ProgressChanged += (_, e) =>
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        DownloadStatus = e.Message;
                        if (e.Total > 0)
                            DownloadProgress = (double)e.Completed / e.Total * 100;
                    });
                };

                await downloader.ConnectAsync(token);

                Application.Current?.Dispatcher.BeginInvoke(() =>
                    DownloadStatus = "Connected. Starting download...");

                var bars = await downloader.DownloadAsync(startDate, endDate, csvPath, token);
                return bars.Count;
            }, token);

            DownloadStatus = $"Downloaded {barCount} bars. Importing to SQLite...";
            DownloadProgress = 90;

            // Auto-import to SQLite
            var dbPath = GetDbPath();
            await Task.Run(() => SqliteBarStore.ImportCsvAsync(csvPath, dbPath, token), token);
            RefreshDbStatus();

            DownloadStatus = $"Download & import complete. {DbBarCount:N0} bars in database.";
            DownloadProgress = 100;
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Download cancelled.";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private async Task RunBacktestAsync()
    {
        IsRunning = true;
        Interlocked.Exchange(ref _cts, new CancellationTokenSource())?.Dispose();
        try
        {
            await LoadBarsAsync();
            if (_loadedBars.Count == 0) return;

            RunStatus = $"Loaded {_loadedBars.Count:N0} bars. Running backtest...";
            RunProgress = 5;

            var parameters = BuildParameters();
            var config = BuildConfig();
            var token = _cts!.Token;

            var result = await Task.Run(() =>
            {
                var engine = new BacktestEngine();
                return engine.Run(_loadedBars, parameters, config, token,
                    pct => Application.Current?.Dispatcher.BeginInvoke(() =>
                        RunProgress = 5 + pct * 0.90));
            }, token);

            RunProgress = 100;
            RunStatus = $"Backtest complete. {result.TotalTrades} trades, Net P&L: ${result.NetPnL:N2}";
            ApplyResult(result);
        }
        catch (OperationCanceledException) { RunStatus = "Backtest cancelled."; }
        catch (Exception ex) { RunStatus = $"Backtest failed: {ex.Message}"; }
        finally { IsRunning = false; }
    }

    private async Task RunAllStrategiesAsync()
    {
        IsRunning = true;
        Interlocked.Exchange(ref _cts, new CancellationTokenSource())?.Dispose();
        try
        {
            await LoadBarsAsync();
            if (_loadedBars.Count == 0) return;

            RunStatus = $"Loaded {_loadedBars.Count:N0} bars. Running all strategies...";
            RunProgress = 5;

            var parameters = BuildParameters();
            var config = BuildConfig();
            var tradingTzId = _appConfig.Trading.Timezone;
            var startCap = config.StartingCapital;
            var enableWF = EnableWalkForward;
            var cutoff = enableWF ? DateOnly.FromDateTime(OosCutoffDate) : default;
            var token = _cts!.Token;

            // Run ALL heavy computation on background thread to keep UI responsive
            var computed = await Task.Run(() =>
            {
                var results = MultiStrategyBacktester.RunAllStrategies(_loadedBars, parameters, config, token,
                    pct => Application.Current?.Dispatcher.BeginInvoke(() =>
                        RunProgress = 5 + pct * 0.90),
                    status => Application.Current?.Dispatcher.BeginInvoke(() =>
                        RunStatus = status));

                var allTrades = results.Values.SelectMany(r => r.Trades).OrderBy(t => t.EntryTime).ToList();

                var combinedEquity = results.Values
                    .SelectMany(r => r.EquityCurve.Select(e => new EquityPoint(e.Timestamp, e.Equity - startCap, 0m)))
                    .GroupBy(e => e.Timestamp)
                    .Select(g => new EquityPoint(g.Key, startCap + g.Sum(e => e.Equity), 0m))
                    .OrderBy(e => e.Timestamp)
                    .ToList();

                var peakEquity = combinedEquity.Count > 0 ? combinedEquity[0].Equity : startCap;
                for (var i = 0; i < combinedEquity.Count; i++)
                {
                    if (combinedEquity[i].Equity > peakEquity) peakEquity = combinedEquity[i].Equity;
                    var ddPct = peakEquity > 0 ? (peakEquity - combinedEquity[i].Equity) / peakEquity * 100m : 0m;
                    combinedEquity[i] = combinedEquity[i] with { DrawdownPct = Math.Max(0m, ddPct) };
                }

                var combinedNetPnL = results.Values.Sum(r => r.NetPnL);
                var combinedTradeCount = results.Values.Sum(r => r.TotalTrades);
                var combinedWins = results.Values.Sum(r => r.WinningTrades);

                var combinedDailyReturns = ResultCalculator.CalculateDailyReturns(allTrades, config);
                var combinedSharpe = combinedDailyReturns.Count >= 2
                    ? ResultCalculator.Calculate(parameters, config, allTrades, combinedEquity).SharpeRatio
                    : 0m;

                var combinedMaxDD = 0m;
                var combinedPeak = combinedEquity.Count > 0 ? combinedEquity[0].Equity : startCap;
                foreach (var ep in combinedEquity)
                {
                    if (ep.Equity > combinedPeak) combinedPeak = ep.Equity;
                    var dd = combinedPeak - ep.Equity;
                    if (dd > combinedMaxDD) combinedMaxDD = dd;
                }

                var totalGrossWins = allTrades.Where(t => t.PnLDollars > 0).Sum(t => t.PnLDollars);
                var totalGrossLosses = Math.Abs(allTrades.Where(t => t.PnLDollars < 0).Sum(t => t.PnLDollars));
                var combinedPF = totalGrossLosses > 0 ? totalGrossWins / totalGrossLosses : totalGrossWins > 0 ? 999.99m : 0m;

                string isPnlStr = "--", oosPnlStr = "--";
                if (enableWF)
                {
                    var tradingTz = ResolveTimeZone(tradingTzId);
                    var isPnl = allTrades.Where(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.ExitTime, tradingTz).DateTime) < cutoff).Sum(t => t.PnLDollars - t.Commission);
                    var oosPnl = allTrades.Where(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.ExitTime, tradingTz).DateTime) >= cutoff).Sum(t => t.PnLDollars - t.Commission);
                    isPnlStr = $"IS: ${isPnl:N2}";
                    oosPnlStr = $"OOS: ${oosPnl:N2}";
                }

                // Per-strategy summaries
                var stratSummaries = new Dictionary<string, List<DailySummary>>();
                foreach (var kvp in results)
                    stratSummaries[kvp.Key] = MultiStrategyBacktester.ComputeDailySummaries(kvp.Value, tradingTzId);

                return (results, allTrades, combinedEquity, combinedNetPnL, combinedTradeCount, combinedWins,
                    combinedSharpe, combinedMaxDD, combinedPF, isPnlStr, oosPnlStr, stratSummaries);
            }, token);

            // Only lightweight UI property assignments on UI thread
            _strategyResults = computed.results;
            _strategySummaries = computed.stratSummaries;

            NetPnL = $"${computed.combinedNetPnL:N2}";
            WinRate = computed.combinedTradeCount > 0 ? $"{(decimal)computed.combinedWins / computed.combinedTradeCount * 100:F1}%" : "--";
            TotalTrades = computed.combinedTradeCount.ToString();
            EquityCurve = computed.combinedEquity;
            Sharpe = $"{computed.combinedSharpe:F2}";
            MaxDrawdown = $"${computed.combinedMaxDD:N2}";
            ProfitFactor = $"{computed.combinedPF:F2}";
            IsNetPnL = computed.isPnlStr;
            OosNetPnL = computed.oosPnlStr;

            Trades.Clear();
            foreach (var t in computed.allTrades) Trades.Add(t);

            // Per-strategy
            if (_strategyResults.TryGetValue("EmaPullback", out var r1))
            { CalendarSummaries1 = computed.stratSummaries.GetValueOrDefault("EmaPullback"); Stats1 = FormatStats(r1); ApplyStrategyDetail(r1, 1); }
            if (_strategyResults.TryGetValue("EmaPullbackBarBreak", out var r5))
            { CalendarSummaries5 = computed.stratSummaries.GetValueOrDefault("EmaPullbackBarBreak"); Stats5 = FormatStats(r5); ApplyStrategyDetail(r5, 5); }
            if (_strategyResults.TryGetValue("SecondLeg", out var r7))
            { CalendarSummaries7 = computed.stratSummaries.GetValueOrDefault("SecondLeg"); Stats7 = FormatStats(r7); ApplyStrategyDetail(r7, 7); }
            if (_strategyResults.TryGetValue("BrooksPA", out var r9))
            { CalendarSummaries9 = computed.stratSummaries.GetValueOrDefault("BrooksPA"); Stats9 = FormatStats(r9); ApplyStrategyDetail(r9, 9); }

            // Populate strategy overview
            StrategyOverview.Clear();
            foreach (var kvp in computed.results)
            {
                var r = kvp.Value;
                StrategyOverview.Add(new StrategyOverviewRow(
                    kvp.Key, r.NetPnL, r.WinRate, r.ProfitFactor,
                    r.TotalTrades, r.WinningTrades, r.LosingTrades,
                    r.SharpeRatio, r.SortinoRatio, r.MaxDrawdown,
                    r.AvgWinPoints, r.AvgLossPoints, r.AvgRPerTrade));
            }

            RunProgress = 100;
            RunStatus = $"All strategies complete. {computed.combinedTradeCount} total trades.";
        }
        catch (OperationCanceledException) { RunStatus = "Cancelled."; }
        catch (Exception ex) { RunStatus = $"Failed: {ex.Message}"; }
        finally { IsRunning = false; }
    }

    private static string FormatStats(BacktestResult r) =>
        $"Net: ${r.NetPnL:N2}  |  Win: {r.WinRate:F1}%  |  PF: {r.ProfitFactor:F2}  |  Trades: {r.TotalTrades}  |  Sharpe: {r.SharpeRatio:F2}  |  MaxDD: ${r.MaxDrawdown:N2}";

    private void ApplyStrategyDetail(BacktestResult r, int idx)
    {
        var pnl = $"${r.NetPnL:N2}";
        var wr = $"{r.WinRate:F1}%";
        var sh = $"{r.SharpeRatio:F2}";
        var dd = $"${r.MaxDrawdown:N2}";
        var pf = $"{r.ProfitFactor:F2}";
        var tr = r.TotalTrades.ToString();
        IReadOnlyList<EquityPoint> eq = r.EquityCurve;

        switch (idx)
        {
            case 1: StatsNetPnL1 = pnl; StatsWinRate1 = wr; StatsSharpe1 = sh; StatsMaxDD1 = dd; StatsPF1 = pf; StatsTrades1 = tr; EquityCurve1 = eq; break;
            case 5: StatsNetPnL5 = pnl; StatsWinRate5 = wr; StatsSharpe5 = sh; StatsMaxDD5 = dd; StatsPF5 = pf; StatsTrades5 = tr; EquityCurve5 = eq; break;
            case 7: StatsNetPnL7 = pnl; StatsWinRate7 = wr; StatsSharpe7 = sh; StatsMaxDD7 = dd; StatsPF7 = pf; StatsTrades7 = tr; EquityCurve7 = eq; break;
            case 9: StatsNetPnL9 = pnl; StatsWinRate9 = wr; StatsSharpe9 = sh; StatsMaxDD9 = dd; StatsPF9 = pf; StatsTrades9 = tr; EquityCurve9 = eq; break;
        }
    }

    private async Task LoadBarsAsync()
    {
        RunStatus = "Loading data from database...";
        RunProgress = 0;

        var dbPath = GetDbPath();
        var startDate = DateOnly.FromDateTime(BacktestStartDate);
        var endDate = DateOnly.FromDateTime(BacktestEndDate);
        var token = _cts?.Token ?? default;

        // Run all I/O on background thread — SQLite ReadAsync completes synchronously
        // for local files and would freeze the UI thread with 50k+ rows.
        _loadedBars = await Task.Run(async () =>
        {
            if (File.Exists(dbPath) && SqliteBarStore.GetBarCount(dbPath) > 0)
            {
                return await SqliteBarStore.LoadRangeAsync(dbPath, startDate, endDate, token);
            }

            Application.Current?.Dispatcher.BeginInvoke(() =>
                RunStatus = "Loading data from CSV...");

            // Fallback to CSV
            var csvPath = Path.Combine(AppContext.BaseDirectory, _appConfig.Storage.BasePath, "historical", "ES_5min_RTH.csv");
            if (!File.Exists(csvPath))
                return new List<MarketBar>();

            return await CsvBarStorage.ReadAsync(csvPath, token);
        }, token);

        if (_loadedBars.Count == 0)
            RunStatus = "No data found. Import CSV or download from IBKR.";
    }

    private Task CancelAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private void ApplyResult(BacktestResult result)
    {
        EquityCurve = result.EquityCurve;
        NetPnL = $"${result.NetPnL:N2}";
        WinRate = $"{result.WinRate:F1}%";
        Sharpe = $"{result.SharpeRatio:F2}";
        MaxDrawdown = $"${result.MaxDrawdown:N2} ({result.MaxDrawdownPct:F1}%)";
        ProfitFactor = $"{result.ProfitFactor:F2}";
        TotalTrades = result.TotalTrades.ToString();

        Trades.Clear();
        foreach (var t in result.Trades) Trades.Add(t);

        MonthlyReturns.Clear();
        foreach (var m in result.MonthlyReturns) MonthlyReturns.Add(m);
    }

    private BacktestParameters BuildParameters() => new()
    {
        FastEmaPeriod = FastEma,
        SlowEmaPeriod = SlowEma,
        AtrPeriod = AtrPeriod,
        AtrMultiplier = AtrMultiplier,
        RewardRiskRatio = RewardRiskRatio,
        PullbackTolerancePct = PullbackTolerance,
        MaxTradesPerDay = MaxTradesPerDay,
        MaxLossesPerDay = MaxLossesPerDay,
        MaxStopPoints = MaxStopPoints,
        EnableStrategy1 = EnableStrategy1,
        EnableStrategy5 = EnableStrategy5,
        EnableStrategy7 = EnableStrategy7,
        EnableStrategy9 = EnableStrategy9,
        EnableHourlyBias = EnableHourlyBias,
        HourlyRangeLookback = HourlyRangeLookback,
        RangeTopPct = RangeTopPct,
        RangeBottomPct = RangeBottomPct,
        SwingLookback = SwingLookback,
        SRClusterAtrFactor = SRClusterAtrFactor,
        BigMoveAtrFactor = BigMoveAtrFactor,
        TickSize = TickSize,
        TrailingStopAtrMultiplier = _appConfig.MultiStrategy.TrailingStopAtrMultiplier,
        TrailingStopActivationBars = _appConfig.MultiStrategy.TrailingStopActivationBars,
        UseBarBreakExit = _appConfig.MultiStrategy.UseBarBreakExit,
        UseReversalBarExit = _appConfig.MultiStrategy.UseReversalBarExit,
        RsiPeriod = _appConfig.MultiStrategy.RsiPeriod,
        EmaPullbackRewardRatio = _appConfig.MultiStrategy.EmaPullbackRewardRatio,
        EmaPullbackTolerance = _appConfig.MultiStrategy.EmaPullbackTolerance,
        EmaMinSlopeAtr = _appConfig.MultiStrategy.EmaMinSlopeAtr,
        EmaBodyMinAtrRatio = _appConfig.MultiStrategy.EmaBodyMinAtrRatio,
        EmaRsiLongMin = _appConfig.MultiStrategy.EmaRsiLongMin,
        EmaRsiLongMax = _appConfig.MultiStrategy.EmaRsiLongMax,
        EmaRsiShortMin = _appConfig.MultiStrategy.EmaRsiShortMin,
        EmaRsiShortMax = _appConfig.MultiStrategy.EmaRsiShortMax,
        EmaStopAtrBuffer = _appConfig.MultiStrategy.EmaStopAtrBuffer,
        BrooksPA_SignalBarBodyRatio = _appConfig.MultiStrategy.BrooksPA_SignalBarBodyRatio,
        BrooksPA_MinBarRangeAtr = _appConfig.MultiStrategy.BrooksPA_MinBarRangeAtr,
        BrooksPA_PullbackLookback = _appConfig.MultiStrategy.BrooksPA_PullbackLookback,
        BrooksPA_EmaToleranceAtr = _appConfig.MultiStrategy.BrooksPA_EmaToleranceAtr,
        BrooksPA_RewardRatio = _appConfig.MultiStrategy.BrooksPA_RewardRatio,
        BrooksPA_MaxStopTicks = _appConfig.MultiStrategy.BrooksPA_MaxStopTicks,
        EnableTimeFilter = _appConfig.MultiStrategy.EnableTimeFilter,
        LunchStartHour = _appConfig.MultiStrategy.LunchStartHour,
        LunchStartMinute = _appConfig.MultiStrategy.LunchStartMinute,
        LunchEndHour = _appConfig.MultiStrategy.LunchEndHour,
        LunchEndMinute = _appConfig.MultiStrategy.LunchEndMinute,
        LateCutoffHour = _appConfig.MultiStrategy.LateCutoffHour,
        LateCutoffMinute = _appConfig.MultiStrategy.LateCutoffMinute,
        MaxDailyTrades = _appConfig.MultiStrategy.MaxDailyTrades,
        EnableBreakEvenStop = _appConfig.MultiStrategy.EnableBreakEvenStop,
        BreakEvenActivationR = _appConfig.MultiStrategy.BreakEvenActivationR
    };

    private BacktestConfig BuildConfig() => new()
    {
        StartDate = DateOnly.FromDateTime(BacktestStartDate),
        EndDate = DateOnly.FromDateTime(BacktestEndDate),
        StartingCapital = StartingCapital,
        CommissionPerTrade = CommissionPerTrade,
        SlippagePoints = SlippagePoints,
        PointValue = _appConfig.Trading.PointValue,
        Timezone = _appConfig.Trading.Timezone,
        EntryWindowStart = _appConfig.Trading.EntryWindowStart,
        EntryWindowEnd = _appConfig.Trading.EntryWindowEnd,
        FlattenTime = _appConfig.Trading.FlattenTime,
        MaxDailyLossPoints = MaxDailyLossPoints,
        DbPath = GetDbPath(),
        InSampleCutoff = EnableWalkForward ? DateOnly.FromDateTime(OosCutoffDate) : null
    };

    private void NotifyCommandStates()
    {
        DownloadCommand.NotifyCanExecuteChanged();
        ImportCsvCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        RunBacktestCommand.NotifyCanExecuteChanged();
        RunAllStrategiesCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Local; }
    }
}
