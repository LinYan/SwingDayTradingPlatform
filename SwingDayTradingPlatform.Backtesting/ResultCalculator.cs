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

        // R-based metrics
        var avgRPerTrade = CalculateAvgRPerTrade(trades);
        var expectancyR = CalculateExpectancyR(trades);
        var maxDrawdownR = CalculateMaxDrawdownR(trades);
        var rDistribution = CalculateRDistribution(trades);

        // Advanced metrics
        var cagr = CalculateCAGR(config.StartingCapital, endingCapital, dailyReturns);
        var calmar = CalculateCalmarRatio(cagr, maxDrawdownPct);
        var recoveryFactor = CalculateRecoveryFactor(netPnL, maxDrawdown);
        var payoffRatio = CalculatePayoffRatio(avgWinPoints, avgLossPoints);
        var (maxConsWins, maxConsLosses) = CalculateMaxConsecutiveWinsLosses(trades);
        var (avgHoldMin, maxHoldMin) = CalculateHoldTimes(trades);
        var ulcerIndex = CalculateUlcerIndex(equityCurve);
        var tailRatio = CalculateTailRatio(trades);
        var mfeEfficiency = CalculateMfeEfficiency(trades);
        var maeRatio = CalculateMaeRatio(trades);

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
            StrategyName = strategyName,
            AvgRPerTrade = avgRPerTrade,
            ExpectancyR = expectancyR,
            MaxDrawdownR = maxDrawdownR,
            RDistribution = rDistribution,
            CalmarRatio = calmar,
            RecoveryFactor = recoveryFactor,
            CAGR = cagr,
            PayoffRatio = payoffRatio,
            MaxConsecutiveWins = maxConsWins,
            MaxConsecutiveLosses = maxConsLosses,
            AvgHoldTimeMinutes = avgHoldMin,
            MaxHoldTimeMinutes = maxHoldMin,
            UlcerIndex = ulcerIndex,
            TailRatio = tailRatio,
            MfeEfficiency = mfeEfficiency,
            MaeRatio = maeRatio
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

        var equity = (double)startingCapital;
        var pctReturns = new double[dailyReturns.Count];
        for (var i = 0; i < dailyReturns.Count; i++)
        {
            var pnl = (double)dailyReturns[i].PnL;
            pctReturns[i] = equity > 0 ? pnl / equity : 0;
            equity += pnl;
        }

        var mean = pctReturns.Average();
        var downsideSquared = pctReturns.Select(r => r < 0 ? r * r : 0.0).ToArray();
        var hasDownside = downsideSquared.Any(d => d > 0);

        if (!hasDownside)
            return mean > 0 ? 999.99m : 0m;

        var downsideDev = Math.Sqrt(downsideSquared.Sum() / (downsideSquared.Length - 1));
        if (downsideDev < 1e-10)
            return 0m;

        return (decimal)(mean / downsideDev * Math.Sqrt(252));
    }

    public static decimal CalculateAvgRPerTrade(List<BacktestTrade> trades)
    {
        if (trades.Count == 0) return 0m;
        return trades.Average(t => t.RMultiple);
    }

    public static decimal CalculateExpectancyR(List<BacktestTrade> trades)
    {
        if (trades.Count == 0) return 0m;

        var wins = trades.Where(t => t.RMultiple > 0).ToList();
        var losses = trades.Where(t => t.RMultiple <= 0).ToList();

        var winRate = (decimal)wins.Count / trades.Count;
        var lossRate = (decimal)losses.Count / trades.Count;
        var avgWinR = wins.Count > 0 ? wins.Average(t => t.RMultiple) : 0m;
        var avgLossR = losses.Count > 0 ? Math.Abs(losses.Average(t => t.RMultiple)) : 0m;

        return winRate * avgWinR - lossRate * avgLossR;
    }

    public static decimal CalculateMaxDrawdownR(List<BacktestTrade> trades)
    {
        if (trades.Count == 0) return 0m;

        var runningR = 0m;
        var peakR = 0m;
        var maxDDR = 0m;

        foreach (var trade in trades)
        {
            runningR += trade.RMultiple;
            if (runningR > peakR)
                peakR = runningR;

            var dd = peakR - runningR;
            if (dd > maxDDR)
                maxDDR = dd;
        }

        return maxDDR;
    }

    public static List<RDistributionBucket> CalculateRDistribution(List<BacktestTrade> trades)
    {
        if (trades.Count == 0) return [];

        var bucketSize = 0.5m;
        var minR = trades.Min(t => t.RMultiple);
        var maxR = trades.Max(t => t.RMultiple);

        var bucketStart = Math.Floor(minR / bucketSize) * bucketSize;
        var bucketEnd = Math.Ceiling(maxR / bucketSize) * bucketSize;
        if (bucketEnd <= maxR) bucketEnd += bucketSize;

        var buckets = new List<RDistributionBucket>();
        for (var b = bucketStart; b < bucketEnd; b += bucketSize)
        {
            var bMin = b;
            var bMax = b + bucketSize;
            var count = trades.Count(t => t.RMultiple >= bMin && t.RMultiple < bMax);
            var pct = trades.Count > 0 ? (decimal)count / trades.Count * 100m : 0m;
            buckets.Add(new RDistributionBucket(bMin, bMax, count, pct));
        }

        return buckets;
    }

    public static decimal CalculateCAGR(decimal startingCapital, decimal endingCapital, List<DailyReturn> dailyReturns)
    {
        if (startingCapital <= 0 || endingCapital <= 0 || dailyReturns.Count < 2)
            return 0m;

        var totalDays = (dailyReturns[^1].Date.ToDateTime(TimeOnly.MinValue) -
                         dailyReturns[0].Date.ToDateTime(TimeOnly.MinValue)).TotalDays;
        if (totalDays <= 0) return 0m;

        var years = totalDays / 365.25;
        var ratio = (double)endingCapital / (double)startingCapital;
        if (ratio <= 0) return 0m;

        var cagr = Math.Pow(ratio, 1.0 / years) - 1.0;
        return (decimal)(cagr * 100);
    }

    public static decimal CalculateCalmarRatio(decimal cagr, decimal maxDrawdownPct)
    {
        if (maxDrawdownPct <= 0) return cagr > 0 ? 999.99m : 0m;
        return cagr / maxDrawdownPct;
    }

    public static decimal CalculateRecoveryFactor(decimal netPnL, decimal maxDrawdown)
    {
        if (maxDrawdown <= 0) return netPnL > 0 ? 999.99m : 0m;
        return netPnL / maxDrawdown;
    }

    public static decimal CalculatePayoffRatio(decimal avgWinPoints, decimal avgLossPoints)
    {
        if (avgLossPoints <= 0) return avgWinPoints > 0 ? 999.99m : 0m;
        return avgWinPoints / avgLossPoints;
    }

    public static (int MaxConsecutiveWins, int MaxConsecutiveLosses) CalculateMaxConsecutiveWinsLosses(
        List<BacktestTrade> trades)
    {
        if (trades.Count == 0) return (0, 0);

        int maxWins = 0, maxLosses = 0;
        int currentWins = 0, currentLosses = 0;

        foreach (var trade in trades)
        {
            var netPnL = trade.PnLDollars - trade.Commission;
            if (netPnL > 0)
            {
                currentWins++;
                currentLosses = 0;
                if (currentWins > maxWins) maxWins = currentWins;
            }
            else if (netPnL < 0)
            {
                currentLosses++;
                currentWins = 0;
                if (currentLosses > maxLosses) maxLosses = currentLosses;
            }
            else
            {
                currentWins = 0;
                currentLosses = 0;
            }
        }

        return (maxWins, maxLosses);
    }

    public static (decimal AvgMinutes, decimal MaxMinutes) CalculateHoldTimes(List<BacktestTrade> trades)
    {
        if (trades.Count == 0) return (0m, 0m);

        var holdMinutes = trades.Select(t => (decimal)t.HoldTime.TotalMinutes).ToList();
        return (holdMinutes.Average(), holdMinutes.Max());
    }

    public static decimal CalculateUlcerIndex(List<EquityPoint> equityCurve)
    {
        if (equityCurve.Count < 2) return 0m;

        var peak = equityCurve[0].Equity;
        var sumSquaredDD = 0.0;

        foreach (var point in equityCurve)
        {
            if (point.Equity > peak) peak = point.Equity;
            var ddPct = peak > 0 ? (double)(peak - point.Equity) / (double)peak * 100.0 : 0.0;
            sumSquaredDD += ddPct * ddPct;
        }

        return (decimal)Math.Sqrt(sumSquaredDD / equityCurve.Count);
    }

    public static decimal CalculateTailRatio(List<BacktestTrade> trades)
    {
        if (trades.Count < 20) return 0m;

        var netPnLs = trades.Select(t => t.PnLDollars - t.Commission).OrderBy(x => x).ToList();
        var p5Index = (int)(netPnLs.Count * 0.05);
        var p95Index = (int)(netPnLs.Count * 0.95);
        if (p95Index >= netPnLs.Count) p95Index = netPnLs.Count - 1;

        var p5 = netPnLs[p5Index];
        var p95 = netPnLs[p95Index];

        if (p5 >= 0) return 999.99m;
        return p95 / Math.Abs(p5);
    }

    public static decimal CalculateMfeEfficiency(List<BacktestTrade> trades)
    {
        var tradesWithMfe = trades.Where(t => t.MFE > 0).ToList();
        if (tradesWithMfe.Count == 0) return 0m;

        var efficiencies = tradesWithMfe.Select(t => t.PnLPoints / t.MFE).ToList();
        return efficiencies.Average();
    }

    public static decimal CalculateMaeRatio(List<BacktestTrade> trades)
    {
        var tradesWithStop = trades.Where(t => t.InitialStopDistance > 0).ToList();
        if (tradesWithStop.Count == 0) return 0m;

        var ratios = tradesWithStop.Select(t => t.MAE / t.InitialStopDistance).ToList();
        return ratios.Average();
    }
}
