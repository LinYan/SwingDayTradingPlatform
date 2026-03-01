using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Tests;

public class AppConfigLoaderTests : IDisposable
{
    private readonly string _testDir;

    public AppConfigLoaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"SwingConfigTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var path = Path.Combine(_testDir, "config.json");
        var config = new AppConfig
        {
            Ibkr = new IbkrConfig { Host = "192.168.1.1", Port = 7496, ClientId = 200 },
            Trading = new TradingConfig { Symbol = "NQ", PointValue = 20m },
            Risk = new RiskConfig { MaxDailyLoss = 500m, MaxContracts = 3 }
        };

        AppConfigLoader.Save(path, config);
        var loaded = AppConfigLoader.Load(path);

        Assert.Equal("192.168.1.1", loaded.Ibkr.Host);
        Assert.Equal(7496, loaded.Ibkr.Port);
        Assert.Equal(200, loaded.Ibkr.ClientId);
        Assert.Equal("NQ", loaded.Trading.Symbol);
        Assert.Equal(20m, loaded.Trading.PointValue);
        Assert.Equal(500m, loaded.Risk.MaxDailyLoss);
        Assert.Equal(3, loaded.Risk.MaxContracts);
    }

    [Fact]
    public void Load_DefaultConfig_HasDefaults()
    {
        var path = Path.Combine(_testDir, "default.json");
        AppConfigLoader.Save(path, new AppConfig());
        var loaded = AppConfigLoader.Load(path);

        Assert.Equal("ES", loaded.Trading.Symbol);
        Assert.Equal(50m, loaded.Trading.PointValue);
        Assert.Equal("America/New_York", loaded.Trading.Timezone);
        Assert.Equal("127.0.0.1", loaded.Ibkr.Host);
    }

    [Fact]
    public void Load_NonExistentFile_Throws()
    {
        var path = Path.Combine(_testDir, "nonexistent.json");
        Assert.Throws<FileNotFoundException>(() => AppConfigLoader.Load(path));
    }

    [Fact]
    public void Save_CreatesDirectory()
    {
        var subDir = Path.Combine(_testDir, "subdir");
        var path = Path.Combine(subDir, "config.json");

        AppConfigLoader.Save(path, new AppConfig());

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Save_AtomicWrite_NoTmpFileLeft()
    {
        var path = Path.Combine(_testDir, "atomic.json");
        AppConfigLoader.Save(path, new AppConfig());

        Assert.False(File.Exists(path + ".tmp"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Save_CamelCaseNaming()
    {
        var path = Path.Combine(_testDir, "camel.json");
        AppConfigLoader.Save(path, new AppConfig());
        var json = File.ReadAllText(path);

        // Properties should be camelCase
        Assert.Contains("\"ibkr\"", json);
        Assert.Contains("\"trading\"", json);
        Assert.Contains("\"risk\"", json);
    }

    [Fact]
    public void SaveAndLoad_MultiStrategyConfig_Preserved()
    {
        var path = Path.Combine(_testDir, "multi.json");
        var config = new AppConfig
        {
            MultiStrategy = new MultiStrategyConfig
            {
                FastEmaPeriod = 10,
                SlowEmaPeriod = 30,
                EnableStrategy1 = false,
                EnableHourlyBias = true
            }
        };

        AppConfigLoader.Save(path, config);
        var loaded = AppConfigLoader.Load(path);

        Assert.Equal(10, loaded.MultiStrategy.FastEmaPeriod);
        Assert.Equal(30, loaded.MultiStrategy.SlowEmaPeriod);
        Assert.False(loaded.MultiStrategy.EnableStrategy1);
        Assert.True(loaded.MultiStrategy.EnableHourlyBias);
    }
}
