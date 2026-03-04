using Microsoft.Extensions.Configuration;
using SwingDayTradingPlatform.AlertService;
using SwingDayTradingPlatform.AlertService.Feeds;
using SwingDayTradingPlatform.AlertService.Notifiers;
using SwingDayTradingPlatform.AlertService.Stores;

// ─── Configuration ───────────────────────────────────────────────

var switchMappings = new Dictionary<string, string>
{
    ["--mode"] = "Mode",
    ["--file"] = "File",
    ["--symbol"] = "Symbol",
    ["--exchange"] = "Exchange",
    ["--tick"] = "TickSize",
    ["--bar"] = "BarSize",
    ["--lookback"] = "Lookback",
    ["--ema"] = "EmaPeriod",
    ["--ibHost"] = "IbHost",
    ["--ibPort"] = "IbPort",
    ["--clientId"] = "ClientId",
    ["--telegramToken"] = "TelegramToken",
    ["--telegramChatId"] = "TelegramChatId",
    ["--output"] = "Output"
};

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddCommandLine(args, switchMappings)
    .Build();

var cfg = new AlertServiceConfig();
configuration.Bind(cfg);

// ─── Banner ──────────────────────────────────────────────────────

Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║       Extrema Alert Service  (MES/ES)           ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
Console.WriteLine($"  Mode      : {cfg.Mode}");
Console.WriteLine($"  Symbol    : {cfg.Symbol}");
Console.WriteLine($"  Exchange  : {cfg.Exchange}");
Console.WriteLine($"  Lookback  : {cfg.Lookback}");
Console.WriteLine($"  EMA       : {cfg.EmaPeriod}");
Console.WriteLine($"  Output    : {cfg.Output}");

if (cfg.Mode.Equals("replay", StringComparison.OrdinalIgnoreCase))
    Console.WriteLine($"  File      : {cfg.File}");
else
    Console.WriteLine($"  IB Gateway: {cfg.IbHost}:{cfg.IbPort} (clientId={cfg.ClientId})");

var telegramEnabled = !string.IsNullOrWhiteSpace(cfg.TelegramToken) && !string.IsNullOrWhiteSpace(cfg.TelegramChatId);
Console.WriteLine($"  Telegram  : {(telegramEnabled ? "Enabled" : "Disabled (no token/chatId)")}");
Console.WriteLine();

// ─── Create components ───────────────────────────────────────────

IBarFeed feed = cfg.Mode.Equals("realtime", StringComparison.OrdinalIgnoreCase)
    ? new IbBarFeed(cfg.IbHost, cfg.IbPort, cfg.ClientId, cfg.Symbol, cfg.Exchange)
    : new CsvBarFeed(cfg.File);

var detector = new ExtremaDetector(cfg.Lookback, cfg.EmaPeriod, cfg.Symbol);
var store = new CsvAlertStore(cfg.Output);
var consoleNotifier = new ConsoleNotifier();
using var telegramNotifier = new TelegramNotifier(cfg.TelegramToken, cfg.TelegramChatId);

var notifiers = new List<INotifier> { consoleNotifier };
if (telegramNotifier.IsEnabled)
    notifiers.Add(telegramNotifier);

// ─── Wire events ─────────────────────────────────────────────────

var barCount = 0;
Bar? currentBar = null;

feed.BarClosed += bar =>
{
    barCount++;
    currentBar = bar;

    // Print bar status every bar
    var barEndTime = bar.Timestamp;
    var barStartTime = barEndTime.AddMinutes(-5);
    Console.WriteLine(
        $"  Bar #{barCount,4} | {barStartTime:HH:mm}-{barEndTime:HH:mm} | " +
        $"O={bar.Open,-10} H={bar.High,-10} L={bar.Low,-10} C={bar.Close,-10}");

    detector.OnBar(bar);
};

detector.AlertDetected += alert =>
{
    // Fire-and-forget for async notifiers; errors are logged inside each notifier
    _ = HandleAlertAsync(alert);
};

async Task HandleAlertAsync(Alert alert)
{
    foreach (var notifier in notifiers)
    {
        try
        {
            await notifier.NotifyAsync(alert);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Notifier error: {ex.Message}");
        }
    }

    try
    {
        await store.WriteAsync(alert);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARN] Alert store error: {ex.Message}");
    }
}

// ─── Run ─────────────────────────────────────────────────────────

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n[INFO] Shutting down...");
    cts.Cancel();
};

try
{
    await feed.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // Normal Ctrl+C shutdown
}
catch (Exception ex)
{
    Console.WriteLine($"[ERROR] {ex.Message}");
    Environment.ExitCode = 1;
}
finally
{
    await feed.DisposeAsync();
}

Console.WriteLine($"\n[INFO] Done. Processed {barCount} bars total.");

// ─── Configuration class ─────────────────────────────────────────

public sealed class AlertServiceConfig
{
    public string Mode { get; set; } = "replay";
    public string File { get; set; } = "./data.csv";
    public string Symbol { get; set; } = "MES";
    public string Exchange { get; set; } = "CME";
    public decimal TickSize { get; set; } = 0.25m;
    public string BarSize { get; set; } = "5m";
    public int Lookback { get; set; } = 5;
    public int EmaPeriod { get; set; } = 20;
    public string IbHost { get; set; } = "127.0.0.1";
    public int IbPort { get; set; } = 4002;
    public int ClientId { get; set; } = 12;
    public string TelegramToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;
    public string Output { get; set; } = "./alerts.csv";
}
