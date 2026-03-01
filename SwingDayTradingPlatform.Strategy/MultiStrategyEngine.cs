using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Strategy.Strategies;

namespace SwingDayTradingPlatform.Strategy;

public sealed class MultiStrategyEngine
{
    private readonly MultiStrategyConfig _config;
    private readonly MarketContext _context;
    private readonly List<MarketBar> _bars = [];
    private ActiveTradeContext? _activeTrade;
    private DateOnly _currentDate;
    private int _dailyTradeCount;

    public MultiStrategyEngine(MultiStrategyConfig config)
    {
        _config = config;
        _context = new MarketContext(config);
    }

    public string LatestReason { get; private set; } = "Waiting";
    public string LatestSignalText { get; private set; } = "None";
    public int BarsProcessed => _bars.Count;
    public IReadOnlyList<MarketBar> Bars => _bars;
    public decimal FastEma => _context.Ema20;
    public decimal SlowEma => _context.Ema50;

    public StrategySignal? OnBarClosed(
        MarketBar bar,
        PositionSnapshot? currentPosition,
        DateTimeOffset nowInTradingTimezone,
        bool canOpenNewPosition,
        TradingConfig tradingConfig)
    {
        _bars.Add(bar);

        // New day reset
        var tradingDate = DateOnly.FromDateTime(nowInTradingTimezone.DateTime);
        if (_currentDate != tradingDate)
        {
            _currentDate = tradingDate;
            _context.OnNewDay();
            _dailyTradeCount = 0;
            // Preserve _activeTrade if we're still in a position (overnight hold);
            // only clear it when flat so exit management continues into the new day.
            if (currentPosition is null || currentPosition.Side == PositionSide.Flat)
                _activeTrade = null;
        }

        // Trim bars to prevent unbounded memory growth; keep enough for indicators
        var keepBars = Math.Max(_config.SlowEmaPeriod, _config.AtrPeriod) + 50;
        if (_bars.Count > keepBars * 2)
        {
            var trimCount = _bars.Count - keepBars;
            _bars.RemoveRange(0, trimCount);

            // Adjust swing point indices to match the trimmed bar list
            _context.AdjustSwingPointIndices(trimCount);
        }

        var warmup = Math.Max(_config.SlowEmaPeriod, _config.AtrPeriod) + 5;
        if (_bars.Count < warmup)
        {
            LatestReason = "Warm-up on 5-minute bars";
            return null;
        }

        // Update shared market context
        _context.OnNewBar(_bars, nowInTradingTimezone);

        var idx = _bars.Count - 1;
        var isInPosition = currentPosition is not null && currentPosition.Side != PositionSide.Flat;

        // === Phase 1: Exit check ===
        if (isInPosition && _activeTrade is not null && idx >= 1)
        {
            var prevBar = _activeTrade.PreviousBar;

            // Increment bars since entry and track swing extremes
            _activeTrade.BarsSinceEntry++;
            if (bar.High > _activeTrade.HighestHighSinceEntry)
                _activeTrade.HighestHighSinceEntry = bar.High;
            if (bar.Low < _activeTrade.LowestLowSinceEntry)
                _activeTrade.LowestLowSinceEntry = bar.Low;

            // Break-even stop: move stop to entry when profit reaches 1R
            if (_config.EnableBreakEvenStop && !_activeTrade.BreakEvenActivated && _activeTrade.RiskAmount > 0)
            {
                var unrealizedR = _activeTrade.Direction == PositionSide.Long
                    ? (bar.Close - _activeTrade.EntryPrice) / _activeTrade.RiskAmount
                    : (_activeTrade.EntryPrice - bar.Close) / _activeTrade.RiskAmount;
                if (unrealizedR >= _config.BreakEvenActivationR)
                {
                    _activeTrade.TrailingStopLevel = _activeTrade.EntryPrice;
                    _activeTrade.BreakEvenActivated = true;
                }
            }

            // ATR trailing stop (activates after N bars)
            if (_activeTrade.BarsSinceEntry >= _config.TrailingStopActivationBars && _context.Atr14 > 0)
            {
                var atrTrail = _context.Atr14 * _config.TrailingStopAtrMultiplier;

                if (_activeTrade.Direction == PositionSide.Long)
                {
                    var newTrail = _activeTrade.HighestHighSinceEntry - atrTrail;
                    if (newTrail > _activeTrade.TrailingStopLevel)
                        _activeTrade.TrailingStopLevel = newTrail;

                    if (bar.Low <= _activeTrade.TrailingStopLevel)
                    {
                        LatestSignalText = "Flatten (trailing stop)";
                        LatestReason = $"[{_activeTrade.OwningStrategyName}] ATR trailing stop hit at {_activeTrade.TrailingStopLevel:F2}";
                        _activeTrade.PreviousBar = bar;

                        var flattenSignal = new StrategySignal(
                            $"TRAIL-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                            bar.CloseTimeUtc,
                            PositionSide.Short,
                            _activeTrade.TrailingStopLevel,
                            _activeTrade.TrailingStopLevel,
                            null,
                            LatestReason,
                            IsFlattenSignal: true);

                        _activeTrade = null;
                        return flattenSignal;
                    }
                }
                else // Short
                {
                    var newTrail = _activeTrade.LowestLowSinceEntry + atrTrail;
                    if (newTrail < _activeTrade.TrailingStopLevel)
                        _activeTrade.TrailingStopLevel = newTrail;

                    if (bar.High >= _activeTrade.TrailingStopLevel)
                    {
                        LatestSignalText = "Flatten (trailing stop)";
                        LatestReason = $"[{_activeTrade.OwningStrategyName}] ATR trailing stop hit at {_activeTrade.TrailingStopLevel:F2}";
                        _activeTrade.PreviousBar = bar;

                        var flattenSignal = new StrategySignal(
                            $"TRAIL-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                            bar.CloseTimeUtc,
                            PositionSide.Long,
                            _activeTrade.TrailingStopLevel,
                            _activeTrade.TrailingStopLevel,
                            null,
                            LatestReason,
                            IsFlattenSignal: true);

                        _activeTrade = null;
                        return flattenSignal;
                    }
                }
            }

            // Bar-break trailing exit (global config flag or per-trade flag)
            if ((_config.UseBarBreakExit || _activeTrade.UseBarBreakExit)
                && _activeTrade.BarsSinceEntry >= _config.MinBarBreakHoldBars
                && PatternDetector.CheckBarBreakExit(bar, prevBar, _activeTrade.Direction))
            {
                LatestSignalText = "Flatten (bar-break)";
                LatestReason = $"[{_activeTrade.OwningStrategyName}] Bar-break exit triggered";

                _activeTrade.PreviousBar = bar;

                var flattenSignal = new StrategySignal(
                    $"EXIT-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                    bar.CloseTimeUtc,
                    _activeTrade.Direction == PositionSide.Long ? PositionSide.Short : PositionSide.Long,
                    bar.Close,
                    bar.Close,
                    null,
                    LatestReason,
                    IsFlattenSignal: true);

                _activeTrade = null;
                return flattenSignal;
            }

            // Reversal bar exit (behind config flag, default off)
            if (_config.UseReversalBarExit && PatternDetector.CheckReversalBarExit(bar, _activeTrade.Direction))
            {
                LatestSignalText = "Flatten (reversal bar)";
                LatestReason = $"[{_activeTrade.OwningStrategyName}] Reversal bar exit — bar closed opposite direction";
                _activeTrade.PreviousBar = bar;
                var flattenSignal = new StrategySignal(
                    $"REV-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                    bar.CloseTimeUtc,
                    _activeTrade.Direction == PositionSide.Long ? PositionSide.Short : PositionSide.Long,
                    bar.Close, bar.Close, null, LatestReason, IsFlattenSignal: true);
                _activeTrade = null;
                return flattenSignal;
            }

            // Target check
            if (_activeTrade.TargetPrice.HasValue)
            {
                var hitTarget = _activeTrade.Direction == PositionSide.Long
                    ? bar.High >= _activeTrade.TargetPrice.Value
                    : bar.Low <= _activeTrade.TargetPrice.Value;

                if (hitTarget)
                {
                    LatestSignalText = "Flatten (target hit)";
                    LatestReason = $"[{_activeTrade.OwningStrategyName}] Target reached at {_activeTrade.TargetPrice:F2}";

                    _activeTrade.PreviousBar = bar;

                    var flattenSignal = new StrategySignal(
                        $"TGT-{bar.CloseTimeUtc:yyyyMMddHHmmss}",
                        bar.CloseTimeUtc,
                        _activeTrade.Direction == PositionSide.Long ? PositionSide.Short : PositionSide.Long,
                        _activeTrade.TargetPrice.Value,
                        _activeTrade.TargetPrice.Value,
                        null,
                        LatestReason,
                        IsFlattenSignal: true);

                    _activeTrade = null;
                    return flattenSignal;
                }
            }

            // Update previous bar for next iteration
            _activeTrade.PreviousBar = bar;
            LatestReason = $"[{_activeTrade.OwningStrategyName}] Managing position";
            return null;
        }

        // If broker says we're in a position but we lost tracking, skip entry
        if (isInPosition)
        {
            LatestReason = "In position (untracked)";
            return null;
        }

        // === Phase 2: Entry check ===
        if (!canOpenNewPosition)
        {
            LatestReason = "Outside entry window or trading halted";
            return null;
        }

        // Time-of-day filter
        if (_config.EnableTimeFilter)
        {
            var hour = nowInTradingTimezone.Hour;
            var minute = nowInTradingTimezone.Minute;
            var timeMinutes = hour * 60 + minute;
            var lunchStart = _config.LunchStartHour * 60 + _config.LunchStartMinute;
            var lunchEnd = _config.LunchEndHour * 60 + _config.LunchEndMinute;
            var lateCutoff = _config.LateCutoffHour * 60 + _config.LateCutoffMinute;

            if ((timeMinutes >= lunchStart && timeMinutes < lunchEnd) || timeMinutes >= lateCutoff)
            {
                LatestReason = "Outside optimal trading window";
                return null;
            }
        }

        // Daily trade limit
        if (_dailyTradeCount >= _config.MaxDailyTrades)
        {
            LatestReason = $"Daily trade limit reached ({_config.MaxDailyTrades})";
            return null;
        }

        // Iterate strategies by priority, first match wins
        StrategySignal? signal = null;

        if (_config.EnableStrategy1)
            signal = EmaPullbackStrategy.Evaluate(_bars, _context, _config, idx);

        if (signal is null && _config.EnableStrategy5)
            signal = EmaPullbackBarBreakStrategy.Evaluate(_bars, _context, _config, idx);

        if (signal is null && _config.EnableStrategy7)
            signal = SecondLegStrategy.Evaluate(_bars, _context, _config, idx);

        if (signal is null && _config.EnableStrategy9)
            signal = BrooksPriceActionStrategy.Evaluate(_bars, _context, _config, idx);

        if (signal is null)
        {
            LatestSignalText = "None";
            LatestReason = "No pattern match";
            return null;
        }

        // Apply 1H bias filter
        if (_config.EnableHourlyBias && !PassesBiasFilter(signal.Direction))
        {
            LatestSignalText = "None";
            LatestReason = $"Signal filtered by 1H bias ({_context.Bias}, percentile {_context.RangePercentile:F0}%)";
            return null;
        }

        // Record active trade context
        var strategyName = signal.Reason.Contains('[')
            ? signal.Reason.Split(']')[0].TrimStart('[')
            : "Unknown";

        _activeTrade = new ActiveTradeContext
        {
            OwningStrategyName = strategyName,
            Direction = signal.Direction,
            EntryPrice = signal.EntryPrice,
            StopPrice = signal.StopPrice,
            TargetPrice = signal.TargetPrice,
            EntryBar = bar,
            PreviousBar = bar,
            HighestHighSinceEntry = bar.High,
            LowestLowSinceEntry = bar.Low,
            BarsSinceEntry = 0,
            TrailingStopLevel = signal.StopPrice,
            RiskAmount = Math.Abs(signal.EntryPrice - signal.StopPrice),
            UseBarBreakExit = strategyName == EmaPullbackBarBreakStrategy.Name
        };

        _dailyTradeCount++;
        LatestSignalText = $"{signal.Direction} ({strategyName})";
        LatestReason = signal.Reason;

        return signal;
    }

    private bool PassesBiasFilter(PositionSide direction)
    {
        return _context.Bias switch
        {
            DirectionBias.LongOnly => direction == PositionSide.Long,
            DirectionBias.ShortOnly => direction == PositionSide.Short,
            DirectionBias.Both => true,
            _ => true
        };
    }
}
