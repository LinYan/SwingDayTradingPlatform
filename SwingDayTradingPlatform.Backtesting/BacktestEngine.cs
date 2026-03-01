using SwingDayTradingPlatform.Risk;
using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Strategy;

namespace SwingDayTradingPlatform.Backtesting;

public sealed class BacktestEngine
{
    private readonly string? _strategyFilter;

    public BacktestEngine(string? strategyFilter = null)
    {
        _strategyFilter = strategyFilter;
    }

    public BacktestResult Run(
        List<MarketBar> bars,
        BacktestParameters parameters,
        BacktestConfig config,
        CancellationToken ct = default)
    {
        var multiConfig = BuildMultiConfig(parameters);
        var riskConfig = parameters.ToRiskConfig();
        var engine = new MultiStrategyEngine(multiConfig);
        var riskEngine = new RiskEngine(riskConfig);

        var tradingTz = ResolveTimeZone(config.Timezone);
        if (!TimeSpan.TryParse(config.EntryWindowStart, out var entryStart))
            throw new ArgumentException($"Invalid EntryWindowStart: '{config.EntryWindowStart}'");
        if (!TimeSpan.TryParse(config.EntryWindowEnd, out var entryEnd))
            throw new ArgumentException($"Invalid EntryWindowEnd: '{config.EntryWindowEnd}'");
        if (!TimeSpan.TryParse(config.FlattenTime, out var flattenTime))
            throw new ArgumentException($"Invalid FlattenTime: '{config.FlattenTime}'");

        var equity = config.StartingCapital;
        var peakEquity = equity;
        var trades = new List<BacktestTrade>();
        var equityCurve = new List<EquityPoint>();
        PendingPosition? position = null;
        var tradeNumber = 0;
        DateOnly currentDate = default;
        var dayTradingComplete = false;
        var triggeredSignals = new HashSet<string>();
        var dailyPnLPoints = 0m;
        var exitedThisBar = false;

        var tradingConfig = new TradingConfig
        {
            Symbol = "ES",
            Exchange = "CME",
            Currency = "USD",
            Timezone = config.Timezone,
            EntryWindowStart = config.EntryWindowStart,
            EntryWindowEnd = config.EntryWindowEnd,
            FlattenTime = config.FlattenTime,
            PointValue = config.PointValue,
            BarResolution = "5m"
        };

        equityCurve.Add(new EquityPoint(bars.Count > 0 ? bars[0].OpenTimeUtc : DateTimeOffset.MinValue, equity, 0m));

        for (var i = 0; i < bars.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var bar = bars[i];
            exitedThisBar = false;
            var localTime = TimeZoneInfo.ConvertTime(bar.CloseTimeUtc, tradingTz);
            var tradingDate = DateOnly.FromDateTime(localTime.DateTime);

            // New day reset
            if (tradingDate != currentDate)
            {
                currentDate = tradingDate;
                dayTradingComplete = false;
                dailyPnLPoints = 0m;
                riskEngine.ResetForNewDay();
                triggeredSignals.Clear();

                // Close overnight position after reset so PnL counts toward new day's daily limit
                if (position is not null)
                {
                    var dayEndExit = ApplyExitSlippage(bar.Open, position.Direction, config.SlippagePoints);
                    var flatPnL = CloseTrade(position, dayEndExit, "DayEnd", config, trades, ref tradeNumber, bar.OpenTimeUtc);
                    equity += flatPnL;
                    riskEngine.RegisterClosedTrade(flatPnL);
                    position = null;
                }
            }

            // MAE/MFE tracking for open position
            if (position is not null)
            {
                var isLong = position.Direction == PositionSide.Long;
                var adversePrice = isLong ? bar.Low : bar.High;
                var favorablePrice = isLong ? bar.High : bar.Low;
                var adverseExcursion = isLong
                    ? position.EntryPrice - adversePrice
                    : adversePrice - position.EntryPrice;
                var favorableExcursion = isLong
                    ? favorablePrice - position.EntryPrice
                    : position.EntryPrice - favorablePrice;

                if (adverseExcursion > position.MAE) position.MAE = adverseExcursion;
                if (favorableExcursion > position.MFE) position.MFE = favorableExcursion;
            }

            // Check stop/target fill for existing position
            if (position is not null)
            {
                var pending = position.ToPendingOrder();
                var fillResult = SimulatedOrderFill.TryFillExit(pending, bar, config.SlippagePoints);
                if (fillResult is not null)
                {
                    var pnl = CloseTrade(position, fillResult.FillPrice, fillResult.ExitReason, config, trades, ref tradeNumber, bar.CloseTimeUtc);
                    equity += pnl;
                    var pnlPts = trades[^1].PnLPoints;
                    dailyPnLPoints += pnlPts;
                    riskEngine.RegisterClosedTrade(pnl);
                    position = null;
                    exitedThisBar = true;
                }
            }

            // Check flatten time (15:55 ET)
            if (localTime.TimeOfDay >= flattenTime && position is not null)
            {
                var flattenExit = ApplyExitSlippage(bar.Close, position.Direction, config.SlippagePoints);
                var pnl = CloseTrade(position, flattenExit, "Flatten", config, trades, ref tradeNumber, bar.CloseTimeUtc);
                equity += pnl;
                var pnlPts = trades[^1].PnLPoints;
                dailyPnLPoints += pnlPts;
                riskEngine.RegisterClosedTrade(pnl);
                position = null;
                exitedThisBar = true;
                dayTradingComplete = true;
            }
            else if (localTime.TimeOfDay >= flattenTime)
            {
                dayTradingComplete = true;
            }

            // Daily max loss check (in POINTS) — flatten active position when limit hit
            if (dailyPnLPoints <= -config.MaxDailyLossPoints)
            {
                if (position is not null && !exitedThisBar)
                {
                    var lossExit = ApplyExitSlippage(bar.Close, position.Direction, config.SlippagePoints);
                    var lossPnl = CloseTrade(position, lossExit, "DailyLossLimit", config, trades, ref tradeNumber, bar.CloseTimeUtc);
                    equity += lossPnl;
                    var lossPts = trades[^1].PnLPoints;
                    dailyPnLPoints += lossPts;
                    riskEngine.RegisterClosedTrade(lossPnl);
                    position = null;
                    exitedThisBar = true;
                }
                dayTradingComplete = true;
            }

            // Build position snapshot
            PositionSnapshot? currentPosition = null;
            if (position is not null)
            {
                currentPosition = new PositionSnapshot(
                    tradingConfig.Symbol, "", position.Direction, position.Quantity,
                    position.EntryPrice, bar.Close, 0m, bar.CloseTimeUtc);
            }

            // Check entry window: no entry before 09:40 ET, no entry after 15:50 ET
            var insideWindow = localTime.TimeOfDay >= entryStart && localTime.TimeOfDay <= entryEnd;
            var canOpen = insideWindow &&
                          !dayTradingComplete &&
                          position is null &&
                          riskEngine.CanOpenNewPosition(bar.CloseTimeUtc, position is not null, dayTradingComplete);

            // Feed bar to engine
            var signal = engine.OnBarClosed(bar, currentPosition, localTime, canOpen, tradingConfig);

            // Process flatten signal from bar-break exit
            if (signal is not null && signal.IsFlattenSignal && position is not null)
            {
                var barBreakExit = ApplyExitSlippage(bar.Close, position.Direction, config.SlippagePoints);
                var pnl = CloseTrade(position, barBreakExit, "BarBreakExit", config, trades, ref tradeNumber, bar.CloseTimeUtc);
                equity += pnl;
                var pnlPts = trades[^1].PnLPoints;
                dailyPnLPoints += pnlPts;
                riskEngine.RegisterClosedTrade(pnl);
                position = null;
                exitedThisBar = true;
            }

            // Process entry signal
            if (signal is not null && !signal.IsFlattenSignal && position is null && !dayTradingComplete && !exitedThisBar)
            {
                if (!triggeredSignals.Contains(signal.SignalId))
                {
                    var contracts = riskEngine.CalculateContracts(
                        signal.EntryPrice, signal.StopPrice, equity, config.PointValue);

                    if (contracts > 0)
                    {
                        riskEngine.MarkEntryAttempt(bar.CloseTimeUtc, signal.Reason);
                        triggeredSignals.Add(signal.SignalId);

                        var entryFill = signal.Direction == PositionSide.Long
                            ? bar.Close + config.SlippagePoints
                            : bar.Close - config.SlippagePoints;

                        position = new PendingPosition(
                            signal.Direction,
                            entryFill,
                            signal.StopPrice,
                            signal.TargetPrice,
                            bar.CloseTimeUtc,
                            signal.Reason,
                            DetectStrategyName(signal.Reason),
                            contracts);
                    }
                }
            }

            // Record equity point (update peak BEFORE computing drawdown)
            if (equity > peakEquity) peakEquity = equity;
            var ddPct = peakEquity > 0 ? (peakEquity - equity) / peakEquity * 100m : 0m;
            equityCurve.Add(new EquityPoint(bar.CloseTimeUtc, equity, Math.Max(0m, ddPct)));
        }

        // Force-flatten at end
        if (position is not null && bars.Count > 0)
        {
            var lastBar = bars[^1];
            var endExit = ApplyExitSlippage(lastBar.Close, position.Direction, config.SlippagePoints);
            var pnl = CloseTrade(position, endExit, "EndOfData", config, trades, ref tradeNumber, lastBar.CloseTimeUtc);
            equity += pnl;
            position = null;
        }

        return ResultCalculator.Calculate(parameters, config, trades, equityCurve, _strategyFilter ?? "All");
    }

    private MultiStrategyConfig BuildMultiConfig(BacktestParameters parameters)
    {
        var config = parameters.ToMultiStrategyConfig();

        if (_strategyFilter is not null)
        {
            return new MultiStrategyConfig
            {
                FastEmaPeriod = config.FastEmaPeriod,
                SlowEmaPeriod = config.SlowEmaPeriod,
                AtrPeriod = config.AtrPeriod,
                EnableStrategy1 = _strategyFilter == "EmaPullback",
                EnableStrategy2 = _strategyFilter == "SRReversal",
                EnableStrategy3 = _strategyFilter == "FiftyPctPullback",
                EnableStrategy4 = _strategyFilter == "Momentum",
                EnableHourlyBias = config.EnableHourlyBias,
                HourlyRangeLookback = config.HourlyRangeLookback,
                RangeTopPct = config.RangeTopPct,
                RangeBottomPct = config.RangeBottomPct,
                SwingLookback = config.SwingLookback,
                SRClusterAtrFactor = config.SRClusterAtrFactor,
                BigMoveAtrFactor = config.BigMoveAtrFactor,
                MomentumBars = config.MomentumBars,
                MomentumBodyAtrRatio = config.MomentumBodyAtrRatio,
                TickSize = config.TickSize,
                TrailingStopAtrMultiplier = config.TrailingStopAtrMultiplier,
                TrailingStopActivationBars = config.TrailingStopActivationBars,
                UseBarBreakExit = config.UseBarBreakExit,
                EmaPullbackRewardRatio = config.EmaPullbackRewardRatio,
                EmaPullbackTolerance = config.EmaPullbackTolerance,
                SRMinTouches = config.SRMinTouches,
                SRReversalRewardRatio = config.SRReversalRewardRatio,
                MomentumRewardRatio = config.MomentumRewardRatio,
                MomentumPullbackWindowBars = config.MomentumPullbackWindowBars,
                MaxStopPoints = config.MaxStopPoints,
                BigMoveStaleBars = config.BigMoveStaleBars
            };
        }

        return config;
    }

    private decimal CloseTrade(
        PendingPosition pos,
        decimal exitPrice,
        string exitReason,
        BacktestConfig config,
        List<BacktestTrade> trades,
        ref int tradeNumber,
        DateTimeOffset exitTime)
    {
        tradeNumber++;
        var isLong = pos.Direction == PositionSide.Long;
        var pnlPoints = isLong
            ? exitPrice - pos.EntryPrice
            : pos.EntryPrice - exitPrice;
        var pnlDollars = pnlPoints * config.PointValue * pos.Quantity;
        var commission = config.CommissionPerTrade * 2 * pos.Quantity;

        trades.Add(new BacktestTrade(
            tradeNumber,
            pos.EntryTime,
            exitTime,
            pos.EntryPrice,
            exitPrice,
            pos.Direction.ToString(),
            exitReason,
            pnlPoints,
            pnlDollars,
            commission)
        {
            StrategyName = pos.StrategyName,
            MAE = pos.MAE,
            MFE = pos.MFE,
            EntryReason = pos.SignalReason,
            ExitReasonDetail = exitReason
        });

        return pnlDollars - commission;
    }

    private static string DetectStrategyName(string reason)
    {
        // Check bracket-delimited strategy name first (e.g., "[EmaPullback] ...")
        if (reason.Contains('['))
        {
            var bracketEnd = reason.IndexOf(']');
            if (bracketEnd > 1)
            {
                var name = reason[1..bracketEnd];
                if (name == "EmaPullback" || name == "SRReversal" ||
                    name == "FiftyPctPullback" || name == "Momentum")
                    return name;
            }
        }

        // Fallback: check specific patterns (order matters - most specific first)
        if (reason.Contains("50%", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("FiftyPct", StringComparison.OrdinalIgnoreCase))
            return "FiftyPctPullback";
        if (reason.Contains("Momentum", StringComparison.OrdinalIgnoreCase))
            return "Momentum";
        if (reason.Contains("S/R", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("Reversal", StringComparison.OrdinalIgnoreCase))
            return "SRReversal";
        if (reason.Contains("EMA", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("Higher low", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("Lower high", StringComparison.OrdinalIgnoreCase))
            return "EmaPullback";
        return "Unknown";
    }

    /// <summary>Apply slippage to a market-order exit: worse fill for the position holder.</summary>
    private static decimal ApplyExitSlippage(decimal price, PositionSide direction, decimal slippage) =>
        direction == PositionSide.Long ? price - slippage : price + slippage;

    private static TimeZoneInfo ResolveTimeZone(string configured)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(configured); }
        catch { return TimeZoneInfo.Local; }
    }

    private sealed class PendingPosition(
        PositionSide direction,
        decimal entryPrice,
        decimal stopPrice,
        decimal? targetPrice,
        DateTimeOffset entryTime,
        string signalReason,
        string strategyName,
        int quantity = 1)
    {
        public PositionSide Direction { get; } = direction;
        public decimal EntryPrice { get; } = entryPrice;
        public decimal StopPrice { get; } = stopPrice;
        public decimal? TargetPrice { get; } = targetPrice;
        public DateTimeOffset EntryTime { get; } = entryTime;
        public string SignalReason { get; } = signalReason;
        public string StrategyName { get; } = strategyName;
        public int Quantity { get; } = quantity;
        public decimal MAE { get; set; }
        public decimal MFE { get; set; }

        public SimulatedOrderFill.PendingOrder ToPendingOrder() =>
            new(Direction, EntryPrice, StopPrice, TargetPrice, EntryTime, SignalReason);
    }
}
