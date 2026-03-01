using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Backtesting;

public sealed class AnalysisOptions
{
    public bool RunMonteCarlo { get; init; } = true;
    public int MonteCarloIterations { get; init; } = 1000;
    public bool RunSensitivity { get; init; } = false;
    public string[] SensitivityParams { get; init; } = [];
    public string ReportOutputPath { get; init; } = "reports/analysis.md";
}

public sealed class FullAnalysisReport
{
    public required Dictionary<string, BacktestResult> StrategyResults { get; init; }
    public required List<StrategyRankResult> Rankings { get; init; }
    public required Dictionary<string, MonteCarloResult> MonteCarloResults { get; init; }
    public required Dictionary<string, StreakAnalysis> StreakResults { get; init; }
    public required Dictionary<string, List<HourlyBreakdown>> HourlyResults { get; init; }
    public required Dictionary<string, List<DayOfWeekBreakdown>> DayOfWeekResults { get; init; }
    public required Dictionary<string, List<ExitReasonBreakdown>> ExitReasonResults { get; init; }
    public required List<StrategyComparison> StrategyComparisons { get; init; }
    public required List<EfficiencyMetric> EfficiencyMetrics { get; init; }
    public SensitivityResult? SensitivityResult { get; init; }
    public required string ReportMarkdown { get; init; }
}

public static class AnalyticalRunner
{
    public static async Task<FullAnalysisReport> RunAsync(
        List<MarketBar> bars,
        BacktestParameters parameters,
        BacktestConfig config,
        AnalysisOptions? options = null,
        CancellationToken ct = default,
        Action<string>? onStatus = null)
    {
        options ??= new AnalysisOptions();

        // Step 1: Run all strategies
        onStatus?.Invoke("Running strategy backtests...");
        var strategyResults = await Task.Run(
            () => MultiStrategyBacktester.RunAllStrategies(bars, parameters, config, ct, onStatus: onStatus), ct);

        // Step 2: Rank strategies
        onStatus?.Invoke("Ranking strategies...");
        var rankings = StrategyScorer.Rank(strategyResults);

        // Step 3: Monte Carlo per strategy
        var mcResults = new Dictionary<string, MonteCarloResult>();
        if (options.RunMonteCarlo)
        {
            foreach (var (name, result) in strategyResults)
            {
                ct.ThrowIfCancellationRequested();
                if (result.Trades.Count == 0) continue;
                onStatus?.Invoke($"Monte Carlo: {name}...");
                mcResults[name] = await Task.Run(
                    () => MonteCarloEngine.Run(result.Trades, config.StartingCapital,
                        config.PointValue, options.MonteCarloIterations), ct);
            }
        }

        // Step 4: Trade analytics per strategy
        onStatus?.Invoke("Computing trade analytics...");
        var streakResults = new Dictionary<string, StreakAnalysis>();
        var hourlyResults = new Dictionary<string, List<HourlyBreakdown>>();
        var dowResults = new Dictionary<string, List<DayOfWeekBreakdown>>();
        var exitReasonResults = new Dictionary<string, List<ExitReasonBreakdown>>();

        foreach (var (name, result) in strategyResults)
        {
            if (result.Trades.Count == 0) continue;
            streakResults[name] = TradeAnalytics.AnalyzeStreaks(result.Trades);
            hourlyResults[name] = TradeAnalytics.ByHourOfDay(result.Trades, config.Timezone);
            dowResults[name] = TradeAnalytics.ByDayOfWeek(result.Trades, config.Timezone);
            exitReasonResults[name] = TradeAnalytics.ByExitReason(result.Trades);
        }

        var allTrades = strategyResults.Values.SelectMany(r => r.Trades).ToList();
        var efficiencyMetrics = TradeAnalytics.MaeMfeAnalysis(allTrades);
        var strategyComparisons = TradeAnalytics.CompareStrategies(strategyResults);

        // Step 5: Sensitivity analysis (optional, expensive)
        SensitivityResult? sensitivityResult = null;
        if (options.RunSensitivity && options.SensitivityParams.Length > 0)
        {
            onStatus?.Invoke("Running sensitivity analysis...");
            sensitivityResult = await Task.Run(
                () => SensitivityAnalyzer.Analyze(bars, parameters, config,
                    options.SensitivityParams, strategyFilter: null, ct: ct), ct);
        }

        // Step 6: Generate report
        onStatus?.Invoke("Generating report...");
        var report = GenerateFullReport(
            strategyResults, rankings, mcResults, streakResults,
            hourlyResults, exitReasonResults, strategyComparisons,
            efficiencyMetrics, sensitivityResult);

        if (!string.IsNullOrEmpty(options.ReportOutputPath))
        {
            MarkdownReportWriter.WriteToFile(options.ReportOutputPath, report);
            onStatus?.Invoke($"Report written to {options.ReportOutputPath}");
        }

        return new FullAnalysisReport
        {
            StrategyResults = strategyResults,
            Rankings = rankings,
            MonteCarloResults = mcResults,
            StreakResults = streakResults,
            HourlyResults = hourlyResults,
            DayOfWeekResults = dowResults,
            ExitReasonResults = exitReasonResults,
            StrategyComparisons = strategyComparisons,
            EfficiencyMetrics = efficiencyMetrics,
            SensitivityResult = sensitivityResult,
            ReportMarkdown = report
        };
    }

    private static string GenerateFullReport(
        Dictionary<string, BacktestResult> strategyResults,
        List<StrategyRankResult> rankings,
        Dictionary<string, MonteCarloResult> mcResults,
        Dictionary<string, StreakAnalysis> streakResults,
        Dictionary<string, List<HourlyBreakdown>> hourlyResults,
        Dictionary<string, List<ExitReasonBreakdown>> exitReasonResults,
        List<StrategyComparison> strategyComparisons,
        List<EfficiencyMetric> efficiencyMetrics,
        SensitivityResult? sensitivityResult)
    {
        return MarkdownReportWriter.Generate(
            strategyResults: strategyResults,
            rankings: rankings,
            monteCarloResults: mcResults,
            streakResults: streakResults,
            hourlyResults: hourlyResults,
            exitReasonResults: exitReasonResults,
            strategyComparisons: strategyComparisons,
            efficiencyMetrics: efficiencyMetrics,
            sensitivityResult: sensitivityResult);
    }
}
