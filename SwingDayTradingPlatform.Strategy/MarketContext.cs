using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Strategy;

public enum DirectionBias { LongOnly, ShortOnly, Both }

public sealed class MarketContext
{
    private readonly MultiStrategyConfig _config;
    private readonly List<MarketBar> _hourlyBars = [];
    private readonly List<MarketBar> _pendingHourlyCandles = [];
    private DateTimeOffset _currentHourBoundary;

    // Running EMA state to avoid O(N) recomputation each bar
    private decimal _runningEma20;
    private decimal _runningEma50;
    private int _emaBarCount;
    private decimal _ema20Multiplier;
    private decimal _ema50Multiplier;

    // Incremental VWAP state
    private decimal _cumPriceVolume;
    private decimal _cumVolume;

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
        Rsi14 = Indicators.Rsi(bars, _config.RsiPeriod);

        // Incremental VWAP (reset is handled by OnNewDay using trading timezone)
        var latestBar = bars[^1];
        var typicalPrice = (latestBar.High + latestBar.Low + latestBar.Close) / 3m;
        _cumPriceVolume += typicalPrice * latestBar.Volume;
        _cumVolume += latestBar.Volume;
        Vwap = _cumVolume > 0 ? _cumPriceVolume / _cumVolume : latestBar.Close;

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
        Bias = DirectionBias.Both;
        RangeHigh = 0;
        RangeLow = 0;
        RangePercentile = 50;
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
            // Hour changed — finalize the previous hour (already in _hourlyBars via partial update)
            _pendingHourlyCandles.Clear();
            _currentHourBoundary = barHour;
        }

        _pendingHourlyCandles.Add(fiveMinBar);

        // Build/update the current partial hourly bar
        var open = _pendingHourlyCandles[0].Open;
        var close = _pendingHourlyCandles[^1].Close;
        var high = _pendingHourlyCandles.Max(b => b.High);
        var low = _pendingHourlyCandles.Min(b => b.Low);
        var volume = _pendingHourlyCandles.Sum(b => b.Volume);
        var openTime = _pendingHourlyCandles[0].OpenTimeUtc;
        var closeTime = _pendingHourlyCandles[^1].CloseTimeUtc;
        var hourlyBar = new MarketBar(openTime, closeTime, open, high, low, close, volume);

        // Replace or add: first candle of new hour adds, subsequent candles replace
        if (_pendingHourlyCandles.Count == 1)
        {
            _hourlyBars.Add(hourlyBar);
        }
        else if (_hourlyBars.Count > 0)
        {
            _hourlyBars[^1] = hourlyBar;
        }
        else
        {
            // Safety fallback: should not happen, but avoid IndexOutOfRangeException
            _hourlyBars.Add(hourlyBar);
        }
    }

    private void UpdateHourlyBias(decimal currentPrice)
    {
        var lookback = Math.Min(_config.HourlyRangeLookback, _hourlyBars.Count);
        if (lookback < 1)
        {
            Bias = DirectionBias.Both;
            return;
        }

        var recentHourly = _hourlyBars.TakeLast(lookback).ToList();
        RangeHigh = recentHourly.Max(b => b.High);
        RangeLow = recentHourly.Min(b => b.Low);

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
