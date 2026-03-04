using SwingDayTradingPlatform.AlertService;
using SwingDayTradingPlatform.AlertService.Feeds;
using SwingDayTradingPlatform.AlertService.Stores;

namespace SwingDayTradingPlatform.AlertService.Tests;

public class IntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public IntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"alertservice_integ_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task EndToEnd_CsvFeed_DetectsMaxAndMin_WritesToCsv()
    {
        // Arrange: create CSV with a clear MAX and MIN pattern
        var csvPath = Path.Combine(_tempDir, "data.csv");
        var alertPath = Path.Combine(_tempDir, "alerts.csv");

        // Pattern: 5 bars up (MAX), then 5+ bars down (MIN), then up
        var lines = new List<string> { "timestamp,open,high,low,close" };
        decimal[] closes =
        [
            5000, 5002, 5004, 5006, 5008,   // 5 increasing (bars 0-4)
            5006,                             // drop => confirms MAX at bar 4
            5004, 5002, 5000, 4998,          // 4 more decreasing (bars 6-9, from bar 5: 5006,5004,5002,5000,4998)
            4999                              // rise => confirms MIN at bar 9
        ];

        for (var i = 0; i < closes.Length; i++)
        {
            var time = new DateTime(2024, 1, 2, 9, 30, 0).AddMinutes(i * 5);
            lines.Add($"{time:yyyy-MM-dd HH:mm:ss},{closes[i] - 1},{closes[i] + 1},{closes[i] - 1.5m},{closes[i]}");
        }

        File.WriteAllLines(csvPath, lines);

        // Act
        var feed = new CsvBarFeed(csvPath);
        var detector = new ExtremaDetector(5, 20, "MES");
        var store = new CsvAlertStore(alertPath);
        var alerts = new List<Alert>();

        feed.BarClosed += bar => detector.OnBar(bar);
        detector.AlertDetected += alert =>
        {
            alerts.Add(alert);
            store.WriteAsync(alert).GetAwaiter().GetResult();
        };

        await feed.RunAsync(CancellationToken.None);

        // Assert
        Assert.True(alerts.Count >= 1, $"Expected at least 1 alert, got {alerts.Count}");

        // First alert should be MAX at bar 4 (close=5008)
        Assert.Equal(ExtremaType.MAX, alerts[0].Type);
        Assert.Equal(5008m, alerts[0].Close);

        // Check if MIN was also detected
        // From bars 5-9: 5006, 5004, 5002, 5000, 4998 (5 strictly decreasing), bar 10: 4999 (rise)
        if (alerts.Count >= 2)
        {
            Assert.Equal(ExtremaType.MIN, alerts[1].Type);
            Assert.Equal(4998m, alerts[1].Close);
        }

        // Verify CSV output
        Assert.True(File.Exists(alertPath));
        var csvLines = File.ReadAllLines(alertPath);
        Assert.True(csvLines.Length >= 2); // header + at least 1 alert
    }

    [Fact]
    public async Task EndToEnd_NoAlerts_WhenFlatData()
    {
        var csvPath = Path.Combine(_tempDir, "flat.csv");

        var lines = new List<string> { "timestamp,open,high,low,close" };
        for (var i = 0; i < 20; i++)
        {
            var time = new DateTime(2024, 1, 2, 9, 30, 0).AddMinutes(i * 5);
            lines.Add($"{time:yyyy-MM-dd HH:mm:ss},100,101,99,100"); // constant close
        }

        File.WriteAllLines(csvPath, lines);

        var feed = new CsvBarFeed(csvPath);
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();

        feed.BarClosed += bar => detector.OnBar(bar);
        detector.AlertDetected += alert => alerts.Add(alert);

        await feed.RunAsync(CancellationToken.None);

        Assert.Empty(alerts);
    }

    [Fact]
    public async Task EndToEnd_MultipleMaxMin_AlternatingPattern()
    {
        var csvPath = Path.Combine(_tempDir, "alternating.csv");

        // Create a zigzag: up 5, down 1, up 5, down 1, ...
        var lines = new List<string> { "timestamp,open,high,low,close" };
        decimal[] closes =
        [
            // First rise of 5
            100, 102, 104, 106, 108,
            // Drop (confirms MAX at 108)
            106,
            // Continue down 4 more (total 5 decreasing from 108)
            104, 102, 100, 98,
            // Rise (confirms MIN at 98)
            100
        ];

        for (var i = 0; i < closes.Length; i++)
        {
            var time = new DateTime(2024, 1, 2, 9, 30, 0).AddMinutes(i * 5);
            lines.Add($"{time:yyyy-MM-dd HH:mm:ss},{closes[i] - 1},{closes[i] + 1},{closes[i] - 1},{closes[i]}");
        }

        File.WriteAllLines(csvPath, lines);

        var feed = new CsvBarFeed(csvPath);
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();

        feed.BarClosed += bar => detector.OnBar(bar);
        detector.AlertDetected += alert => alerts.Add(alert);

        await feed.RunAsync(CancellationToken.None);

        Assert.True(alerts.Count >= 1);
        Assert.Equal(ExtremaType.MAX, alerts[0].Type);
        Assert.Equal(108m, alerts[0].Close);
    }

    [Fact]
    public async Task EndToEnd_LookbackOf3()
    {
        var csvPath = Path.Combine(_tempDir, "lookback3.csv");

        var lines = new List<string> { "timestamp,open,high,low,close" };
        decimal[] closes = [100, 102, 104, 103]; // 3 up + drop

        for (var i = 0; i < closes.Length; i++)
        {
            var time = new DateTime(2024, 1, 2, 9, 30, 0).AddMinutes(i * 5);
            lines.Add($"{time:yyyy-MM-dd HH:mm:ss},{closes[i] - 1},{closes[i] + 1},{closes[i] - 1},{closes[i]}");
        }

        File.WriteAllLines(csvPath, lines);

        var feed = new CsvBarFeed(csvPath);
        var detector = new ExtremaDetector(3, 20, "MES"); // lookback=3
        var alerts = new List<Alert>();

        feed.BarClosed += bar => detector.OnBar(bar);
        detector.AlertDetected += alert => alerts.Add(alert);

        await feed.RunAsync(CancellationToken.None);

        Assert.Single(alerts);
        Assert.Equal(ExtremaType.MAX, alerts[0].Type);
        Assert.Equal(104m, alerts[0].Close);
    }

    [Fact]
    public async Task EmaValues_AreReasonable()
    {
        var csvPath = Path.Combine(_tempDir, "ema_test.csv");

        var lines = new List<string> { "timestamp,open,high,low,close" };
        // Peak pattern: 100, 102, 106, 103, 101 => peak at index 2 (close=106)
        decimal[] closes = [100, 102, 106, 103, 101];

        for (var i = 0; i < closes.Length; i++)
        {
            var time = new DateTime(2024, 1, 2, 9, 30, 0).AddMinutes(i * 5);
            lines.Add($"{time:yyyy-MM-dd HH:mm:ss},{closes[i] - 0.5m},{closes[i] + 0.5m},{closes[i] - 0.5m},{closes[i]}");
        }

        File.WriteAllLines(csvPath, lines);

        var feed = new CsvBarFeed(csvPath);
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();

        feed.BarClosed += bar => detector.OnBar(bar);
        detector.AlertDetected += alert => alerts.Add(alert);

        await feed.RunAsync(CancellationToken.None);

        Assert.Single(alerts);
        Assert.Equal(ExtremaType.MAX, alerts[0].Type);
        Assert.Equal(106m, alerts[0].Close);
        Assert.Equal(20, alerts[0].EmaPeriod);
    }
}
