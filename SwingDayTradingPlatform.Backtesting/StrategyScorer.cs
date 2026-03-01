namespace SwingDayTradingPlatform.Backtesting;

public sealed record StrategyRankResult(
    string StrategyName,
    decimal CompositeScore,
    decimal OosExpectancyR,
    decimal OosMaxDrawdownR,
    decimal OosProfitFactor,
    decimal OosWinRate,
    decimal PctProfitableMonths,
    decimal MonthlyReturnVariance,
    decimal AvgTradesPerDay,
    BacktestResult OosResult);

public sealed class ScoringWeights
{
    public decimal ExpectancyWeight { get; init; } = 0.40m;
    public decimal ProfitFactorWeight { get; init; } = 0.20m;
    public decimal ProfitableMonthsWeight { get; init; } = 0.10m;
    public decimal DrawdownPenaltyWeight { get; init; } = 0.15m;
    public decimal VariancePenaltyWeight { get; init; } = 0.10m;
    public decimal OverTradingPenaltyWeight { get; init; } = 0.05m;
    public int MinOosTrades { get; init; } = 10;
}

public static class StrategyScorer
{
    public static List<StrategyRankResult> Rank(
        Dictionary<string, BacktestResult> oosResults,
        ScoringWeights? weights = null)
    {
        var w = weights ?? new ScoringWeights();
        var rankings = new List<StrategyRankResult>();

        foreach (var (name, result) in oosResults)
        {
            if (result.TotalTrades < w.MinOosTrades) continue;

            var profitableMonths = result.MonthlyReturns.Count > 0
                ? (decimal)result.MonthlyReturns.Count(m => m.PnL > 0) / result.MonthlyReturns.Count * 100m
                : 0m;

            var monthlyVariance = CalculateMonthlyVariance(result.MonthlyReturns);

            var tradingDays = result.DailyReturns.Count;
            var avgTradesPerDay = tradingDays > 0 ? (decimal)result.TotalTrades / tradingDays : 0m;

            var score = ComputeScore(
                result.ExpectancyR,
                result.ProfitFactor,
                profitableMonths,
                result.MaxDrawdownR,
                monthlyVariance,
                avgTradesPerDay,
                w);

            rankings.Add(new StrategyRankResult(
                name,
                score,
                result.ExpectancyR,
                result.MaxDrawdownR,
                result.ProfitFactor,
                result.WinRate,
                profitableMonths,
                monthlyVariance,
                avgTradesPerDay,
                result));
        }

        return rankings.OrderByDescending(r => r.CompositeScore).ToList();
    }

    private static decimal ComputeScore(
        decimal expectancyR,
        decimal profitFactor,
        decimal pctProfitableMonths,
        decimal maxDrawdownR,
        decimal monthlyVariance,
        decimal avgTradesPerDay,
        ScoringWeights w)
    {
        var score = 0m;

        // Positive factors
        score += w.ExpectancyWeight * expectancyR;
        score += w.ProfitFactorWeight * Math.Min(profitFactor, 5m) / 5m;
        score += w.ProfitableMonthsWeight * pctProfitableMonths / 100m;

        // Penalties
        score -= w.DrawdownPenaltyWeight * maxDrawdownR;
        score -= w.VariancePenaltyWeight * monthlyVariance;

        // Over-trading penalty: penalize > 3 trades/day
        if (avgTradesPerDay > 3m)
            score -= w.OverTradingPenaltyWeight * (avgTradesPerDay - 3m);

        return score;
    }

    private static decimal CalculateMonthlyVariance(List<MonthlyReturn> monthlyReturns)
    {
        if (monthlyReturns.Count < 2) return 0m;

        var pcts = monthlyReturns.Select(m => (double)m.ReturnPct).ToArray();
        var mean = pcts.Average();
        var variance = pcts.Select(p => (p - mean) * (p - mean)).Sum() / (pcts.Length - 1);
        return (decimal)variance;
    }
}
