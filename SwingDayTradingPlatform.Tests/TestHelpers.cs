using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Tests;

public static class TestHelpers
{
    private static readonly DateTimeOffset BaseTime = new(2024, 6, 10, 14, 0, 0, TimeSpan.Zero);

    public static MarketBar MakeBar(
        decimal open, decimal high, decimal low, decimal close,
        decimal volume = 1000m, int minutesOffset = 0)
    {
        var openTime = BaseTime.AddMinutes(minutesOffset);
        return new MarketBar(openTime, openTime.AddMinutes(5), open, high, low, close, volume);
    }

    public static MarketBar MakeBar(
        decimal open, decimal high, decimal low, decimal close,
        DateTimeOffset closeTime, decimal volume = 1000m)
    {
        return new MarketBar(closeTime.AddMinutes(-5), closeTime, open, high, low, close, volume);
    }

    /// <summary>
    /// Generate a series of bars with a simple trend pattern.
    /// direction > 0 = uptrend, direction < 0 = downtrend, direction == 0 = sideways
    /// </summary>
    public static List<MarketBar> GenerateBars(int count, decimal startPrice = 5000m,
        decimal stepSize = 2m, int direction = 0, decimal volatility = 5m)
    {
        var bars = new List<MarketBar>();
        var price = startPrice;
        for (var i = 0; i < count; i++)
        {
            price += stepSize * direction;
            var open = price - volatility / 2;
            var close = price + volatility / 2;
            var high = Math.Max(open, close) + volatility * 0.3m;
            var low = Math.Min(open, close) - volatility * 0.3m;
            var time = BaseTime.AddMinutes(i * 5);
            bars.Add(new MarketBar(time, time.AddMinutes(5), open, high, low, close, 1000));
        }
        return bars;
    }

    /// <summary>
    /// Generate bars suitable for reaching warmup threshold with realistic OHLCV data.
    /// </summary>
    public static List<MarketBar> GenerateWarmupBars(int count = 60, decimal startPrice = 5000m)
    {
        var bars = new List<MarketBar>();
        var price = startPrice;
        var rng = new Random(42); // deterministic seed
        for (var i = 0; i < count; i++)
        {
            var change = (decimal)(rng.NextDouble() - 0.5) * 8m;
            var newPrice = price + change;
            var open = price;
            var close = newPrice;
            var high = Math.Max(open, close) + (decimal)rng.NextDouble() * 3m;
            var low = Math.Min(open, close) - (decimal)rng.NextDouble() * 3m;
            var time = BaseTime.AddMinutes(i * 5);
            bars.Add(new MarketBar(time, time.AddMinutes(5), open, high, low, close, 500 + rng.Next(1500)));
            price = newPrice;
        }
        return bars;
    }

    public static TradingConfig DefaultTradingConfig() => new()
    {
        Symbol = "ES",
        Exchange = "CME",
        Currency = "USD",
        Timezone = "America/New_York",
        EntryWindowStart = "09:40",
        EntryWindowEnd = "15:50",
        FlattenTime = "15:55",
        PointValue = 50m,
        BarResolution = "5m"
    };

    public static MultiStrategyConfig DefaultMultiConfig() => new()
    {
        FastEmaPeriod = 20,
        SlowEmaPeriod = 50,
        AtrPeriod = 14,
        EnableStrategy1 = true,
        EnableStrategy9 = true,
        EnableHourlyBias = false, // disable for simpler testing
        EnableTimeFilter = false, // disable for simpler testing
        EnableBreakEvenStop = false, // disable for simpler testing
        SwingLookback = 3,
        SRClusterAtrFactor = 0.5m,
        BigMoveAtrFactor = 3.0m,
        TickSize = 0.25m,
        TrailingStopAtrMultiplier = 2.0m,
        TrailingStopActivationBars = 3,
        UseBarBreakExit = false,
        EmaPullbackRewardRatio = 2.0m,
        EmaPullbackTolerance = 0.5m,
        MaxStopPoints = 10m
    };

    public static RiskConfig DefaultRiskConfig() => new()
    {
        MaxDailyLoss = 1000m,
        RiskPerTradePct = 0m,
        MaxContracts = 1,
        FixedContracts = 1,
        MaxTradesPerDay = 5,
        MaxLossesPerDay = 3,
        MaxStopPoints = 10m,
        CooldownSeconds = 60,
        UseUnrealizedPnLForDailyLimit = true
    };
}
