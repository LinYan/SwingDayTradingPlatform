using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Backtesting;

public static class MultiStrategyBacktester
{
    private static readonly string[] StrategyNames =
        ["EmaPullback", "SRReversal", "FiftyPctPullback", "Momentum"];

    public static Dictionary<string, BacktestResult> RunAllStrategies(
        List<MarketBar> bars,
        BacktestParameters parameters,
        BacktestConfig config,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, BacktestResult>();

        foreach (var strategyName in StrategyNames)
        {
            ct.ThrowIfCancellationRequested();
            var engine = new BacktestEngine(strategyName);
            var result = engine.Run(bars, parameters, config, ct);
            results[strategyName] = result;
        }

        return results;
    }

    public static (BacktestResult inSample, BacktestResult outOfSample) SplitWalkForward(
        BacktestResult fullResult, DateOnly cutoffDate, BacktestConfig config)
    {
        var tz = ResolveTimeZone(config.Timezone);

        var isTrades = fullResult.Trades
            .Where(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.ExitTime, tz).DateTime) < cutoffDate)
            .ToList();
        var oosTrades = fullResult.Trades
            .Where(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.ExitTime, tz).DateTime) >= cutoffDate)
            .ToList();

        var isEquity = fullResult.EquityCurve
            .Where(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Timestamp, tz).DateTime) < cutoffDate)
            .ToList();
        var oosEquity = fullResult.EquityCurve
            .Where(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Timestamp, tz).DateTime) >= cutoffDate)
            .ToList();

        var isResult = ResultCalculator.Calculate(fullResult.Parameters, config, isTrades, isEquity, fullResult.StrategyName);

        // Use IS ending equity as OOS starting capital for accurate Sharpe/Sortino
        var oosStartingCapital = isEquity.Count > 0 ? isEquity[^1].Equity : config.StartingCapital;
        var oosConfig = new BacktestConfig
        {
            StartDate = config.StartDate,
            EndDate = config.EndDate,
            StartingCapital = oosStartingCapital,
            CommissionPerTrade = config.CommissionPerTrade,
            SlippagePoints = config.SlippagePoints,
            PointValue = config.PointValue,
            Timezone = config.Timezone,
            EntryWindowStart = config.EntryWindowStart,
            EntryWindowEnd = config.EntryWindowEnd,
            FlattenTime = config.FlattenTime,
            MaxDailyLossPoints = config.MaxDailyLossPoints,
            DbPath = config.DbPath,
            InSampleCutoff = config.InSampleCutoff
        };
        var oosResult = ResultCalculator.Calculate(fullResult.Parameters, oosConfig, oosTrades, oosEquity, fullResult.StrategyName);

        return (isResult, oosResult);
    }

    public static List<DailySummary> ComputeDailySummaries(BacktestResult result, string timezone = "America/New_York")
    {
        var tz = ResolveTimeZone(timezone);
        return result.Trades
            .GroupBy(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.ExitTime, tz).DateTime))
            .Select(g =>
            {
                var wins = g.Count(t => t.PnLPoints > 0);
                var losses = g.Count(t => t.PnLPoints < 0);
                return new DailySummary(
                    g.Key,
                    g.Count(),
                    wins,
                    losses,
                    g.Sum(t => t.PnLPoints),
                    g.Sum(t => t.PnLDollars - t.Commission),
                    result.StrategyName);
            })
            .OrderBy(d => d.Date)
            .ToList();
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Local; }
    }
}
