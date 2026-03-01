using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy;

public sealed class IntradaySwingStrategy
{
    private readonly StrategyConfig _config;
    private readonly List<MarketBar> _bars = [];

    public IntradaySwingStrategy(StrategyConfig config)
    {
        _config = config;
    }

    public string LatestReason { get; private set; } = "Waiting";
    public string LatestSignalText { get; private set; } = "None";
    public int BarsProcessed => _bars.Count;
    public IReadOnlyList<MarketBar> Bars => _bars;

    public StrategySignal? OnBarClosed(
        MarketBar bar,
        PositionSnapshot? currentPosition,
        DateTimeOffset nowInTradingTimezone,
        bool canOpenNewPosition,
        TradingConfig tradingConfig)
    {
        _bars.Add(bar);

        // Trim to prevent unbounded growth and O(N) indicator recomputation
        var keepBars = Math.Max(_config.SlowEmaPeriod, _config.AtrPeriod) + 50;
        if (_bars.Count > keepBars * 2)
            _bars.RemoveRange(0, _bars.Count - keepBars);

        if (_bars.Count < Math.Max(_config.SlowEmaPeriod, _config.AtrPeriod) + 5)
        {
            LatestReason = "Warm-up on 5-minute bars";
            return null;
        }

        var closes = _bars.Select(x => x.Close).ToList();
        var fastEma = Indicators.Ema(closes, _config.FastEmaPeriod);
        var slowEma = Indicators.Ema(closes, _config.SlowEmaPeriod);
        var atr = Indicators.Atr(_bars, _config.AtrPeriod);
        var tolerance = bar.Close * _config.PullbackTolerancePct;
        var trendUp = fastEma > slowEma;
        var trendDown = fastEma < slowEma;
        var pulledBackToFastLong = Math.Abs(bar.Low - fastEma) <= tolerance || Math.Abs(bar.Close - fastEma) <= tolerance;
        var pulledBackToFastShort = Math.Abs(bar.High - fastEma) <= tolerance || Math.Abs(bar.Close - fastEma) <= tolerance;
        var closeAboveFast = bar.Close > fastEma;
        var closeBelowFast = bar.Close < fastEma;

        if (currentPosition is not null && currentPosition.Side != PositionSide.Flat)
        {
            LatestReason = "Managing existing position";
            return null;
        }

        if (!canOpenNewPosition)
        {
            LatestReason = "Outside entry window or trading halted";
            return null;
        }

        if (trendUp && pulledBackToFastLong && closeAboveFast)
        {
            var stop = bar.Close - (atr * _config.AtrMultiplier);
            var target = bar.Close + ((bar.Close - stop) * _config.RewardRiskRatio);
            LatestSignalText = "Long pullback";
            LatestReason = "5-minute EMA20 reclaimed inside EMA50 uptrend";
            return new StrategySignal(
                $"LONG-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                bar.CloseTimeUtc,
                PositionSide.Long,
                bar.Close,
                stop,
                target,
                LatestReason);
        }

        if (trendDown && pulledBackToFastShort && closeBelowFast)
        {
            var stop = bar.Close + (atr * _config.AtrMultiplier);
            var target = bar.Close - ((stop - bar.Close) * _config.RewardRiskRatio);
            LatestSignalText = "Short pullback";
            LatestReason = "5-minute EMA20 rejected inside EMA50 downtrend";
            return new StrategySignal(
                $"SHORT-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                bar.CloseTimeUtc,
                PositionSide.Short,
                bar.Close,
                stop,
                target,
                LatestReason);
        }

        LatestSignalText = "None";
        LatestReason = "No reversal confirmation";
        return null;
    }
}
