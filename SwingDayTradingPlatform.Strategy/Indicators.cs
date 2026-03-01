using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy;

public static class Indicators
{
    public static decimal Ema(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count == 0)
            return 0m;

        var multiplier = 2m / (period + 1);
        var ema = values[0];
        for (var i = 1; i < values.Count; i++)
            ema = ((values[i] - ema) * multiplier) + ema;

        return ema;
    }

    public static decimal Atr(IReadOnlyList<MarketBar> bars, int period)
    {
        if (bars.Count < 2)
            return 0m;

        // Only compute the last `period` true ranges to avoid O(N) waste
        var startIdx = Math.Max(1, bars.Count - period);
        var sum = 0m;
        var count = 0;
        for (var i = startIdx; i < bars.Count; i++)
        {
            var current = bars[i];
            var previous = bars[i - 1];
            var tr = Math.Max(
                current.High - current.Low,
                Math.Max(
                    Math.Abs(current.High - previous.Close),
                    Math.Abs(current.Low - previous.Close)));
            sum += tr;
            count++;
        }

        return count > 0 ? sum / count : 0m;
    }

    /// <summary>
    /// Computes session VWAP for all bars. Resets when trading date changes.
    /// Uses typical price = (H+L+C)/3.
    /// </summary>
    public static List<decimal> SessionVwap(IReadOnlyList<MarketBar> bars, TimeZoneInfo tradingTz)
    {
        var vwaps = new List<decimal>(bars.Count);
        var cumPv = 0m;
        var cumVol = 0m;
        DateOnly currentDate = default;

        for (var i = 0; i < bars.Count; i++)
        {
            var localTime = TimeZoneInfo.ConvertTime(bars[i].OpenTimeUtc, tradingTz);
            var tradingDate = DateOnly.FromDateTime(localTime.DateTime);

            if (tradingDate != currentDate)
            {
                cumPv = 0m;
                cumVol = 0m;
                currentDate = tradingDate;
            }

            var typicalPrice = (bars[i].High + bars[i].Low + bars[i].Close) / 3m;
            var volume = bars[i].Volume;
            if (volume <= 0) volume = 1; // avoid division by zero for missing volume

            cumPv += typicalPrice * volume;
            cumVol += volume;

            vwaps.Add(cumVol > 0 ? cumPv / cumVol : typicalPrice);
        }

        return vwaps;
    }

    /// <summary>
    /// Computes Wilder's smoothed RSI for the given bars.
    /// Seeds with SMA of first <paramref name="period"/> bars, then uses exponential decay.
    /// Returns 50 if insufficient data.
    /// </summary>
    public static decimal Rsi(IReadOnlyList<MarketBar> bars, int period)
    {
        if (bars.Count < period + 1)
            return 50m;

        var gainSum = 0m;
        var lossSum = 0m;

        // Seed phase: SMA of first `period` changes
        for (var i = 1; i <= period; i++)
        {
            var change = bars[i].Close - bars[i - 1].Close;
            if (change > 0) gainSum += change;
            else lossSum += Math.Abs(change);
        }

        var avgGain = gainSum / period;
        var avgLoss = lossSum / period;

        // Wilder smoothing for remaining bars
        for (var i = period + 1; i < bars.Count; i++)
        {
            var change = bars[i].Close - bars[i - 1].Close;
            var gain = change > 0 ? change : 0m;
            var loss = change < 0 ? Math.Abs(change) : 0m;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        if (avgLoss == 0m)
            return 100m;

        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    /// <summary>
    /// Computes EMA series for all bars (returns one value per bar).
    /// </summary>
    public static List<decimal> EmaSeries(IReadOnlyList<decimal> closePrices, int period)
    {
        var result = new List<decimal>(closePrices.Count);
        if (closePrices.Count == 0) return result;

        var multiplier = 2m / (period + 1);
        var ema = closePrices[0];
        result.Add(ema);

        for (var i = 1; i < closePrices.Count; i++)
        {
            ema = ((closePrices[i] - ema) * multiplier) + ema;
            result.Add(ema);
        }

        return result;
    }
}
