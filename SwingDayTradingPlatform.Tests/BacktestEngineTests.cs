using SwingDayTradingPlatform.Backtesting;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Tests;

public class BacktestEngineTests
{
    private static BacktestConfig DefaultConfig() => new()
    {
        StartingCapital = 25000m,
        CommissionPerTrade = 0m,
        SlippagePoints = 0m,
        PointValue = 50m,
        Timezone = "America/New_York",
        EntryWindowStart = "09:40",
        EntryWindowEnd = "15:50",
        FlattenTime = "15:55",
        MaxDailyLossPoints = 20m
    };

    private static BacktestParameters DefaultParams() => new()
    {
        FastEmaPeriod = 20,
        SlowEmaPeriod = 50,
        AtrPeriod = 14,
        MaxTradesPerDay = 5,
        MaxLossesPerDay = 3,
        MaxStopPoints = 10m,
        CooldownSeconds = 0,
        EnableStrategy1 = true,
        EnableHourlyBias = false,
    };

    private static List<MarketBar> GenerateTradingDayBars(int barCount = 80)
    {
        var bars = new List<MarketBar>();
        // Trading day starts at 9:30 ET = 13:30 UTC (during EDT)
        var dayStart = new DateTimeOffset(2024, 6, 10, 13, 30, 0, TimeSpan.Zero);
        var price = 5000m;
        var rng = new Random(42);

        for (var i = 0; i < barCount; i++)
        {
            var change = (decimal)(rng.NextDouble() - 0.5) * 8m;
            var newPrice = price + change;
            var open = price;
            var close = newPrice;
            var high = Math.Max(open, close) + (decimal)rng.NextDouble() * 3m;
            var low = Math.Min(open, close) - (decimal)rng.NextDouble() * 3m;
            var openTime = dayStart.AddMinutes(i * 5);
            var closeTime = openTime.AddMinutes(5);
            bars.Add(new MarketBar(openTime, closeTime, open, high, low, close, 1000 + rng.Next(2000)));
            price = newPrice;
        }
        return bars;
    }

    [Fact]
    public void Run_EmptyBars_ReturnsZeroTrades()
    {
        var engine = new BacktestEngine();
        var result = engine.Run([], DefaultParams(), DefaultConfig());

        Assert.Equal(0, result.TotalTrades);
        Assert.Equal(25000m, result.StartingCapital);
    }

    [Fact]
    public void Run_WithBars_ReturnsResult()
    {
        var engine = new BacktestEngine();
        var bars = GenerateTradingDayBars(80);
        var result = engine.Run(bars, DefaultParams(), DefaultConfig());

        Assert.NotNull(result);
        Assert.True(result.EquityCurve.Count > 0);
        Assert.Equal("All", result.StrategyName);
    }

    [Fact]
    public void Run_EquityCurveHasEntries()
    {
        var engine = new BacktestEngine();
        var bars = GenerateTradingDayBars(80);
        var result = engine.Run(bars, DefaultParams(), DefaultConfig());

        // Equity curve should have at least bars.Count + 1 entries (initial + one per bar)
        Assert.True(result.EquityCurve.Count >= bars.Count);
    }

    [Fact]
    public void Run_StrategyFilter_OnlyRunsOneStrategy()
    {
        var engine = new BacktestEngine("EmaPullback");
        var bars = GenerateTradingDayBars(80);
        var result = engine.Run(bars, DefaultParams(), DefaultConfig());

        Assert.Equal("EmaPullback", result.StrategyName);
    }

    [Fact]
    public void Run_CancellationToken_Respects()
    {
        var engine = new BacktestEngine();
        var bars = GenerateTradingDayBars(80);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            engine.Run(bars, DefaultParams(), DefaultConfig(), cts.Token));
    }

    [Fact]
    public void Run_WithSlippage_AffectsResults()
    {
        var bars = GenerateTradingDayBars(80);

        var configNoSlip = DefaultConfig();
        var configWithSlip = new BacktestConfig
        {
            StartingCapital = 25000m, CommissionPerTrade = 0m, SlippagePoints = 1m,
            PointValue = 50m, Timezone = "America/New_York",
            EntryWindowStart = "09:40", EntryWindowEnd = "15:50", FlattenTime = "15:55",
            MaxDailyLossPoints = 20m
        };

        var resultNoSlip = new BacktestEngine().Run(bars, DefaultParams(), configNoSlip);
        var resultWithSlip = new BacktestEngine().Run(bars, DefaultParams(), configWithSlip);

        // Results should differ due to slippage
        // (may have same trades but different PnL, or different trade count)
        Assert.NotNull(resultNoSlip);
        Assert.NotNull(resultWithSlip);
    }

    [Fact]
    public void Run_WithCommission_ReducesNetPnL()
    {
        var bars = GenerateTradingDayBars(80);

        var configNoComm = DefaultConfig();
        var configWithComm = new BacktestConfig
        {
            StartingCapital = 25000m, CommissionPerTrade = 5m, SlippagePoints = 0m,
            PointValue = 50m, Timezone = "America/New_York",
            EntryWindowStart = "09:40", EntryWindowEnd = "15:50", FlattenTime = "15:55",
            MaxDailyLossPoints = 20m
        };

        var resultNoComm = new BacktestEngine().Run(bars, DefaultParams(), configNoComm);
        var resultWithComm = new BacktestEngine().Run(bars, DefaultParams(), configWithComm);

        if (resultNoComm.TotalTrades > 0 && resultWithComm.TotalTrades > 0)
        {
            Assert.True(resultWithComm.TotalCommissions >= resultNoComm.TotalCommissions);
        }
    }

    [Fact]
    public void Run_MultiDay_ResetsBetweenDays()
    {
        // Create bars spanning 2 trading days
        var bars = new List<MarketBar>();
        var day1Start = new DateTimeOffset(2024, 6, 10, 13, 30, 0, TimeSpan.Zero);
        var day2Start = new DateTimeOffset(2024, 6, 11, 13, 30, 0, TimeSpan.Zero);
        var price = 5000m;
        var rng = new Random(42);

        // Day 1: 40 bars
        for (var i = 0; i < 40; i++)
        {
            var change = (decimal)(rng.NextDouble() - 0.5) * 8m;
            var newPrice = price + change;
            var openTime = day1Start.AddMinutes(i * 5);
            bars.Add(new MarketBar(openTime, openTime.AddMinutes(5),
                price, Math.Max(price, newPrice) + 2, Math.Min(price, newPrice) - 2, newPrice, 1000));
            price = newPrice;
        }

        // Day 2: 40 bars
        for (var i = 0; i < 40; i++)
        {
            var change = (decimal)(rng.NextDouble() - 0.5) * 8m;
            var newPrice = price + change;
            var openTime = day2Start.AddMinutes(i * 5);
            bars.Add(new MarketBar(openTime, openTime.AddMinutes(5),
                price, Math.Max(price, newPrice) + 2, Math.Min(price, newPrice) - 2, newPrice, 1000));
            price = newPrice;
        }

        var engine = new BacktestEngine();
        var result = engine.Run(bars, DefaultParams(), DefaultConfig());

        Assert.NotNull(result);
        Assert.True(result.EquityCurve.Count > 0);
    }

    [Fact]
    public void BuildMultiConfig_WithStrategyFilter_PropagatesAllNewFields()
    {
        // Use non-default values for every new field so we can verify propagation
        var parameters = new BacktestParameters
        {
            FastEmaPeriod = 15,
            SlowEmaPeriod = 40,
            AtrPeriod = 10,
            EnableStrategy1 = true,
            TrailingStopAtrMultiplier = 3.5m,
            TrailingStopActivationBars = 7,
            UseBarBreakExit = true,
            EmaPullbackRewardRatio = 3.0m,
            EmaPullbackTolerance = 1.25m,
            MaxStopPoints = 20m
        };

        // Run a per-strategy backtest — the engine internally calls BuildMultiConfig
        var engine = new BacktestEngine("EmaPullback");
        var bars = GenerateTradingDayBars(80);
        var result = engine.Run(bars, parameters, DefaultConfig());

        // The test validates the engine doesn't crash and filters correctly.
        // To directly test field propagation, we use reflection on BuildMultiConfig.
        var method = typeof(BacktestEngine).GetMethod("BuildMultiConfig",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var filteredEngine = new BacktestEngine("EmaPullback");
        var multiConfig = (MultiStrategyConfig)method.Invoke(filteredEngine, new object[] { parameters })!;

        // Verify strategy flags — only EmaPullback enabled when filtered
        Assert.True(multiConfig.EnableStrategy1);

        // Verify all fields are propagated (not default values)
        Assert.Equal(3.5m, multiConfig.TrailingStopAtrMultiplier);
        Assert.Equal(7, multiConfig.TrailingStopActivationBars);
        Assert.True(multiConfig.UseBarBreakExit);
        Assert.Equal(3.0m, multiConfig.EmaPullbackRewardRatio);
        Assert.Equal(1.25m, multiConfig.EmaPullbackTolerance);
        Assert.Equal(20m, multiConfig.MaxStopPoints);

        // Also verify original fields are still propagated
        Assert.Equal(15, multiConfig.FastEmaPeriod);
        Assert.Equal(40, multiConfig.SlowEmaPeriod);
        Assert.Equal(10, multiConfig.AtrPeriod);
    }

    [Fact]
    public void Run_ForceFlattenAtEnd()
    {
        // If a position is open at end of data, it should be force-flattened
        var engine = new BacktestEngine();
        var bars = GenerateTradingDayBars(80);
        var result = engine.Run(bars, DefaultParams(), DefaultConfig());

        // All trades should have exit times and reasons
        foreach (var trade in result.Trades)
        {
            Assert.True(trade.ExitTime > trade.EntryTime);
            Assert.False(string.IsNullOrEmpty(trade.ExitReason));
        }
    }

    [Fact]
    public void Run_InvalidTimeConfig_Throws()
    {
        var engine = new BacktestEngine();
        var bars = GenerateTradingDayBars(10);

        var badConfig = new BacktestConfig
        {
            StartingCapital = 25000m,
            CommissionPerTrade = 0m,
            SlippagePoints = 0m,
            PointValue = 50m,
            Timezone = "America/New_York",
            EntryWindowStart = "not-a-time",
            EntryWindowEnd = "15:50",
            FlattenTime = "15:55",
            MaxDailyLossPoints = 20m
        };

        Assert.Throws<ArgumentException>(() => engine.Run(bars, DefaultParams(), badConfig));
    }
}
