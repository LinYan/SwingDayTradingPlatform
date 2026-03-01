using System.Text.Json;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Storage;

public sealed class JsonOrderStateStore : IOrderStateStore, IDisposable
{
    private readonly StorageConfig _config;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public JsonOrderStateStore(StorageConfig config)
    {
        _config = config;
        Directory.CreateDirectory(_config.BasePath);
    }

    public async Task<PersistedState> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var statePath = Path.Combine(_config.BasePath, _config.StrategyStateFile);
            if (!File.Exists(statePath))
                return new PersistedState();

            await using var stream = File.OpenRead(statePath);
            return await JsonSerializer.DeserializeAsync<PersistedState>(stream, cancellationToken: cancellationToken) ?? new PersistedState();
        }
        catch (JsonException)
        {
            // Corrupted state file — start fresh
            return new PersistedState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PersistedState state, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveJsonAsync(Path.Combine(_config.BasePath, _config.StrategyStateFile), state, cancellationToken);
            await SaveJsonAsync(Path.Combine(_config.BasePath, _config.OrdersFile), state.Orders, cancellationToken);
            await SaveJsonAsync(Path.Combine(_config.BasePath, _config.ExecutionsFile), state.Executions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private int _logAppendCount = 100; // Start at threshold to trigger trim on first append after restart

    public async Task AppendLogAsync(LogEntry entry, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var logsPath = Path.Combine(_config.BasePath, _config.LogsFile);

            // Every 100 appends, do a full read-trim-write to keep file manageable
            if (_logAppendCount >= 100 || !File.Exists(logsPath))
            {
                List<LogEntry> logs;
                if (File.Exists(logsPath))
                {
                    try
                    {
                        await using var readStream = File.OpenRead(logsPath);
                        logs = await JsonSerializer.DeserializeAsync<List<LogEntry>>(readStream, cancellationToken: cancellationToken) ?? [];
                    }
                    catch (JsonException)
                    {
                        logs = [];
                    }
                }
                else
                {
                    logs = [];
                }

                logs.Add(entry);
                if (logs.Count > 1000)
                    logs = logs.TakeLast(1000).ToList();

                await SaveJsonAsync(logsPath, logs, cancellationToken);
                _logAppendCount = 0;
            }
            else
            {
                // Fast path: read existing JSON, insert entry before closing bracket
                var json = await File.ReadAllTextAsync(logsPath, cancellationToken);
                var lastBracket = json.LastIndexOf(']');
                if (lastBracket >= 0)
                {
                    var entryJson = JsonSerializer.Serialize(entry, _jsonOptions);
                    var hasEntries = json.AsSpan(0, lastBracket).Trim().Length > 1; // more than just '['
                    var separator = hasEntries ? ",\n" : "\n";
                    var newJson = string.Concat(json.AsSpan(0, lastBracket), separator, entryJson, "\n]");
                    await File.WriteAllTextAsync(logsPath, newJson, cancellationToken);
                }
                else
                {
                    // Malformed file, rewrite
                    await SaveJsonAsync(logsPath, new List<LogEntry> { entry }, cancellationToken);
                }
                _logAppendCount++;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<LogEntry>> LoadLogsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var logsPath = Path.Combine(_config.BasePath, _config.LogsFile);
            if (!File.Exists(logsPath))
                return [];

            await using var stream = File.OpenRead(logsPath);
            return await JsonSerializer.DeserializeAsync<List<LogEntry>>(stream, cancellationToken: cancellationToken) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var tmpPath = path + ".tmp";
        await using (var stream = File.Create(tmpPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
        }

        File.Move(tmpPath, path, overwrite: true);
    }

    public void Dispose() => _gate.Dispose();
}
