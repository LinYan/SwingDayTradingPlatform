using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Backtesting;

public static class SimulatedOrderFill
{
    public sealed record PendingOrder(
        PositionSide Direction,
        decimal EntryPrice,
        decimal StopPrice,
        decimal? TargetPrice,
        DateTimeOffset EntryTime,
        string SignalReason);

    public sealed record FillResult(
        decimal FillPrice,
        string ExitReason);

    /// <summary>
    /// Checks if stop or target would fill within the given bar.
    /// Returns null if neither is hit.
    /// For same-bar conflicts (both stop and target within H/L), uses proximity to Open to determine fill order.
    /// </summary>
    public static FillResult? TryFillExit(
        PendingOrder order,
        MarketBar bar,
        decimal slippagePoints)
    {
        var isLong = order.Direction == PositionSide.Long;

        var stopHit = isLong
            ? bar.Low <= order.StopPrice
            : bar.High >= order.StopPrice;

        var targetHit = order.TargetPrice.HasValue && (isLong
            ? bar.High >= order.TargetPrice.Value
            : bar.Low <= order.TargetPrice.Value);

        if (!stopHit && !targetHit)
            return null;

        if (stopHit && targetHit)
        {
            // Same-bar conflict: determine which was hit first using proximity to Open
            var stopDistance = Math.Abs(bar.Open - order.StopPrice);
            var targetDistance = Math.Abs(bar.Open - order.TargetPrice!.Value);

            if (stopDistance <= targetDistance)
            {
                return new FillResult(ComputeStopFill(bar, order, isLong, slippagePoints), "StopLoss");
            }
            else
            {
                // Target was closer to open, fill target
                return new FillResult(order.TargetPrice!.Value, "Target");
            }
        }

        if (stopHit)
        {
            return new FillResult(ComputeStopFill(bar, order, isLong, slippagePoints), "StopLoss");
        }

        // Target hit (limit fills at exact price)
        return new FillResult(order.TargetPrice!.Value, "Target");
    }

    /// <summary>
    /// Computes a realistic stop fill price accounting for gap-through scenarios.
    /// When the bar opens beyond the stop (gap), fills at bar.Open with slippage.
    /// </summary>
    private static decimal ComputeStopFill(MarketBar bar, PendingOrder order, bool isLong, decimal slippagePoints)
    {
        var gapped = isLong ? bar.Open < order.StopPrice : bar.Open > order.StopPrice;
        if (gapped)
        {
            // Gap through stop: fill at the gap open with slippage (worse direction)
            return isLong
                ? bar.Open - slippagePoints
                : bar.Open + slippagePoints;
        }

        // Normal stop fill: at the stop price with slippage, clamped to bar range
        return isLong
            ? Math.Max(bar.Low, order.StopPrice - slippagePoints)
            : Math.Min(bar.High, order.StopPrice + slippagePoints);
    }
}
