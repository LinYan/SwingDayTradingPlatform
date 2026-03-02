using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Backtesting;

public sealed class SweepProgressEventArgs(int completed, int total) : EventArgs
{
    public int Completed { get; } = completed;
    public int Total { get; } = total;
}

public sealed class ParameterSweep
{
    public event EventHandler<SweepProgressEventArgs>? ProgressChanged;

    public sealed class ParameterRanges
    {
        public int[] FastEmaPeriods { get; init; } = [20];
        public int[] SlowEmaPeriods { get; init; } = [50];
        public int[] AtrPeriods { get; init; } = [14];
        public decimal[] AtrMultipliers { get; init; } = [1.5m];
        public decimal[] RewardRiskRatios { get; init; } = [1.5m];
        public decimal[] PullbackTolerancePcts { get; init; } = [0.0015m];
        public int[] MaxTradesPerDays { get; init; } = [5];
        public int[] MaxLossesPerDays { get; init; } = [3];
        public decimal[] MaxStopPointsList { get; init; } = [10m];
    }

    public static List<BacktestParameters> GenerateCombinations(ParameterRanges ranges)
    {
        var combos = new List<BacktestParameters>();

        foreach (var fast in ranges.FastEmaPeriods)
        foreach (var slow in ranges.SlowEmaPeriods)
        foreach (var atrPeriod in ranges.AtrPeriods)
        foreach (var atrMult in ranges.AtrMultipliers)
        foreach (var rr in ranges.RewardRiskRatios)
        foreach (var pullback in ranges.PullbackTolerancePcts)
        foreach (var maxTrades in ranges.MaxTradesPerDays)
        foreach (var maxLosses in ranges.MaxLossesPerDays)
        foreach (var maxStop in ranges.MaxStopPointsList)
        {
            // Filter invalid: fast must be < slow
            if (fast >= slow)
                continue;

            combos.Add(new BacktestParameters
            {
                FastEmaPeriod = fast,
                SlowEmaPeriod = slow,
                AtrPeriod = atrPeriod,
                AtrMultiplier = atrMult,
                RewardRiskRatio = rr,
                PullbackTolerancePct = pullback,
                MaxTradesPerDay = maxTrades,
                MaxLossesPerDay = maxLosses,
                MaxStopPoints = maxStop
            });
        }

        return combos;
    }

    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    public async Task<List<BacktestResult>> RunSweepAsync(
        List<MarketBar> bars,
        BacktestConfig config,
        ParameterRanges ranges,
        CancellationToken ct)
    {
        var combinations = GenerateCombinations(ranges);
        if (combinations.Count == 0)
            return [];

        var completed = 0;
        var total = combinations.Count;
        var results = new BacktestResult[total];

        ProgressChanged?.Invoke(this, new SweepProgressEventArgs(0, total));

        await Parallel.ForEachAsync(
            Enumerable.Range(0, total),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = ct
            },
            (index, token) =>
            {
                var engine = new BacktestEngine();
                results[index] = engine.Run(bars, combinations[index], config, token);
                var done = Interlocked.Increment(ref completed);
                ProgressChanged?.Invoke(this, new SweepProgressEventArgs(done, total));
                return ValueTask.CompletedTask;
            });

        return results
            .OrderByDescending(r => r.NetPnL)
            .ToList();
    }

    public static int[] ParseIntRange(string input) =>
        input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();

    public static decimal[] ParseDecimalRange(string input) =>
        input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();
}
