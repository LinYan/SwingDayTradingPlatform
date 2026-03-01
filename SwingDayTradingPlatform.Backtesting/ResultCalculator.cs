namespace SwingDayTradingPlatform.Backtesting;

public static class ResultCalculator
{
    public static BacktestResult Calculate(
        BacktestParameters parameters,
        BacktestConfig config,
        List<BacktestTrade> trades,
        List<EquityPoint> equityCurve,
        string strategyName = "All")
    {
        var grossPnL = trades.Sum(t => t.PnLDollars);
        var totalCommissions = trades.Sum(t => t.Commission);
        var netPnL = grossPnL - totalCommissions;
        var endingCapital = config.StartingCapital + netPnL;
        var returnPct = config.StartingCapital > 0 ? netPnL / config.StartingCapital * 100m : 0m;

        var winningTrades = trades.Count(t => t.PnLDollars - t.Commission > 0);
        var losingTrades = trades.Count(t => t.PnLDollars - t.Commission < 0);
        var flattenedTrades = trades.Count(t => t.ExitReason == "Flatten");
        var winRate = trades.Count > 0 ? (decimal)winningTrades / trades.Count * 100m : 0m;

        var wins = trades.Where(t => t.PnLDollars - t.Commission > 0).ToList();
        var losses = trades.Where(t => t.PnLDollars - t.Commission < 0).ToList();
        var avgWinPoints = wins.Count > 0 ? wins.Average(t => t.PnLPoints) : 0m;
        var avgLossPoints = losses.Count > 0 ? Math.Abs(losses.Average(t => t.PnLPoints)) : 0m;

        var grossWins = wins.Sum(t => t.PnLDollars - t.Commission);
        var grossLosses = Math.Abs(losses.Sum(t => t.PnLDollars - t.Commission));
        var profitFactor = grossLosses > 0 ? grossWins / grossLosses : grossWins > 0 ? 999.99m : 0m;

        var (maxDrawdown, maxDrawdownPct) = CalculateMaxDrawdown(equityCurve);
        var dailyReturns = CalculateDailyReturns(trades, config);
        var monthlyReturns = CalculateMonthlyReturns(dailyReturns, config.StartingCapital);
        var sharpe = CalculateSharpe(dailyReturns, config.StartingCapital);
        var sortino = CalculateSortino(dailyReturns, config.StartingCapital);

        return new BacktestResult
        {
            Parameters = parameters,
            NetPnL = netPnL,
            GrossPnL = grossPnL,
            TotalCommissions = totalCommissions,
            ReturnPct = returnPct,
            TotalTrades = trades.Count,
            WinningTrades = winningTrades,
            LosingTrades = losingTrades,
            FlattenedTrades = flattenedTrades,
            WinRate = winRate,
            ProfitFactor = profitFactor,
            AvgWinPoints = avgWinPoints,
            AvgLossPoints = avgLossPoints,
            MaxDrawdown = maxDrawdown,
            MaxDrawdownPct = maxDrawdownPct,
            SharpeRatio = sharpe,
            SortinoRatio = sortino,
            StartingCapital = config.StartingCapital,
            EndingCapital = endingCapital,
            EquityCurve = equityCurve,
            DailyReturns = dailyReturns,
            MonthlyReturns = monthlyReturns,
            Trades = trades,
            StrategyName = strategyName
        };
    }

    private static (decimal maxDD, decimal maxDDPct) CalculateMaxDrawdown(List<EquityPoint> curve)
    {
        if (curve.Count == 0)
            return (0m, 0m);

        var peak = curve[0].Equity;
        var maxDD = 0m;
        var maxDDPct = 0m;

        foreach (var point in curve)
        {
            if (point.Equity > peak)
                peak = point.Equity;

            var dd = peak - point.Equity;
            if (dd > maxDD)
            {
                maxDD = dd;
                maxDDPct = peak > 0 ? dd / peak * 100m : 0m;
            }
        }

        return (maxDD, maxDDPct);
    }

    public static List<DailyReturn> CalculateDailyReturns(List<BacktestTrade> trades, BacktestConfig config)
    {
        var tz = ResolveTimeZone(config.Timezone);
        return trades
            .GroupBy(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.ExitTime, tz).DateTime))
            .Select(g => new DailyReturn(
                g.Key,
                g.Sum(t => t.PnLDollars - t.Commission),
                g.Count()))
            .OrderBy(d => d.Date)
            .ToList();
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Local; }
    }

    public static List<MonthlyReturn> CalculateMonthlyReturns(List<DailyReturn> dailyReturns, decimal startingCapital)
    {
        // Group daily P&L by month, tracking beginning-of-month equity for accurate % returns
        var monthlies = new Dictionary<(int Year, int Month), (decimal pnl, decimal startEquity)>();
        var equity = startingCapital;

        foreach (var dr in dailyReturns)
        {
            var key = (dr.Date.Year, dr.Date.Month);
            if (!monthlies.ContainsKey(key))
                monthlies[key] = (0m, equity);

            var (pnl, startEq) = monthlies[key];
            monthlies[key] = (pnl + dr.PnL, startEq);
            equity += dr.PnL;
        }

        return monthlies
            .OrderBy(kv => kv.Key.Year).ThenBy(kv => kv.Key.Month)
            .Select(kv =>
            {
                var pct = kv.Value.startEquity > 0 ? kv.Value.pnl / kv.Value.startEquity * 100m : 0m;
                return new MonthlyReturn(kv.Key.Year, kv.Key.Month, kv.Value.pnl, pct);
            })
            .ToList();
    }

    private static decimal CalculateSharpe(List<DailyReturn> dailyReturns, decimal startingCapital)
    {
        if (dailyReturns.Count < 2 || startingCapital <= 0)
            return 0m;

        // Convert daily PnL to percentage returns against rolling equity
        var equity = (double)startingCapital;
        var pctReturns = new double[dailyReturns.Count];
        for (var i = 0; i < dailyReturns.Count; i++)
        {
            var pnl = (double)dailyReturns[i].PnL;
            pctReturns[i] = equity > 0 ? pnl / equity : 0;
            equity += pnl;
        }

        var mean = pctReturns.Average();
        var variance = pctReturns.Select(r => (r - mean) * (r - mean)).Sum() / (pctReturns.Length - 1);
        var stdDev = Math.Sqrt(variance);

        if (stdDev < 1e-10)
            return 0m;

        return (decimal)(mean / stdDev * Math.Sqrt(252));
    }

    private static decimal CalculateSortino(List<DailyReturn> dailyReturns, decimal startingCapital)
    {
        if (dailyReturns.Count < 2 || startingCapital <= 0)
            return 0m;

        // Convert daily PnL to percentage returns against rolling equity
        var equity = (double)startingCapital;
        var pctReturns = new double[dailyReturns.Count];
        for (var i = 0; i < dailyReturns.Count; i++)
        {
            var pnl = (double)dailyReturns[i].PnL;
            pctReturns[i] = equity > 0 ? pnl / equity : 0;
            equity += pnl;
        }

        var mean = pctReturns.Average();
        // Use all observations for downside deviation (standard Sortino formula):
        // sqrt(sum(min(r, 0)^2) / N) where N = total observations
        var downsideSquared = pctReturns.Select(r => r < 0 ? r * r : 0.0).ToArray();
        var hasDownside = downsideSquared.Any(d => d > 0);

        if (!hasDownside)
            return mean > 0 ? 999.99m : 0m;

        var downsideDev = Math.Sqrt(downsideSquared.Sum() / (downsideSquared.Length - 1));
        if (downsideDev < 1e-10)
            return 0m;

        return (decimal)(mean / downsideDev * Math.Sqrt(252));
    }
}
