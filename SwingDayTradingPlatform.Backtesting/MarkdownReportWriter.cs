using System.Text;

namespace SwingDayTradingPlatform.Backtesting;

public static class MarkdownReportWriter
{
    public static string Generate(
        WalkForwardEngine.WalkForwardResult? wfResult = null,
        List<StrategyRankResult>? rankings = null,
        Dictionary<string, BacktestResult>? strategyResults = null,
        Dictionary<string, MonteCarloResult>? monteCarloResults = null,
        Dictionary<string, StreakAnalysis>? streakResults = null,
        Dictionary<string, List<HourlyBreakdown>>? hourlyResults = null,
        Dictionary<string, List<ExitReasonBreakdown>>? exitReasonResults = null,
        List<StrategyComparison>? strategyComparisons = null,
        List<EfficiencyMetric>? efficiencyMetrics = null,
        SensitivityResult? sensitivityResult = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Quantitative Trading Research Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        // Executive Summary
        if (strategyResults is not null && strategyResults.Count > 0)
            AppendExecutiveSummary(sb, strategyResults, rankings);

        // Strategy Comparison Table
        if (strategyComparisons is not null && strategyComparisons.Count > 0)
            AppendStrategyComparisonTable(sb, strategyComparisons);

        if (wfResult is not null)
            AppendWalkForwardSection(sb, wfResult);

        if (rankings is not null && rankings.Count > 0)
            AppendRankingsSection(sb, rankings);

        if (strategyResults is not null)
            AppendStrategyDetails(sb, strategyResults);

        // Advanced Metrics
        if (strategyResults is not null)
            AppendAdvancedMetrics(sb, strategyResults);

        // Trade Analytics - Hourly
        if (hourlyResults is not null && hourlyResults.Count > 0)
            AppendHourlyAnalysis(sb, hourlyResults);

        // Trade Analytics - Exit Reasons
        if (exitReasonResults is not null && exitReasonResults.Count > 0)
            AppendExitReasonAnalysis(sb, exitReasonResults);

        // Streak Analysis
        if (streakResults is not null && streakResults.Count > 0)
            AppendStreakAnalysis(sb, streakResults);

        // MAE/MFE Efficiency
        if (efficiencyMetrics is not null && efficiencyMetrics.Count > 0)
            AppendEfficiencyAnalysis(sb, efficiencyMetrics);

        // Monte Carlo
        if (monteCarloResults is not null && monteCarloResults.Count > 0)
            AppendMonteCarloSection(sb, monteCarloResults);

        // Sensitivity
        if (sensitivityResult is not null)
            AppendSensitivitySection(sb, sensitivityResult);

        return sb.ToString();
    }

    public static void WriteToFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }

    private static void AppendExecutiveSummary(StringBuilder sb, Dictionary<string, BacktestResult> results,
        List<StrategyRankResult>? rankings)
    {
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();

        var bestByExpR = results.OrderByDescending(kv => kv.Value.ExpectancyR).First();
        var bestByPnL = results.OrderByDescending(kv => kv.Value.NetPnL).First();
        var worstDD = results.OrderByDescending(kv => kv.Value.MaxDrawdownPct).First();

        sb.AppendLine($"- **Best strategy (ExpR):** {bestByExpR.Key} — ExpR: {bestByExpR.Value.ExpectancyR:F3}, PF: {bestByExpR.Value.ProfitFactor:F2}");
        sb.AppendLine($"- **Best strategy (PnL):** {bestByPnL.Key} — Net PnL: ${bestByPnL.Value.NetPnL:N2}");
        sb.AppendLine($"- **Highest drawdown:** {worstDD.Key} — MaxDD: {worstDD.Value.MaxDrawdownPct:F2}%");
        sb.AppendLine($"- **Total strategies tested:** {results.Count}");

        if (rankings is not null && rankings.Count > 0)
            sb.AppendLine($"- **Top ranked:** {rankings[0].StrategyName} (Score: {rankings[0].CompositeScore:F3})");

        // Risk warnings
        foreach (var (name, result) in results)
        {
            if (result.MaxDrawdownPct > 20)
                sb.AppendLine($"- **WARNING:** {name} has drawdown of {result.MaxDrawdownPct:F1}% — exceeds 20% threshold");
            if (result.ProfitFactor < 1.0m && result.TotalTrades > 10)
                sb.AppendLine($"- **WARNING:** {name} has PF < 1.0 ({result.ProfitFactor:F2}) — losing strategy");
        }

        sb.AppendLine();
    }

    private static void AppendStrategyComparisonTable(StringBuilder sb, List<StrategyComparison> comparisons)
    {
        sb.AppendLine("## Strategy Comparison");
        sb.AppendLine();
        sb.AppendLine("| Strategy | Trades | WinRate | PF | Net PnL | Sharpe | ExpR | MaxDD% | Calmar | AvgHold |");
        sb.AppendLine("|----------|--------|--------|----|---------|--------|------|--------|--------|--------|");

        foreach (var c in comparisons)
        {
            sb.AppendLine($"| {c.StrategyName} | {c.TotalTrades} | {c.WinRate:F1}% | {c.ProfitFactor:F2} | ${c.NetPnL:N0} | {c.SharpeRatio:F2} | {c.ExpectancyR:F3} | {c.MaxDrawdownPct:F1}% | {c.CalmarRatio:F2} | {c.AvgHoldTimeMinutes:F0}m |");
        }

        sb.AppendLine();
    }

    private static void AppendWalkForwardSection(StringBuilder sb, WalkForwardEngine.WalkForwardResult wfResult)
    {
        sb.AppendLine("## Walk-Forward Analysis");
        sb.AppendLine();
        sb.AppendLine($"Total folds: {wfResult.Folds.Count}");
        sb.AppendLine($"Aggregated OOS trades: {wfResult.AggregatedOosResult.TotalTrades}");
        sb.AppendLine($"Aggregated OOS ExpectancyR: {wfResult.AggregatedOosResult.ExpectancyR:F3}");
        sb.AppendLine();

        sb.AppendLine("| Fold | IS Period | OOS Period | IS Trades | OOS Trades | IS ExpR | OOS ExpR | OOS PF |");
        sb.AppendLine("|------|-----------|------------|-----------|------------|---------|----------|--------|");

        foreach (var fold in wfResult.Folds)
        {
            sb.AppendLine($"| {fold.FoldNumber} | {fold.TrainStart:yyyy-MM} to {fold.TrainEnd:yyyy-MM} | {fold.TestStart:yyyy-MM} to {fold.TestEnd:yyyy-MM} | {fold.InSampleResult.TotalTrades} | {fold.OutOfSampleResult.TotalTrades} | {fold.InSampleResult.ExpectancyR:F3} | {fold.OutOfSampleResult.ExpectancyR:F3} | {fold.OutOfSampleResult.ProfitFactor:F2} |");
        }

        sb.AppendLine();
    }

    private static void AppendRankingsSection(StringBuilder sb, List<StrategyRankResult> rankings)
    {
        sb.AppendLine("## Strategy Rankings");
        sb.AppendLine();

        var top = rankings.Take(3).ToList();
        sb.AppendLine("### Top 3 Strategies");
        sb.AppendLine();
        sb.AppendLine("| Rank | Strategy | Score | ExpR | PF | WinRate | DD(R) | %ProfMonths |");
        sb.AppendLine("|------|----------|-------|------|----|---------|-------|-------------|");

        for (var i = 0; i < top.Count; i++)
        {
            var r = top[i];
            sb.AppendLine($"| {i + 1} | {r.StrategyName} | {r.CompositeScore:F3} | {r.OosExpectancyR:F3} | {r.OosProfitFactor:F2} | {r.OosWinRate:F1}% | {r.OosMaxDrawdownR:F2} | {r.PctProfitableMonths:F1}% |");
        }

        sb.AppendLine();
    }

    private static void AppendStrategyDetails(StringBuilder sb, Dictionary<string, BacktestResult> results)
    {
        sb.AppendLine("## Strategy Details");
        sb.AppendLine();

        foreach (var (name, result) in results.OrderByDescending(kv => kv.Value.ExpectancyR))
        {
            sb.AppendLine($"### {name}");
            sb.AppendLine();
            sb.AppendLine($"- Total trades: {result.TotalTrades}");
            sb.AppendLine($"- Win rate: {result.WinRate:F1}%");
            sb.AppendLine($"- Profit factor: {result.ProfitFactor:F2}");
            sb.AppendLine($"- Net PnL: ${result.NetPnL:N2}");
            sb.AppendLine($"- Sharpe: {result.SharpeRatio:F2}");
            sb.AppendLine($"- Sortino: {result.SortinoRatio:F2}");
            sb.AppendLine($"- ExpectancyR: {result.ExpectancyR:F3}");
            sb.AppendLine($"- AvgR/Trade: {result.AvgRPerTrade:F3}");
            sb.AppendLine($"- MaxDD(R): {result.MaxDrawdownR:F2}");
            sb.AppendLine($"- MaxDD($): ${result.MaxDrawdown:N2} ({result.MaxDrawdownPct:F2}%)");
            sb.AppendLine();

            // R Distribution
            if (result.RDistribution.Count > 0)
            {
                sb.AppendLine("**R Distribution:**");
                sb.AppendLine();
                sb.AppendLine("| Bucket | Count | % |");
                sb.AppendLine("|--------|-------|---|");
                foreach (var bucket in result.RDistribution.Where(b => b.Count > 0))
                {
                    sb.AppendLine($"| [{bucket.BucketMin:F1}, {bucket.BucketMax:F1}) | {bucket.Count} | {bucket.Pct:F1}% |");
                }
                sb.AppendLine();
            }

            // Monthly returns
            if (result.MonthlyReturns.Count > 0)
            {
                sb.AppendLine("**Monthly Returns:**");
                sb.AppendLine();
                sb.AppendLine("| Year-Month | PnL | Return% |");
                sb.AppendLine("|------------|-----|---------|");
                foreach (var m in result.MonthlyReturns)
                {
                    sb.AppendLine($"| {m.Year}-{m.Month:D2} | ${m.PnL:N2} | {m.ReturnPct:F2}% |");
                }
                sb.AppendLine();
            }
        }
    }

    private static void AppendAdvancedMetrics(StringBuilder sb, Dictionary<string, BacktestResult> results)
    {
        sb.AppendLine("## Advanced Metrics");
        sb.AppendLine();
        sb.AppendLine("| Strategy | CAGR | Calmar | Recovery | Payoff | UlcerIdx | TailRatio | MFE Eff | MAE Ratio |");
        sb.AppendLine("|----------|------|--------|----------|--------|----------|-----------|---------|-----------|");

        foreach (var (name, r) in results.OrderByDescending(kv => kv.Value.ExpectancyR))
        {
            sb.AppendLine($"| {name} | {r.CAGR:F2}% | {r.CalmarRatio:F2} | {r.RecoveryFactor:F2} | {r.PayoffRatio:F2} | {r.UlcerIndex:F2} | {r.TailRatio:F2} | {r.MfeEfficiency:F2} | {r.MaeRatio:F2} |");
        }

        sb.AppendLine();

        // Consecutive wins/losses and hold times
        sb.AppendLine("| Strategy | MaxConsWins | MaxConsLosses | AvgHold | MaxHold |");
        sb.AppendLine("|----------|------------|---------------|---------|---------|");

        foreach (var (name, r) in results.OrderByDescending(kv => kv.Value.ExpectancyR))
        {
            var avgHold = r.AvgHoldTimeMinutes >= 60 ? $"{r.AvgHoldTimeMinutes / 60:F1}h" : $"{r.AvgHoldTimeMinutes:F0}m";
            var maxHold = r.MaxHoldTimeMinutes >= 60 ? $"{r.MaxHoldTimeMinutes / 60:F1}h" : $"{r.MaxHoldTimeMinutes:F0}m";
            sb.AppendLine($"| {name} | {r.MaxConsecutiveWins} | {r.MaxConsecutiveLosses} | {avgHold} | {maxHold} |");
        }

        sb.AppendLine();
    }

    private static void AppendHourlyAnalysis(StringBuilder sb, Dictionary<string, List<HourlyBreakdown>> hourlyResults)
    {
        sb.AppendLine("## Trade Analytics — Hour of Day");
        sb.AppendLine();

        foreach (var (name, hours) in hourlyResults)
        {
            sb.AppendLine($"### {name}");
            sb.AppendLine();
            sb.AppendLine("| Hour | Trades | Wins | Losses | PnL | Avg PnL |");
            sb.AppendLine("|------|--------|------|--------|-----|---------|");

            foreach (var h in hours)
            {
                sb.AppendLine($"| {h.Hour:D2}:00 | {h.TradeCount} | {h.Wins} | {h.Losses} | ${h.PnL:N0} | ${h.AvgPnL:N0} |");
            }

            sb.AppendLine();
        }
    }

    private static void AppendExitReasonAnalysis(StringBuilder sb, Dictionary<string, List<ExitReasonBreakdown>> exitResults)
    {
        sb.AppendLine("## Trade Analytics — Exit Reasons");
        sb.AppendLine();

        foreach (var (name, exits) in exitResults)
        {
            sb.AppendLine($"### {name}");
            sb.AppendLine();
            sb.AppendLine("| Exit Reason | Count | Total PnL | Avg PnL | Win Rate |");
            sb.AppendLine("|-------------|-------|-----------|---------|----------|");

            foreach (var e in exits)
            {
                sb.AppendLine($"| {e.ExitReason} | {e.Count} | ${e.TotalPnL:N0} | ${e.AvgPnL:N0} | {e.WinRate:F1}% |");
            }

            sb.AppendLine();
        }
    }

    private static void AppendStreakAnalysis(StringBuilder sb, Dictionary<string, StreakAnalysis> streakResults)
    {
        sb.AppendLine("## Streak Analysis");
        sb.AppendLine();
        sb.AppendLine("| Strategy | MaxWinStreak | MaxLossStreak | AvgWinStreak | AvgLossStreak | WinStreaks | LossStreaks |");
        sb.AppendLine("|----------|-------------|---------------|-------------|---------------|-----------|------------|");

        foreach (var (name, s) in streakResults)
        {
            sb.AppendLine($"| {name} | {s.MaxWinStreak} | {s.MaxLossStreak} | {s.AvgWinStreakLength:F1} | {s.AvgLossStreakLength:F1} | {s.TotalWinStreaks} | {s.TotalLossStreaks} |");
        }

        sb.AppendLine();
    }

    private static void AppendEfficiencyAnalysis(StringBuilder sb, List<EfficiencyMetric> metrics)
    {
        sb.AppendLine("## MAE/MFE Efficiency");
        sb.AppendLine();
        sb.AppendLine("| Strategy | Trades | MFE Capture | MAE/Stop Ratio |");
        sb.AppendLine("|----------|--------|-------------|----------------|");

        foreach (var m in metrics)
        {
            sb.AppendLine($"| {m.StrategyName} | {m.TradeCount} | {m.AvgMfeCapture:F2} | {m.AvgMaeToStop:F2} |");
        }

        sb.AppendLine();
        sb.AppendLine("*MFE Capture: avg(PnL/MFE) — closer to 1.0 means capturing more of the move*");
        sb.AppendLine("*MAE/Stop: avg(MAE/InitialStop) — lower means better stop placement*");
        sb.AppendLine();
    }

    private static void AppendMonteCarloSection(StringBuilder sb, Dictionary<string, MonteCarloResult> mcResults)
    {
        sb.AppendLine("## Monte Carlo Simulation");
        sb.AppendLine();

        foreach (var (name, mc) in mcResults)
        {
            sb.AppendLine($"### {name} ({mc.Iterations} iterations, {mc.OriginalTradeCount} trades)");
            sb.AppendLine();

            sb.AppendLine("| Metric | P5 | P25 | P50 | P75 | P95 |");
            sb.AppendLine("|--------|----|----|----|----|-----|");
            AppendMcBandRow(sb, mc.NetPnL, "$");
            AppendMcBandRow(sb, mc.MaxDrawdown, "$");
            AppendMcBandRow(sb, mc.MaxDrawdownPct, "%");
            AppendMcBandRow(sb, mc.SharpeRatio, "");
            sb.AppendLine();

            sb.AppendLine($"- **Probability of ruin** (DD > {mc.RuinThresholdPct:F0}%): **{mc.ProbabilityOfRuin:F1}%**");
            sb.AppendLine();
        }
    }

    private static void AppendMcBandRow(StringBuilder sb, PercentileBand band, string suffix)
    {
        if (suffix == "$")
            sb.AppendLine($"| {band.Label} | ${band.P5:N0} | ${band.P25:N0} | ${band.P50:N0} | ${band.P75:N0} | ${band.P95:N0} |");
        else if (suffix == "%")
            sb.AppendLine($"| {band.Label} | {band.P5:F1}% | {band.P25:F1}% | {band.P50:F1}% | {band.P75:F1}% | {band.P95:F1}% |");
        else
            sb.AppendLine($"| {band.Label} | {band.P5:F2} | {band.P25:F2} | {band.P50:F2} | {band.P75:F2} | {band.P95:F2} |");
    }

    private static void AppendSensitivitySection(StringBuilder sb, SensitivityResult sensitivity)
    {
        sb.AppendLine("## Sensitivity Analysis");
        sb.AppendLine();
        sb.AppendLine($"- **Most sensitive parameter:** {sensitivity.MostSensitiveParameter}");
        sb.AppendLine($"- **Least sensitive parameter:** {sensitivity.LeastSensitiveParameter}");
        sb.AppendLine();

        foreach (var param in sensitivity.Parameters)
        {
            sb.AppendLine($"### {param.ParameterName} (base: {param.BaseValue:G})");
            sb.AppendLine();
            sb.AppendLine("| Perturbation | Value | ExpR | Sharpe | PF | MaxDD% | Trades |");
            sb.AppendLine("|-------------|-------|------|--------|----|--------|--------|");

            foreach (var p in param.Perturbations)
            {
                sb.AppendLine($"| {p.PerturbPct:+0;-0}% | {p.NewValue:G} | {p.ExpectancyR:F3} | {p.SharpeRatio:F2} | {p.ProfitFactor:F2} | {p.MaxDrawdownPct:F1}% | {p.TotalTrades} |");
            }

            sb.AppendLine();
            sb.AppendLine($"Metric variance: {param.MetricVariance:F6}");
            sb.AppendLine();
        }
    }
}
