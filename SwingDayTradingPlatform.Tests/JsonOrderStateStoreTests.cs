using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Storage;

namespace SwingDayTradingPlatform.Tests;

public class JsonOrderStateStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly StorageConfig _config;

    public JsonOrderStateStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"SwingTests_{Guid.NewGuid():N}");
        _config = new StorageConfig
        {
            BasePath = _testDir,
            OrdersFile = "orders.json",
            ExecutionsFile = "executions.json",
            StrategyStateFile = "strategy-state.json",
            LogsFile = "logs.json"
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsDefaultState()
    {
        using var store = new JsonOrderStateStore(_config);
        var state = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(state);
        Assert.Empty(state.Orders);
        Assert.Empty(state.Executions);
        Assert.False(state.KillSwitchArmed);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip()
    {
        using var store = new JsonOrderStateStore(_config);
        var state = new PersistedState
        {
            Orders = [new OrderTicket(1, "sig1", "ES", "", OrderSide.Buy, OrderIntent.Entry, 1, null, null, "Filled", DateTimeOffset.UtcNow)],
            KillSwitchArmed = true,
            DailyTradeCount = 3,
            DailyLossCount = 1
        };

        await store.SaveAsync(state, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.True(loaded.KillSwitchArmed);
        Assert.Equal(3, loaded.DailyTradeCount);
        Assert.Equal(1, loaded.DailyLossCount);
        Assert.Single(loaded.Orders);
    }

    [Fact]
    public async Task AppendLogAsync_CreatesFile()
    {
        using var store = new JsonOrderStateStore(_config);
        var entry = new LogEntry(DateTimeOffset.UtcNow, LogCategory.System, "Test log");

        await store.AppendLogAsync(entry, CancellationToken.None);

        var logs = await store.LoadLogsAsync(CancellationToken.None);
        Assert.Single(logs);
        Assert.Equal("Test log", logs[0].Message);
    }

    [Fact]
    public async Task AppendLogAsync_MultipleLogs_Accumulates()
    {
        using var store = new JsonOrderStateStore(_config);

        for (var i = 0; i < 5; i++)
        {
            var entry = new LogEntry(DateTimeOffset.UtcNow.AddSeconds(i), LogCategory.System, $"Log {i}");
            await store.AppendLogAsync(entry, CancellationToken.None);
        }

        var logs = await store.LoadLogsAsync(CancellationToken.None);
        Assert.Equal(5, logs.Count);
    }

    [Fact]
    public async Task LoadLogsAsync_NoFile_ReturnsEmpty()
    {
        using var store = new JsonOrderStateStore(_config);
        var logs = await store.LoadLogsAsync(CancellationToken.None);
        Assert.Empty(logs);
    }

    [Fact]
    public async Task SaveAsync_CreatesAtomicFiles()
    {
        using var store = new JsonOrderStateStore(_config);
        var state = new PersistedState { DailyTradeCount = 5 };

        await store.SaveAsync(state, CancellationToken.None);

        // tmp file should not exist after save
        var tmpPath = Path.Combine(_testDir, "strategy-state.json.tmp");
        Assert.False(File.Exists(tmpPath));

        // Main file should exist
        var mainPath = Path.Combine(_testDir, "strategy-state.json");
        Assert.True(File.Exists(mainPath));
    }

    [Fact]
    public async Task LoadAsync_CorruptedFile_ReturnsDefault()
    {
        Directory.CreateDirectory(_testDir);
        var statePath = Path.Combine(_testDir, "strategy-state.json");
        await File.WriteAllTextAsync(statePath, "not valid json {{{");

        using var store = new JsonOrderStateStore(_config);
        var state = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(state);
        Assert.Empty(state.Orders);
    }

    [Fact]
    public async Task SaveAsync_TriggeredSignalIds_Persisted()
    {
        using var store = new JsonOrderStateStore(_config);
        var state = new PersistedState
        {
            TriggeredSignalIds = ["SIG-001", "SIG-002"]
        };

        await store.SaveAsync(state, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Contains("SIG-001", loaded.TriggeredSignalIds);
        Assert.Contains("SIG-002", loaded.TriggeredSignalIds);
    }
}
