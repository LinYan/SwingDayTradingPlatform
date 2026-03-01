using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Backtesting;

public sealed class WalkForwardEngine
{
    public sealed class WalkForwardConfig
    {
        public int TrainMonths { get; init; } = 6;
        public int TestMonths { get; init; } = 1;
        public int StepMonths { get; init; } = 1;
    }

    public sealed class WalkForwardFold
    {
        public required int FoldNumber { get; init; }
        public required DateOnly TrainStart { get; init; }
        public required DateOnly TrainEnd { get; init; }
        public required DateOnly TestStart { get; init; }
        public required DateOnly TestEnd { get; init; }
        public required BacktestResult InSampleResult { get; init; }
        public required BacktestResult OutOfSampleResult { get; init; }
        public required BacktestParameters BestParameters { get; init; }
    }

    public sealed class WalkForwardResult
    {
        public required BacktestResult AggregatedOosResult { get; init; }
        public required List<WalkForwardFold> Folds { get; init; }
    }

    public async Task<WalkForwardResult> RunAsync(
        List<MarketBar> allBars,
        BacktestConfig baseConfig,
        ParameterSweep.ParameterRanges sweepRanges,
        WalkForwardConfig wfConfig,
        string? strategyFilter,
        CancellationToken ct,
        Action<string>? onStatus = null)
    {
        var tz = ResolveTimeZone(baseConfig.Timezone);
        var folds = GenerateFolds(allBars, wfConfig, tz);
        var foldResults = new List<WalkForwardFold>();
        var allOosTrades = new List<BacktestTrade>();
        var allOosEquity = new List<EquityPoint>();

        for (var i = 0; i < folds.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (trainStart, trainEnd, testStart, testEnd) = folds[i];

            onStatus?.Invoke($"Walk-forward fold {i + 1}/{folds.Count}: IS {trainStart:yyyy-MM} to {trainEnd:yyyy-MM}");

            // Split bars into IS and OOS
            var isBars = FilterBars(allBars, trainStart, trainEnd, tz);
            var oosBars = FilterBars(allBars, testStart, testEnd, tz);

            if (isBars.Count < 100 || oosBars.Count < 20) continue;

            // Run parameter sweep on IS
            var isConfig = new BacktestConfig
            {
                StartDate = baseConfig.StartDate,
                EndDate = baseConfig.EndDate,
                StartingCapital = baseConfig.StartingCapital,
                CommissionPerTrade = baseConfig.CommissionPerTrade,
                SlippagePoints = baseConfig.SlippagePoints,
                PointValue = baseConfig.PointValue,
                Timezone = baseConfig.Timezone,
                EntryWindowStart = baseConfig.EntryWindowStart,
                EntryWindowEnd = baseConfig.EntryWindowEnd,
                FlattenTime = baseConfig.FlattenTime,
                CsvPath = baseConfig.CsvPath,
                MaxDailyLossPoints = baseConfig.MaxDailyLossPoints,
                DbPath = baseConfig.DbPath,
                InSampleCutoff = baseConfig.InSampleCutoff,
            };
            var sweep = new ParameterSweep();
            var sweepResults = await sweep.RunSweepAsync(isBars, isConfig, sweepRanges, ct);

            // Select best by ExpectancyR (min 5 trades)
            var bestResult = sweepResults
                .Where(r => r.TotalTrades >= 5)
                .OrderByDescending(r => r.ExpectancyR)
                .FirstOrDefault();

            if (bestResult is null) continue;

            var bestParams = bestResult.Parameters;

            // Run OOS with best params
            var oosEngine = new BacktestEngine(strategyFilter);
            var oosResult = oosEngine.Run(oosBars, bestParams, isConfig, ct);

            allOosTrades.AddRange(oosResult.Trades);
            allOosEquity.AddRange(oosResult.EquityCurve);

            foldResults.Add(new WalkForwardFold
            {
                FoldNumber = i + 1,
                TrainStart = trainStart,
                TrainEnd = trainEnd,
                TestStart = testStart,
                TestEnd = testEnd,
                InSampleResult = bestResult,
                OutOfSampleResult = oosResult,
                BestParameters = bestParams
            });
        }

        // Aggregate all OOS trades into a single result
        var defaultParams = new BacktestParameters();
        var aggregatedResult = ResultCalculator.Calculate(
            defaultParams, baseConfig, allOosTrades, allOosEquity, strategyFilter ?? "All");

        return new WalkForwardResult
        {
            AggregatedOosResult = aggregatedResult,
            Folds = foldResults
        };
    }

    public static List<(DateOnly TrainStart, DateOnly TrainEnd, DateOnly TestStart, DateOnly TestEnd)>
        GenerateFolds(List<MarketBar> bars, WalkForwardConfig config, TimeZoneInfo tz)
    {
        if (bars.Count == 0) return [];

        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(bars[0].CloseTimeUtc, tz).DateTime);
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(bars[^1].CloseTimeUtc, tz).DateTime);

        var folds = new List<(DateOnly, DateOnly, DateOnly, DateOnly)>();

        var trainStart = firstDate;
        while (true)
        {
            var trainEnd = trainStart.AddMonths(config.TrainMonths);
            var testStart = trainEnd;
            var testEnd = testStart.AddMonths(config.TestMonths);

            if (testEnd > lastDate) break;

            folds.Add((trainStart, trainEnd, testStart, testEnd));
            trainStart = trainStart.AddMonths(config.StepMonths);
        }

        return folds;
    }

    private static List<MarketBar> FilterBars(List<MarketBar> bars, DateOnly start, DateOnly end, TimeZoneInfo tz)
    {
        return bars
            .Where(b =>
            {
                var d = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(b.CloseTimeUtc, tz).DateTime);
                return d >= start && d < end;
            })
            .ToList();
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Local; }
    }
}
