using System.Text.Json;

namespace SwingDayTradingPlatform.Shared;

public static class AppConfigLoader
{
    public static AppConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        var config = JsonSerializer.Deserialize<AppConfig>(stream, JsonOptions());
        return config ?? new AppConfig();
    }

    public static void Save(string path, AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(config, JsonOptions()));
        File.Move(tmpPath, path, overwrite: true);
    }

    private static JsonSerializerOptions JsonOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
}
