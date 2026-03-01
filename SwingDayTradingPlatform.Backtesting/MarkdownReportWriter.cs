using System.Text;

namespace SwingDayTradingPlatform.Backtesting;

public static class MarkdownReportWriter
{
    public static string Generate(
        WalkForwardEngine.WalkForwardResult? wfResult = null,
        List<StrategyRankResult>? rankings = null,
        Dictionary<string, BacktestResult>? strategyResults = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Quantitative Trading Research Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        if (wfResult is not null)
            AppendWalkForwardSection(sb, wfResult);

        if (rankings is not null && rankings.Count > 0)
            AppendRankingsSection(sb, rankings);

        if (strategyResults is not null)
            AppendStrategyDetails(sb, strategyResults);

        return sb.ToString();
    }

    public static void WriteToFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
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
}
