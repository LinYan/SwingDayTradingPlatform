namespace SwingDayTradingPlatform.Backtesting;

public sealed record HourlyBreakdown(int Hour, int TradeCount, int Wins, int Losses, decimal PnL, decimal AvgPnL);

public sealed record DayOfWeekBreakdown(DayOfWeek Day, int TradeCount, int Wins, int Losses, decimal PnL, decimal AvgPnL);

public sealed record HoldTimeBucket(string Label, int MinMinutes, int MaxMinutes, int Count, decimal AvgPnL);

public sealed record StreakAnalysis(
    int MaxWinStreak,
    int MaxLossStreak,
    decimal AvgWinStreakLength,
    decimal AvgLossStreakLength,
    int TotalWinStreaks,
    int TotalLossStreaks);

public sealed record EfficiencyMetric(
    string StrategyName,
    decimal AvgMfeCapture,
    decimal AvgMaeToStop,
    int TradeCount);

public sealed record ExitReasonBreakdown(string ExitReason, int Count, decimal TotalPnL, decimal AvgPnL, decimal WinRate);

public sealed record StrategyComparison(
    string StrategyName,
    int TotalTrades,
    decimal WinRate,
    decimal ProfitFactor,
    decimal NetPnL,
    decimal SharpeRatio,
    decimal ExpectancyR,
    decimal MaxDrawdownPct,
    decimal CalmarRatio,
    decimal AvgHoldTimeMinutes);

public static class TradeAnalytics
{
    public static List<HourlyBreakdown> ByHourOfDay(List<BacktestTrade> trades, string timezone = "America/New_York")
    {
        if (trades.Count == 0) return [];

        var tz = ResolveTimeZone(timezone);
        return trades
            .GroupBy(t => TimeZoneInfo.ConvertTime(t.EntryTime, tz).Hour)
            .Select(g =>
            {
                var wins = g.Count(t => t.PnLDollars - t.Commission > 0);
                var losses = g.Count(t => t.PnLDollars - t.Commission < 0);
                var pnl = g.Sum(t => t.PnLDollars - t.Commission);
                return new HourlyBreakdown(g.Key, g.Count(), wins, losses, pnl, pnl / g.Count());
            })
            .OrderBy(h => h.Hour)
            .ToList();
    }

    public static List<DayOfWeekBreakdown> ByDayOfWeek(List<BacktestTrade> trades, string timezone = "America/New_York")
    {
        if (trades.Count == 0) return [];

        var tz = ResolveTimeZone(timezone);
        return trades
            .GroupBy(t => TimeZoneInfo.ConvertTime(t.EntryTime, tz).DayOfWeek)
            .Select(g =>
            {
                var wins = g.Count(t => t.PnLDollars - t.Commission > 0);
                var losses = g.Count(t => t.PnLDollars - t.Commission < 0);
                var pnl = g.Sum(t => t.PnLDollars - t.Commission);
                return new DayOfWeekBreakdown(g.Key, g.Count(), wins, losses, pnl, pnl / g.Count());
            })
            .OrderBy(d => d.Day)
            .ToList();
    }

    public static List<HoldTimeBucket> HoldTimeDistribution(List<BacktestTrade> trades)
    {
        if (trades.Count == 0) return [];

        var buckets = new (string Label, int Min, int Max)[]
        {
            ("0-5 min", 0, 5),
            ("5-15 min", 5, 15),
            ("15-30 min", 15, 30),
            ("30-60 min", 30, 60),
            ("1-2 hr", 60, 120),
            ("2-4 hr", 120, 240),
            ("4+ hr", 240, int.MaxValue)
        };

        return buckets
            .Select(b =>
            {
                var matching = trades.Where(t =>
                    t.HoldTime.TotalMinutes >= b.Min && t.HoldTime.TotalMinutes < b.Max).ToList();
                var avgPnL = matching.Count > 0 ? matching.Average(t => t.PnLDollars - t.Commission) : 0m;
                return new HoldTimeBucket(b.Label, b.Min, b.Max, matching.Count, avgPnL);
            })
            .Where(b => b.Count > 0)
            .ToList();
    }

    public static StreakAnalysis AnalyzeStreaks(List<BacktestTrade> trades)
    {
        if (trades.Count == 0)
            return new StreakAnalysis(0, 0, 0, 0, 0, 0);

        int maxWin = 0, maxLoss = 0;
        int currentWin = 0, currentLoss = 0;
        var winStreaks = new List<int>();
        var lossStreaks = new List<int>();

        foreach (var trade in trades)
        {
            var netPnL = trade.PnLDollars - trade.Commission;
            if (netPnL > 0)
            {
                if (currentLoss > 0) { lossStreaks.Add(currentLoss); currentLoss = 0; }
                currentWin++;
                if (currentWin > maxWin) maxWin = currentWin;
            }
            else if (netPnL < 0)
            {
                if (currentWin > 0) { winStreaks.Add(currentWin); currentWin = 0; }
                currentLoss++;
                if (currentLoss > maxLoss) maxLoss = currentLoss;
            }
            else
            {
                if (currentWin > 0) { winStreaks.Add(currentWin); currentWin = 0; }
                if (currentLoss > 0) { lossStreaks.Add(currentLoss); currentLoss = 0; }
            }
        }

        if (currentWin > 0) winStreaks.Add(currentWin);
        if (currentLoss > 0) lossStreaks.Add(currentLoss);

        var avgWinStreak = winStreaks.Count > 0 ? (decimal)winStreaks.Average() : 0m;
        var avgLossStreak = lossStreaks.Count > 0 ? (decimal)lossStreaks.Average() : 0m;

        return new StreakAnalysis(maxWin, maxLoss, avgWinStreak, avgLossStreak, winStreaks.Count, lossStreaks.Count);
    }

    public static List<EfficiencyMetric> MaeMfeAnalysis(List<BacktestTrade> trades)
    {
        if (trades.Count == 0) return [];

        return trades
            .GroupBy(t => string.IsNullOrEmpty(t.StrategyName) ? "Unknown" : t.StrategyName)
            .Select(g =>
            {
                var withMfe = g.Where(t => t.MFE > 0).ToList();
                var withStop = g.Where(t => t.InitialStopDistance > 0).ToList();

                var avgCapture = withMfe.Count > 0 ? withMfe.Average(t => t.PnLPoints / t.MFE) : 0m;
                var avgMaeStop = withStop.Count > 0 ? withStop.Average(t => t.MAE / t.InitialStopDistance) : 0m;

                return new EfficiencyMetric(g.Key, avgCapture, avgMaeStop, g.Count());
            })
            .OrderBy(e => e.StrategyName)
            .ToList();
    }

    public static List<ExitReasonBreakdown> ByExitReason(List<BacktestTrade> trades)
    {
        if (trades.Count == 0) return [];

        return trades
            .GroupBy(t => t.ExitReason)
            .Select(g =>
            {
                var totalPnL = g.Sum(t => t.PnLDollars - t.Commission);
                var wins = g.Count(t => t.PnLDollars - t.Commission > 0);
                var winRate = g.Count() > 0 ? (decimal)wins / g.Count() * 100m : 0m;
                return new ExitReasonBreakdown(g.Key, g.Count(), totalPnL, totalPnL / g.Count(), winRate);
            })
            .OrderByDescending(e => e.Count)
            .ToList();
    }

    public static List<StrategyComparison> CompareStrategies(Dictionary<string, BacktestResult> results)
    {
        if (results.Count == 0) return [];

        return results
            .Select(kv => new StrategyComparison(
                kv.Key,
                kv.Value.TotalTrades,
                kv.Value.WinRate,
                kv.Value.ProfitFactor,
                kv.Value.NetPnL,
                kv.Value.SharpeRatio,
                kv.Value.ExpectancyR,
                kv.Value.MaxDrawdownPct,
                kv.Value.CalmarRatio,
                kv.Value.AvgHoldTimeMinutes))
            .OrderByDescending(s => s.ExpectancyR)
            .ToList();
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Local; }
    }
}
