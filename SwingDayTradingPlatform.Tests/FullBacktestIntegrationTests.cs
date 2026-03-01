using SwingDayTradingPlatform.Backtesting;
using SwingDayTradingPlatform.Shared;
using Xunit.Abstractions;

namespace SwingDayTradingPlatform.Tests;

public class FullBacktestIntegrationTests
{
    private readonly ITestOutputHelper _output;

    // Resolve the CSV path relative to the repo root
    private static readonly string CsvPath = ResolveCsvPath();

    public FullBacktestIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string ResolveCsvPath()
    {
        // Walk up from test bin directory to find the CSV
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir!, "SwingDayTradingPlatform.UI.Wpf",
                "bin", "Debug", "net8.0-windows", "data", "historical", "ES_5min_RTH.csv");
            if (File.Exists(candidate))
                return candidate;

            // Also check a flat data folder at repo root
            var flat = Path.Combine(dir!, "data", "historical", "ES_5min_RTH.csv");
            if (File.Exists(flat))
                return flat;

            dir = Path.GetDirectoryName(dir);
            if (dir is null) break;
        }

        // Absolute fallback
        return @"C:\Users\yanlin\GHCPProject\SwingDayTradingPlatform\SwingDayTradingPlatform.UI.Wpf\bin\Debug\net8.0-windows\data\historical\ES_5min_RTH.csv";
    }

    private static BacktestConfig MakeConfig() => new()
    {
        StartDate = new DateOnly(2023, 11, 30),
        EndDate = new DateOnly(2026, 2, 28),
        StartingCapital = 25_000m,
        CommissionPerTrade = 5m,
        SlippagePoints = 0.5m,
        PointValue = 50m,
        Timezone = "America/New_York",
        EntryWindowStart = "09:40",
        EntryWindowEnd = "15:50",
        FlattenTime = "15:55",
        MaxDailyLossPoints = 20m
    };

    private static BacktestParameters MakeParameters() => new();

    private void PrintResult(string label, BacktestResult result)
    {
        _output.WriteLine($"");
        _output.WriteLine($"=== {label} ===");
        _output.WriteLine($"  Bars loaded:     {result.EquityCurve.Count:N0}");
        _output.WriteLine($"  Total trades:    {result.TotalTrades}");
        _output.WriteLine($"  Winning:         {result.WinningTrades}");
        _output.WriteLine($"  Losing:          {result.LosingTrades}");
        _output.WriteLine($"  Flattened:       {result.FlattenedTrades}");
        _output.WriteLine($"  Win rate:        {result.WinRate:F2}%");
        _output.WriteLine($"  Net PnL:         ${result.NetPnL:N2}");
        _output.WriteLine($"  Gross PnL:       ${result.GrossPnL:N2}");
        _output.WriteLine($"  Commissions:     ${result.TotalCommissions:N2}");
        _output.WriteLine($"  Return:          {result.ReturnPct:F2}%");
        _output.WriteLine($"  Profit factor:   {result.ProfitFactor:F2}");
        _output.WriteLine($"  Avg win (pts):   {result.AvgWinPoints:F2}");
        _output.WriteLine($"  Avg loss (pts):  {result.AvgLossPoints:F2}");
        _output.WriteLine($"  Sharpe:          {result.SharpeRatio:F2}");
        _output.WriteLine($"  Sortino:         {result.SortinoRatio:F2}");
        _output.WriteLine($"  Max DD:          ${result.MaxDrawdown:N2} ({result.MaxDrawdownPct:F2}%)");
        _output.WriteLine($"  Starting cap:    ${result.StartingCapital:N2}");
        _output.WriteLine($"  Ending cap:      ${result.EndingCapital:N2}");

        if (result.Trades.Count > 0)
        {
            var byStrategy = result.Trades
                .GroupBy(t => t.StrategyName)
                .OrderByDescending(g => g.Count());
            _output.WriteLine($"  --- By strategy ---");
            foreach (var g in byStrategy)
            {
                var winCount = g.Count(t => t.PnLPoints > 0);
                var totalCount = g.Count();
                var pnl = g.Sum(t => t.PnLDollars - t.Commission);
                _output.WriteLine($"    {g.Key,-20} {totalCount,4} trades  {(totalCount > 0 ? (decimal)winCount / totalCount * 100 : 0):F1}% win  ${pnl:N2}");
            }

            var byExit = result.Trades
                .GroupBy(t => t.ExitReason)
                .OrderByDescending(g => g.Count());
            _output.WriteLine($"  --- By exit reason ---");
            foreach (var g in byExit)
            {
                _output.WriteLine($"    {g.Key,-20} {g.Count(),4} trades  ${g.Sum(t => t.PnLDollars - t.Commission):N2}");
            }

            // Monthly summary
            if (result.MonthlyReturns.Count > 0)
            {
                _output.WriteLine($"  --- Monthly returns ---");
                foreach (var m in result.MonthlyReturns)
                {
                    _output.WriteLine($"    {m.Year}-{m.Month:D2}:  ${m.PnL:N2}  ({m.ReturnPct:F2}%)");
                }
            }
        }
    }

    [Fact]
    public async Task FullBacktest_AllStrategiesCombined_NoErrors()
    {
        // Skip if CSV not available (CI environment)
        if (!File.Exists(CsvPath))
        {
            _output.WriteLine($"SKIPPED: CSV not found at {CsvPath}");
            return;
        }

        var bars = await CsvBarStorage.ReadAsync(CsvPath);
        Assert.True(bars.Count > 0, "CSV should contain bars");
        _output.WriteLine($"Loaded {bars.Count:N0} bars from {bars[0].OpenTimeUtc:yyyy-MM-dd} to {bars[^1].CloseTimeUtc:yyyy-MM-dd}");

        var config = MakeConfig();
        var parameters = MakeParameters();

        var engine = new BacktestEngine();
        var result = engine.Run(bars, parameters, config);

        PrintResult("ALL STRATEGIES COMBINED", result);

        // Basic sanity checks
        Assert.True(result.TotalTrades > 0, "Should produce at least some trades");
        // Winning + Losing covers all trades (flattened is a subset by exit reason)
        Assert.True(result.WinningTrades + result.LosingTrades <= result.TotalTrades);
        Assert.True(result.EquityCurve.Count > 0, "Equity curve should not be empty");
        Assert.Equal(25_000m, result.StartingCapital);
    }

    [Fact]
    public async Task FullBacktest_EachStrategyIndividually_NoErrors()
    {
        if (!File.Exists(CsvPath))
        {
            _output.WriteLine($"SKIPPED: CSV not found at {CsvPath}");
            return;
        }

        var bars = await CsvBarStorage.ReadAsync(CsvPath);
        Assert.True(bars.Count > 0);
        _output.WriteLine($"Loaded {bars.Count:N0} bars");

        var config = MakeConfig();
        var parameters = MakeParameters();

        var allResults = MultiStrategyBacktester.RunAllStrategies(bars, parameters, config);

        Assert.True(allResults.Count >= 4, $"Expected at least 4 strategies, got {allResults.Count}");
        Assert.True(allResults.ContainsKey("EmaPullback"));
        Assert.True(allResults.ContainsKey("EmaPullbackBarBreak"));
        Assert.True(allResults.ContainsKey("SecondLeg"));
        Assert.True(allResults.ContainsKey("BrooksPA"));

        foreach (var (name, result) in allResults)
        {
            PrintResult(name, result);
            Assert.True(result.TotalTrades >= 0, $"{name} TotalTrades should be non-negative");
            Assert.True(result.WinningTrades + result.LosingTrades <= result.TotalTrades, $"{name} win+loss should not exceed total");
            Assert.True(result.EquityCurve.Count > 0, $"{name} equity curve should not be empty");
        }
    }

    [Fact]
    public async Task FullBacktest_WithReversalBarExit_NoErrors()
    {
        if (!File.Exists(CsvPath))
        {
            _output.WriteLine($"SKIPPED: CSV not found at {CsvPath}");
            return;
        }

        var bars = await CsvBarStorage.ReadAsync(CsvPath);
        Assert.True(bars.Count > 0);

        var config = MakeConfig();
        var parameters = new BacktestParameters { UseReversalBarExit = true };

        var engine = new BacktestEngine();
        var result = engine.Run(bars, parameters, config);

        PrintResult("ALL STRATEGIES (UseReversalBarExit=true)", result);

        Assert.True(result.TotalTrades >= 0);
        Assert.True(result.WinningTrades + result.LosingTrades <= result.TotalTrades);
    }

    [Fact]
    public async Task FullBacktest_WithBarBreakExit_NoErrors()
    {
        if (!File.Exists(CsvPath))
        {
            _output.WriteLine($"SKIPPED: CSV not found at {CsvPath}");
            return;
        }

        var bars = await CsvBarStorage.ReadAsync(CsvPath);
        Assert.True(bars.Count > 0);

        var config = MakeConfig();
        var parameters = new BacktestParameters { UseBarBreakExit = true };

        var engine = new BacktestEngine();
        var result = engine.Run(bars, parameters, config);

        PrintResult("ALL STRATEGIES (UseBarBreakExit=true)", result);

        Assert.True(result.TotalTrades >= 0);
        Assert.True(result.WinningTrades + result.LosingTrades <= result.TotalTrades);
    }

    [Fact]
    public async Task FullBacktest_WalkForwardSplit_NoErrors()
    {
        if (!File.Exists(CsvPath))
        {
            _output.WriteLine($"SKIPPED: CSV not found at {CsvPath}");
            return;
        }

        var bars = await CsvBarStorage.ReadAsync(CsvPath);
        Assert.True(bars.Count > 0);

        var config = MakeConfig();
        var parameters = MakeParameters();

        var engine = new BacktestEngine();
        var fullResult = engine.Run(bars, parameters, config);

        // Split at mid-point of data range
        var cutoff = new DateOnly(2025, 6, 1);
        var (inSample, outOfSample) = MultiStrategyBacktester.SplitWalkForward(fullResult, cutoff, config);

        PrintResult("IN-SAMPLE (before 2025-06-01)", inSample);
        PrintResult("OUT-OF-SAMPLE (2025-06-01 onward)", outOfSample);

        Assert.True(inSample.TotalTrades >= 0);
        Assert.True(outOfSample.TotalTrades >= 0);
        Assert.Equal(fullResult.TotalTrades, inSample.TotalTrades + outOfSample.TotalTrades);
    }
}
