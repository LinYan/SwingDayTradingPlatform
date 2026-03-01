namespace SwingDayTradingPlatform.Backtesting;

public sealed record PercentileBand(string Label, decimal P5, decimal P25, decimal P50, decimal P75, decimal P95);

public sealed record MonteCarloResult(
    int Iterations,
    int OriginalTradeCount,
    PercentileBand NetPnL,
    PercentileBand MaxDrawdown,
    PercentileBand MaxDrawdownPct,
    PercentileBand SharpeRatio,
    decimal ProbabilityOfRuin,
    decimal RuinThresholdPct);

public static class MonteCarloEngine
{
    public static MonteCarloResult Run(
        List<BacktestTrade> trades,
        decimal startingCapital,
        decimal pointValue,
        int iterations = 1000,
        int? seed = null,
        decimal ruinThresholdPct = 25m)
    {
        if (trades.Count == 0)
            return new MonteCarloResult(
                iterations, 0,
                new PercentileBand("Net PnL", 0, 0, 0, 0, 0),
                new PercentileBand("Max DD $", 0, 0, 0, 0, 0),
                new PercentileBand("Max DD %", 0, 0, 0, 0, 0),
                new PercentileBand("Sharpe", 0, 0, 0, 0, 0),
                0, ruinThresholdPct);

        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        var tradeCount = trades.Count;

        var allNetPnL = new decimal[iterations];
        var allMaxDD = new decimal[iterations];
        var allMaxDDPct = new decimal[iterations];
        var allSharpe = new decimal[iterations];
        var ruinCount = 0;

        for (var i = 0; i < iterations; i++)
        {
            var resampled = new List<decimal>(tradeCount);
            for (var j = 0; j < tradeCount; j++)
            {
                var idx = rng.Next(tradeCount);
                resampled.Add(trades[idx].PnLDollars - trades[idx].Commission);
            }

            var (netPnL, maxDD, maxDDPct, sharpe) = SimulateEquityCurve(resampled, startingCapital);
            allNetPnL[i] = netPnL;
            allMaxDD[i] = maxDD;
            allMaxDDPct[i] = maxDDPct;
            allSharpe[i] = sharpe;

            if (maxDDPct >= ruinThresholdPct)
                ruinCount++;
        }

        var probRuin = (decimal)ruinCount / iterations * 100m;

        return new MonteCarloResult(
            iterations,
            tradeCount,
            MakeBand("Net PnL", allNetPnL),
            MakeBand("Max DD $", allMaxDD),
            MakeBand("Max DD %", allMaxDDPct),
            MakeBand("Sharpe", allSharpe),
            probRuin,
            ruinThresholdPct);
    }

    private static (decimal netPnL, decimal maxDD, decimal maxDDPct, decimal sharpe) SimulateEquityCurve(
        List<decimal> tradePnLs, decimal startingCapital)
    {
        var equity = startingCapital;
        var peak = equity;
        var maxDD = 0m;
        var maxDDPct = 0m;

        var dailyReturns = new List<double>();
        var dayPnL = 0m;
        const int tradesPerDay = 3; // approximate grouping for Sharpe

        for (var i = 0; i < tradePnLs.Count; i++)
        {
            equity += tradePnLs[i];
            dayPnL += tradePnLs[i];

            if (equity > peak) peak = equity;
            var dd = peak - equity;
            if (dd > maxDD)
            {
                maxDD = dd;
                maxDDPct = peak > 0 ? dd / peak * 100m : 0m;
            }

            if ((i + 1) % tradesPerDay == 0 || i == tradePnLs.Count - 1)
            {
                var startEq = equity - dayPnL;
                if (startEq > 0)
                    dailyReturns.Add((double)dayPnL / (double)startEq);
                dayPnL = 0m;
            }
        }

        var netPnL = equity - startingCapital;
        var sharpe = CalculateSimpleSharpe(dailyReturns);

        return (netPnL, maxDD, maxDDPct, sharpe);
    }

    private static decimal CalculateSimpleSharpe(List<double> returns)
    {
        if (returns.Count < 2) return 0m;

        var mean = returns.Average();
        var variance = returns.Select(r => (r - mean) * (r - mean)).Sum() / (returns.Count - 1);
        var stdDev = Math.Sqrt(variance);

        if (stdDev < 1e-10) return 0m;
        return (decimal)(mean / stdDev * Math.Sqrt(252));
    }

    private static PercentileBand MakeBand(string label, decimal[] values)
    {
        Array.Sort(values);
        return new PercentileBand(
            label,
            Percentile(values, 0.05),
            Percentile(values, 0.25),
            Percentile(values, 0.50),
            Percentile(values, 0.75),
            Percentile(values, 0.95));
    }

    private static decimal Percentile(decimal[] sorted, double p)
    {
        if (sorted.Length == 0) return 0m;
        var index = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper || upper >= sorted.Length) return sorted[lower];
        var fraction = (decimal)(index - lower);
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }
}
