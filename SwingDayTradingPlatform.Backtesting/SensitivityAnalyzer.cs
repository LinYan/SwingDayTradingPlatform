using System.Reflection;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Backtesting;

public sealed record PerturbationResult(
    decimal PerturbPct,
    decimal NewValue,
    decimal ExpectancyR,
    decimal SharpeRatio,
    decimal ProfitFactor,
    decimal MaxDrawdownPct,
    int TotalTrades);

public sealed record ParameterSensitivity(
    string ParameterName,
    decimal BaseValue,
    List<PerturbationResult> Perturbations,
    decimal MetricVariance);

public sealed record SensitivityResult(
    List<ParameterSensitivity> Parameters,
    string MostSensitiveParameter,
    string LeastSensitiveParameter);

public static class SensitivityAnalyzer
{
    private static readonly decimal[] DefaultPerturbPcts = [-50, -20, -10, 10, 20, 50];

    public static SensitivityResult Analyze(
        List<MarketBar> bars,
        BacktestParameters baseParams,
        BacktestConfig config,
        string[] parameterNames,
        decimal[]? perturbPcts = null,
        string? strategyFilter = null,
        CancellationToken ct = default)
    {
        perturbPcts ??= DefaultPerturbPcts;
        var sensitivities = new List<ParameterSensitivity>();

        foreach (var paramName in parameterNames)
        {
            ct.ThrowIfCancellationRequested();

            var prop = typeof(BacktestParameters).GetProperty(paramName);
            if (prop == null) continue;

            var baseValue = GetDecimalValue(prop, baseParams);
            if (baseValue == 0) continue;

            var perturbations = new List<PerturbationResult>();
            foreach (var pct in perturbPcts)
            {
                ct.ThrowIfCancellationRequested();

                var newValue = baseValue * (1 + pct / 100m);
                if (newValue <= 0) continue;

                var perturbedParams = CloneWithValue(baseParams, prop, newValue);
                var engine = new BacktestEngine(strategyFilter);
                var result = engine.Run(bars, perturbedParams, config, ct);

                perturbations.Add(new PerturbationResult(
                    pct, newValue,
                    result.ExpectancyR, result.SharpeRatio,
                    result.ProfitFactor, result.MaxDrawdownPct,
                    result.TotalTrades));
            }

            var metricVariance = perturbations.Count > 1
                ? CalculateVariance(perturbations.Select(p => p.ExpectancyR).ToList())
                : 0m;

            sensitivities.Add(new ParameterSensitivity(paramName, baseValue, perturbations, metricVariance));
        }

        var mostSensitive = sensitivities.Count > 0
            ? sensitivities.OrderByDescending(s => s.MetricVariance).First().ParameterName
            : "";
        var leastSensitive = sensitivities.Count > 0
            ? sensitivities.OrderBy(s => s.MetricVariance).First().ParameterName
            : "";

        return new SensitivityResult(sensitivities, mostSensitive, leastSensitive);
    }

    private static decimal GetDecimalValue(PropertyInfo prop, BacktestParameters parameters)
    {
        var val = prop.GetValue(parameters);
        return val switch
        {
            decimal d => d,
            int i => i,
            _ => 0m
        };
    }

    private static BacktestParameters CloneWithValue(BacktestParameters source, PropertyInfo prop, decimal newValue)
    {
        // Create a shallow copy using reflection to set init-only properties
        var clone = new BacktestParameters
        {
            FastEmaPeriod = source.FastEmaPeriod,
            SlowEmaPeriod = source.SlowEmaPeriod,
            AtrPeriod = source.AtrPeriod,
            AtrMultiplier = source.AtrMultiplier,
            RewardRiskRatio = source.RewardRiskRatio,
            PullbackTolerancePct = source.PullbackTolerancePct,
            MaxTradesPerDay = source.MaxTradesPerDay,
            MaxLossesPerDay = source.MaxLossesPerDay,
            MaxStopPoints = source.MaxStopPoints,
            CooldownSeconds = source.CooldownSeconds,
            EnableStrategy1 = source.EnableStrategy1,
            EnableStrategy9 = source.EnableStrategy9,
            EnableHourlyBias = source.EnableHourlyBias,
            HourlyRangeLookback = source.HourlyRangeLookback,
            RangeTopPct = source.RangeTopPct,
            RangeBottomPct = source.RangeBottomPct,
            SwingLookback = source.SwingLookback,
            SRClusterAtrFactor = source.SRClusterAtrFactor,
            BigMoveAtrFactor = source.BigMoveAtrFactor,
            TickSize = source.TickSize,
            TrailingStopAtrMultiplier = source.TrailingStopAtrMultiplier,
            TrailingStopActivationBars = source.TrailingStopActivationBars,
            UseBarBreakExit = source.UseBarBreakExit,
            MinBarBreakHoldBars = source.MinBarBreakHoldBars,
            UseReversalBarExit = source.UseReversalBarExit,
            RsiPeriod = source.RsiPeriod,
            EmaPullbackRewardRatio = source.EmaPullbackRewardRatio,
            EmaPullbackTolerance = source.EmaPullbackTolerance,
            EmaMinSlopeAtr = source.EmaMinSlopeAtr,
            EmaBodyMinAtrRatio = source.EmaBodyMinAtrRatio,
            EmaRsiLongMin = source.EmaRsiLongMin,
            EmaRsiLongMax = source.EmaRsiLongMax,
            EmaRsiShortMin = source.EmaRsiShortMin,
            EmaRsiShortMax = source.EmaRsiShortMax,
            EmaStopAtrBuffer = source.EmaStopAtrBuffer,
            BrooksPA_SignalBarBodyRatio = source.BrooksPA_SignalBarBodyRatio,
            BrooksPA_MinBarRangeAtr = source.BrooksPA_MinBarRangeAtr,
            BrooksPA_PullbackLookback = source.BrooksPA_PullbackLookback,
            BrooksPA_EmaToleranceAtr = source.BrooksPA_EmaToleranceAtr,
            BrooksPA_RewardRatio = source.BrooksPA_RewardRatio,
            BrooksPA_MaxStopTicks = source.BrooksPA_MaxStopTicks,
            MaxDailyLossR = source.MaxDailyLossR,
            MaxConsecutiveLossesPerDay = source.MaxConsecutiveLossesPerDay,
            MaxStopTicks = source.MaxStopTicks,
            EnableTimeFilter = source.EnableTimeFilter,
            LunchStartHour = source.LunchStartHour,
            LunchStartMinute = source.LunchStartMinute,
            LunchEndHour = source.LunchEndHour,
            LunchEndMinute = source.LunchEndMinute,
            LateCutoffHour = source.LateCutoffHour,
            LateCutoffMinute = source.LateCutoffMinute,
            MaxDailyTrades = source.MaxDailyTrades,
            EnableBreakEvenStop = source.EnableBreakEvenStop,
            BreakEvenActivationR = source.BreakEvenActivationR
        };

        // Now set the perturbed value using reflection on the backing field
        // For init-only properties, we use unsafe accessor pattern
        var propType = prop.PropertyType;
        if (propType == typeof(int))
            SetInitProperty(clone, prop.Name, (int)Math.Round(newValue));
        else if (propType == typeof(decimal))
            SetInitProperty(clone, prop.Name, newValue);

        return clone;
    }

    private static void SetInitProperty<T>(BacktestParameters obj, string propertyName, T value)
    {
        // Use reflection to set init-only property via backing field
        var field = typeof(BacktestParameters)
            .GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(obj, value);
    }

    private static decimal CalculateVariance(List<decimal> values)
    {
        if (values.Count < 2) return 0m;
        var mean = values.Average();
        return values.Select(v => (v - mean) * (v - mean)).Sum() / (values.Count - 1);
    }
}
