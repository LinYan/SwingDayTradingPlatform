using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Risk;

public sealed class RiskEngine
{
    private readonly RiskConfig _config;
    private DateTimeOffset _lastEntryAttemptUtc = DateTimeOffset.MinValue;
    private decimal _dailyCumulativeR;
    private int _consecutiveLosses;

    public RiskEngine(RiskConfig config)
    {
        _config = config;
    }

    public bool KillSwitchArmed { get; private set; }
    public bool TradingHalted { get; private set; }
    public string LastReason { get; private set; } = "Ready";
    public int TradeCountToday { get; private set; }
    public int LossCountToday { get; private set; }
    public int ConsecutiveLosses => _consecutiveLosses;
    public decimal DailyCumulativeR => _dailyCumulativeR;

    public void ResetForNewDay()
    {
        TradingHalted = false;
        KillSwitchArmed = false;
        LastReason = "New day";
        _lastEntryAttemptUtc = DateTimeOffset.MinValue;
        TradeCountToday = 0;
        LossCountToday = 0;
        _dailyCumulativeR = 0m;
        _consecutiveLosses = 0;
    }

    public void RestoreDayState(int tradeCount, int lossCount)
    {
        TradeCountToday = tradeCount;
        LossCountToday = lossCount;
    }

    public void ArmKillSwitch(string reason)
    {
        KillSwitchArmed = true;
        TradingHalted = true;
        LastReason = reason;
    }

    public void EvaluateDailyLoss(AccountSnapshot? account)
    {
        if (account is null)
            return;

        var loss = _config.UseUnrealizedPnLForDailyLimit
            ? account.RealizedPnL + account.UnrealizedPnL
            : account.RealizedPnL;

        if (loss <= -_config.MaxDailyLoss)
            ArmKillSwitch($"Daily loss limit hit: {loss:0.##}");
    }

    public bool CanOpenNewPosition(DateTimeOffset nowUtc, bool hasPosition, bool alreadyTradedAfterFlatten)
    {
        if (KillSwitchArmed || TradingHalted || hasPosition || alreadyTradedAfterFlatten)
            return false;

        if (TradeCountToday >= _config.MaxTradesPerDay)
        {
            TradingHalted = true;
            LastReason = $"Daily trade cap hit ({_config.MaxTradesPerDay})";
            return false;
        }

        if (LossCountToday >= _config.MaxLossesPerDay)
        {
            ArmKillSwitch($"Stopped after {LossCountToday} losing trades.");
            return false;
        }

        if ((nowUtc - _lastEntryAttemptUtc).TotalSeconds < _config.CooldownSeconds)
        {
            LastReason = "Cooldown active";
            return false;
        }

        return true;
    }

    public int CalculateContracts(decimal entryPrice, decimal stopPrice, decimal accountNetLiq, decimal pointValue, decimal tickSize = 0.25m)
    {
        var stopDistance = Math.Abs(entryPrice - stopPrice);
        if (stopDistance <= 0)
        {
            LastReason = "Invalid signal: stop distance is zero";
            return 0;
        }

        if (stopDistance > _config.MaxStopPoints)
        {
            LastReason = $"Stop distance {stopDistance:0.##} > max {_config.MaxStopPoints:0.##}";
            return 0;
        }

        if (_config.MaxStopTicks > 0 && tickSize > 0)
        {
            var stopTicks = stopDistance / tickSize;
            if (stopTicks > _config.MaxStopTicks)
            {
                LastReason = $"Stop ticks {stopTicks:0.#} > max {_config.MaxStopTicks}";
                return 0;
            }
        }

        if (_config.FixedContracts > 0)
            return Math.Min(_config.FixedContracts, _config.MaxContracts);

        var riskBudget = Math.Max(1m, accountNetLiq * (_config.RiskPerTradePct / 100m));
        var contractRisk = stopDistance * pointValue;
        var contracts = (int)Math.Floor(riskBudget / contractRisk);
        if (contracts <= 0)
        {
            LastReason = $"Insufficient capital for 1 contract (risk ${contractRisk:N0} > budget ${riskBudget:N0})";
            return 0;
        }

        contracts = Math.Min(contracts, _config.MaxContracts);
        return contracts;
    }

    public void MarkEntryAttempt(DateTimeOffset nowUtc, string reason)
    {
        _lastEntryAttemptUtc = nowUtc;
        TradeCountToday++;
        LastReason = reason;
    }

    public void RegisterClosedTrade(decimal realizedPnLDelta, decimal rMultiple = 0m)
    {
        if (realizedPnLDelta < 0)
        {
            LossCountToday++;
            _consecutiveLosses++;
        }
        else
        {
            _consecutiveLosses = 0;
        }

        _dailyCumulativeR += rMultiple;

        if (LossCountToday >= _config.MaxLossesPerDay)
            ArmKillSwitch($"Stopped after {LossCountToday} losing trades.");

        if (_consecutiveLosses >= _config.MaxConsecutiveLossesPerDay)
            ArmKillSwitch($"Consecutive loss limit hit ({_consecutiveLosses})");

        if (_dailyCumulativeR <= -_config.MaxDailyLossR)
            ArmKillSwitch($"Daily R-loss limit hit ({_dailyCumulativeR:F2}R)");
    }
}
