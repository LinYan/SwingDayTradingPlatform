using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy;

public enum DirectionBias { LongOnly, ShortOnly, Both }

public sealed class MarketContext
{
    private readonly MultiStrategyConfig _config;
    private readonly List<MarketBar> _hourlyBars = [];
    private readonly List<MarketBar> _pendingHourlyCandles = [];
    private DateTimeOffset _currentHourBoundary;

    // Running state for hourly bar aggregation (avoids LINQ Max/Min/Sum per 5-min bar)
    private decimal _hourlyHigh;
    private decimal _hourlyLow;
    private decimal _hourlyVolume;

    // Running EMA state to avoid O(N) recomputation each bar
    private decimal _runningEma20;
    private decimal _runningEma50;
    private int _emaBarCount;
    private decimal _ema20Multiplier;
    private decimal _ema50Multiplier;

    // Incremental VWAP state
    private decimal _cumPriceVolume;
    private decimal _cumVolume;

    // Incremental RSI state (Wilder's smoothing)
    private decimal _rsiAvgGain;
    private decimal _rsiAvgLoss;
    private int _rsiBarCount;
    private decimal _rsiPrevClose;

    public MarketContext(MultiStrategyConfig config)
    {
        _config = config;
        _ema20Multiplier = 2m / (config.FastEmaPeriod + 1);
        _ema50Multiplier = 2m / (config.SlowEmaPeriod + 1);
    }

    public decimal Ema20 { get; private set; }
    public decimal PreviousEma20 { get; private set; }
    public decimal Ema50 { get; private set; }
    public decimal Atr14 { get; private set; }
    public decimal Vwap { get; private set; }
    public decimal Rsi14 { get; private set; }
    public IReadOnlyList<MarketBar> HourlyBars => _hourlyBars;
    public decimal RangeHigh { get; private set; }
    public decimal RangeLow { get; private set; }
    public decimal RangePercentile { get; private set; } = 50;
    public DirectionBias Bias { get; private set; } = DirectionBias.Both;
    public List<SwingPoint> SwingPoints { get; } = [];
    public List<SRLevel> SRLevels { get; } = [];
    public BigMoveInfo? LatestBigMove { get; private set; }
    public int BarsSinceOpen { get; private set; }

    public void OnNewBar(IReadOnlyList<MarketBar> bars, DateTimeOffset tradingLocalTime)
    {
        if (bars.Count == 0) return;

        var warmup = Math.Max(_config.SlowEmaPeriod, _config.AtrPeriod) + 5;
        if (bars.Count < warmup)
            return;

        // Update running EMAs incrementally instead of recomputing from scratch
        var latestClose = bars[^1].Close;
        if (_emaBarCount == 0)
        {
            // Seed: compute full EMA from all available closes
            for (var i = 0; i < bars.Count; i++)
            {
                var c = bars[i].Close;
                if (i == 0)
                {
                    _runningEma20 = c;
                    _runningEma50 = c;
                }
                else
                {
                    _runningEma20 = (c - _runningEma20) * _ema20Multiplier + _runningEma20;
                    _runningEma50 = (c - _runningEma50) * _ema50Multiplier + _runningEma50;
                }
            }
            _emaBarCount = bars.Count;
        }
        else
        {
            // Incremental update with just the latest bar
            _runningEma20 = (latestClose - _runningEma20) * _ema20Multiplier + _runningEma20;
            _runningEma50 = (latestClose - _runningEma50) * _ema50Multiplier + _runningEma50;
            _emaBarCount = bars.Count;
        }

        PreviousEma20 = Ema20;
        Ema20 = _runningEma20;
        Ema50 = _runningEma50;
        Atr14 = Indicators.Atr(bars, _config.AtrPeriod);
        Rsi14 = UpdateRsi(bars, _config.RsiPeriod);

        // Incremental VWAP (reset is handled by OnNewDay using trading timezone)
        var latestBar = bars[^1];
        var typicalPrice = (latestBar.High + latestBar.Low + latestBar.Close) / 3m;
        _cumPriceVolume += typicalPrice * latestBar.Volume;
        _cumVolume += latestBar.Volume;
        Vwap = _cumVolume > 0 ? _cumPriceVolume / _cumVolume : latestBar.Close;

        BarsSinceOpen++;

        // Update swing points (trim to last 50 to prevent unbounded growth)
        PatternDetector.UpdateSwingPoints(bars, SwingPoints, _config.SwingLookback);
        if (SwingPoints.Count > 50)
            SwingPoints.RemoveRange(0, SwingPoints.Count - 50);

        // Update S/R levels
        var clusterTolerance = Atr14 * _config.SRClusterAtrFactor;
        PatternDetector.UpdateSRLevels(SwingPoints, SRLevels, clusterTolerance);

        // Detect big move
        LatestBigMove = PatternDetector.DetectBigMove(bars, Atr14, _config.BigMoveAtrFactor);

        // Aggregate 5m bar into 1H using trading timezone
        AggregateHourlyBar(latestBar, tradingLocalTime);

        // Update 1H bias
        if (_config.EnableHourlyBias)
            UpdateHourlyBias(latestBar.Close);
    }

    public void OnNewDay()
    {
        SwingPoints.Clear();
        SRLevels.Clear();
        LatestBigMove = null;
        _hourlyBars.Clear();
        _pendingHourlyCandles.Clear();
        _currentHourBoundary = default;
        _hourlyHigh = decimal.MinValue;
        _hourlyLow = decimal.MaxValue;
        _hourlyVolume = 0m;
        Bias = DirectionBias.Both;
        RangeHigh = 0;
        RangeLow = 0;
        RangePercentile = 50;
        BarsSinceOpen = 0;
        // Note: EMA state is NOT reset — EMA should continue incrementally across days
        _cumPriceVolume = 0;
        _cumVolume = 0;
        Vwap = 0;
    }

    /// <summary>
    /// Adjusts swing point bar indices after bar list trimming.
    /// Removes swing points that fall outside the trimmed range.
    /// </summary>
    public void AdjustSwingPointIndices(int trimCount)
    {
        for (var i = SwingPoints.Count - 1; i >= 0; i--)
        {
            var adjusted = SwingPoints[i].BarIndex - trimCount;
            if (adjusted < 0)
                SwingPoints.RemoveAt(i);
            else
                SwingPoints[i] = SwingPoints[i] with { BarIndex = adjusted };
        }
    }

    /// <summary>
    /// Incremental RSI: seed with SMA on first call, then O(1) Wilder smoothing per bar.
    /// Falls back to full computation if bars were trimmed (count dropped).
    /// </summary>
    private decimal UpdateRsi(IReadOnlyList<MarketBar> bars, int period)
    {
        if (bars.Count < period + 1)
            return 50m;

        if (_rsiBarCount == 0 || _rsiBarCount > bars.Count)
        {
            // Seed: SMA of first `period` changes, then smooth remaining
            var gainSum = 0m;
            var lossSum = 0m;
            for (var i = 1; i <= period; i++)
            {
                var change = bars[i].Close - bars[i - 1].Close;
                if (change > 0) gainSum += change;
                else lossSum += Math.Abs(change);
            }

            _rsiAvgGain = gainSum / period;
            _rsiAvgLoss = lossSum / period;

            for (var i = period + 1; i < bars.Count; i++)
            {
                var change = bars[i].Close - bars[i - 1].Close;
                var gain = change > 0 ? change : 0m;
                var loss = change < 0 ? Math.Abs(change) : 0m;
                _rsiAvgGain = (_rsiAvgGain * (period - 1) + gain) / period;
                _rsiAvgLoss = (_rsiAvgLoss * (period - 1) + loss) / period;
            }

            _rsiPrevClose = bars[^1].Close;
            _rsiBarCount = bars.Count;
        }
        else if (bars.Count > _rsiBarCount)
        {
            // Incremental: process only the new bar
            var change = bars[^1].Close - _rsiPrevClose;
            var gain = change > 0 ? change : 0m;
            var loss = change < 0 ? Math.Abs(change) : 0m;
            _rsiAvgGain = (_rsiAvgGain * (period - 1) + gain) / period;
            _rsiAvgLoss = (_rsiAvgLoss * (period - 1) + loss) / period;
            _rsiPrevClose = bars[^1].Close;
            _rsiBarCount = bars.Count;
        }

        if (_rsiAvgLoss == 0m)
            return _rsiAvgGain == 0m ? 50m : 100m; // No movement → neutral; all gains → overbought

        var rs = _rsiAvgGain / _rsiAvgLoss;
        return 100m - (100m / (1m + rs));
    }

    private void AggregateHourlyBar(MarketBar fiveMinBar, DateTimeOffset tradingLocalTime)
    {
        // Determine the hour boundary in trading timezone (truncate to hour)
        var barHour = new DateTimeOffset(
            tradingLocalTime.Year,
            tradingLocalTime.Month,
            tradingLocalTime.Day,
            tradingLocalTime.Hour,
            0, 0,
            tradingLocalTime.Offset);

        if (_currentHourBoundary == default)
            _currentHourBoundary = barHour;

        if (barHour > _currentHourBoundary)
        {
            // Hour changed — reset running state for new hour
            _pendingHourlyCandles.Clear();
            _currentHourBoundary = barHour;
            _hourlyHigh = decimal.MinValue;
            _hourlyLow = decimal.MaxValue;
            _hourlyVolume = 0m;
        }

        _pendingHourlyCandles.Add(fiveMinBar);

        // Update running high/low/volume (O(1) per bar instead of O(N) LINQ)
        if (fiveMinBar.High > _hourlyHigh) _hourlyHigh = fiveMinBar.High;
        if (fiveMinBar.Low < _hourlyLow) _hourlyLow = fiveMinBar.Low;
        _hourlyVolume += fiveMinBar.Volume;

        // Build the current partial hourly bar from running state
        var open = _pendingHourlyCandles[0].Open;
        var hourlyBar = new MarketBar(
            _pendingHourlyCandles[0].OpenTimeUtc,
            fiveMinBar.CloseTimeUtc,
            open,
            _hourlyHigh,
            _hourlyLow,
            fiveMinBar.Close,
            _hourlyVolume);

        // Replace or add: first candle of new hour adds, subsequent candles replace
        if (_pendingHourlyCandles.Count == 1)
            _hourlyBars.Add(hourlyBar);
        else if (_hourlyBars.Count > 0)
            _hourlyBars[^1] = hourlyBar;
        else
            _hourlyBars.Add(hourlyBar);
    }

    private void UpdateHourlyBias(decimal currentPrice)
    {
        var lookback = Math.Min(_config.HourlyRangeLookback, _hourlyBars.Count);
        if (lookback < 1)
        {
            Bias = DirectionBias.Both;
            return;
        }

        // Compute range high/low without LINQ allocation
        var startIdx = _hourlyBars.Count - lookback;
        var rangeHigh = decimal.MinValue;
        var rangeLow = decimal.MaxValue;
        for (var i = startIdx; i < _hourlyBars.Count; i++)
        {
            if (_hourlyBars[i].High > rangeHigh) rangeHigh = _hourlyBars[i].High;
            if (_hourlyBars[i].Low < rangeLow) rangeLow = _hourlyBars[i].Low;
        }
        RangeHigh = rangeHigh;
        RangeLow = rangeLow;

        var range = RangeHigh - RangeLow;
        if (range <= 0)
        {
            Bias = DirectionBias.Both;
            RangePercentile = 50;
            return;
        }

        RangePercentile = (currentPrice - RangeLow) / range * 100m;

        if (RangePercentile >= _config.RangeTopPct)
            Bias = DirectionBias.ShortOnly;
        else if (RangePercentile <= _config.RangeBottomPct)
            Bias = DirectionBias.LongOnly;
        else
            Bias = DirectionBias.Both;
    }
}
