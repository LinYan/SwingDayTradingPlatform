using SwingDayTradingPlatform.AlertService;
using SwingDayTradingPlatform.AlertService.Feeds;

namespace SwingDayTradingPlatform.AlertService.Tests;

public class CsvBarFeedTests : IDisposable
{
    private readonly string _tempDir;

    public CsvBarFeedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"alertservice_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
    }

    private string CreateTempCsv(string content)
    {
        var path = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task ParsesStandardFormat()
    {
        var csv = "timestamp,open,high,low,close\n" +
                  "2024-01-02 09:35:00,4800.25,4802.50,4799.00,4801.75\n" +
                  "2024-01-02 09:40:00,4801.75,4803.00,4800.50,4802.25\n";

        var path = CreateTempCsv(csv);
        var feed = new CsvBarFeed(path);
        var bars = new List<Bar>();
        feed.BarClosed += b => bars.Add(b);

        await feed.RunAsync(CancellationToken.None);

        Assert.Equal(2, bars.Count);
        Assert.Equal(4800.25m, bars[0].Open);
        Assert.Equal(4802.50m, bars[0].High);
        Assert.Equal(4799.00m, bars[0].Low);
        Assert.Equal(4801.75m, bars[0].Close);
        Assert.Equal(new DateTime(2024, 1, 2, 9, 35, 0), bars[0].Timestamp);
    }

    [Fact]
    public async Task SkipsHeaderLine()
    {
        var csv = "timestamp,open,high,low,close\n" +
                  "2024-01-02 09:35:00,100,102,99,101\n";

        var path = CreateTempCsv(csv);
        var feed = new CsvBarFeed(path);
        var bars = new List<Bar>();
        feed.BarClosed += b => bars.Add(b);

        await feed.RunAsync(CancellationToken.None);

        Assert.Single(bars);
        Assert.Equal(101m, bars[0].Close);
    }

    [Fact]
    public async Task HandlesEmptyLines()
    {
        var csv = "timestamp,open,high,low,close\n" +
                  "\n" +
                  "2024-01-02 09:35:00,100,102,99,101\n" +
                  "\n" +
                  "2024-01-02 09:40:00,101,103,100,102\n";

        var path = CreateTempCsv(csv);
        var feed = new CsvBarFeed(path);
        var bars = new List<Bar>();
        feed.BarClosed += b => bars.Add(b);

        await feed.RunAsync(CancellationToken.None);

        Assert.Equal(2, bars.Count);
    }

    [Fact]
    public async Task ThrowsOnMissingFile()
    {
        var feed = new CsvBarFeed("/nonexistent/path.csv");
        await Assert.ThrowsAsync<FileNotFoundException>(() => feed.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task HandlesCancellation()
    {
        // Create a large CSV
        var lines = new List<string> { "timestamp,open,high,low,close" };
        for (var i = 0; i < 1000; i++)
            lines.Add($"2024-01-02 09:{30 + (i / 12):00}:00,{100 + i},{101 + i},{99 + i},{100.5m + i}");

        var path = CreateTempCsv(string.Join("\n", lines));
        var feed = new CsvBarFeed(path);
        var bars = new List<Bar>();
        feed.BarClosed += b => bars.Add(b);

        using var cts = new CancellationTokenSource();
        // Cancel after a few bars
        var count = 0;
        feed.BarClosed += _ => { if (++count >= 5) cts.Cancel(); };

        await feed.RunAsync(cts.Token);

        // Should have stopped early
        Assert.True(bars.Count < 1000);
    }

    [Fact]
    public async Task ParsesSlashDateFormat()
    {
        var csv = "timestamp,open,high,low,close\n" +
                  "1/2/2024 9:35:00,100,102,99,101\n";

        var path = CreateTempCsv(csv);
        var feed = new CsvBarFeed(path);
        var bars = new List<Bar>();
        feed.BarClosed += b => bars.Add(b);

        await feed.RunAsync(CancellationToken.None);

        Assert.Single(bars);
        Assert.Equal(2024, bars[0].Timestamp.Year);
        Assert.Equal(1, bars[0].Timestamp.Month);
        Assert.Equal(2, bars[0].Timestamp.Day);
    }

    [Fact]
    public async Task SkipsLinesWithInsufficientColumns()
    {
        var csv = "timestamp,open,high,low,close\n" +
                  "2024-01-02 09:35:00,100,102\n" +       // too few columns
                  "2024-01-02 09:40:00,100,102,99,101\n";  // valid

        var path = CreateTempCsv(csv);
        var feed = new CsvBarFeed(path);
        var bars = new List<Bar>();
        feed.BarClosed += b => bars.Add(b);

        await feed.RunAsync(CancellationToken.None);

        Assert.Single(bars);
    }

    [Fact]
    public async Task DisposeIsIdempotent()
    {
        var feed = new CsvBarFeed("dummy.csv");
        await feed.DisposeAsync();
        await feed.DisposeAsync(); // Should not throw
    }
}
