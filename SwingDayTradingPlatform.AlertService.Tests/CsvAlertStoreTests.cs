using SwingDayTradingPlatform.AlertService;
using SwingDayTradingPlatform.AlertService.Stores;

namespace SwingDayTradingPlatform.AlertService.Tests;

public class CsvAlertStoreTests : IDisposable
{
    private readonly string _tempDir;

    public CsvAlertStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"alertservice_store_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
    }

    private static Alert MakeAlert(ExtremaType type = ExtremaType.MAX, decimal close = 4805.50m)
    {
        return new Alert(
            EventTime: new DateTime(2024, 1, 2, 10, 0, 0),
            PivotTime: new DateTime(2024, 1, 2, 9, 55, 0),
            Type: type,
            Symbol: "MES",
            Close: close,
            High: 4806.50m,
            Low: 4803.50m,
            EmaPeriod: 20,
            EmaValue: 4801.91m,
            EmaDir: EmaDirection.UP,
            Mode: "close");
    }

    [Fact]
    public async Task WritesHeaderAndAlert()
    {
        var path = Path.Combine(_tempDir, "alerts.csv");
        var store = new CsvAlertStore(path);

        await store.WriteAsync(MakeAlert());

        Assert.True(File.Exists(path));
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Equal("eventTime,pivotTime,type,symbol,close,high,low,emaPeriod,emaValue,emaDirection,mode", lines[0]);
        Assert.Contains("MAX", lines[1]);
        Assert.Contains("MES", lines[1]);
        Assert.Contains("4805.50", lines[1]);
    }

    [Fact]
    public async Task AppendsMultipleAlerts()
    {
        var path = Path.Combine(_tempDir, "alerts2.csv");
        var store = new CsvAlertStore(path);

        await store.WriteAsync(MakeAlert(ExtremaType.MAX));
        await store.WriteAsync(MakeAlert(ExtremaType.MIN, 4796.50m));

        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length); // header + 2 data lines
        Assert.Contains("MAX", lines[1]);
        Assert.Contains("MIN", lines[2]);
    }

    [Fact]
    public async Task AppendsToExistingFile()
    {
        var path = Path.Combine(_tempDir, "alerts3.csv");
        // Pre-create file with header
        File.WriteAllText(path, "eventTime,pivotTime,type,symbol,close,high,low,emaPeriod,emaValue,emaDirection,mode\n");

        var store = new CsvAlertStore(path);
        await store.WriteAsync(MakeAlert());

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length); // existing header + new line
    }

    [Fact]
    public async Task CreatesDirectoryIfNotExists()
    {
        var path = Path.Combine(_tempDir, "subdir", "deep", "alerts.csv");
        var store = new CsvAlertStore(path);

        await store.WriteAsync(MakeAlert());

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task CorrectCsvFormat()
    {
        var path = Path.Combine(_tempDir, "format_test.csv");
        var store = new CsvAlertStore(path);

        var alert = new Alert(
            EventTime: new DateTime(2024, 3, 15, 14, 30, 0),
            PivotTime: new DateTime(2024, 3, 15, 14, 25, 0),
            Type: ExtremaType.MIN,
            Symbol: "ES",
            Close: 5200.25m,
            High: 5201.00m,
            Low: 5199.50m,
            EmaPeriod: 20,
            EmaValue: 5210.75m,
            EmaDir: EmaDirection.DOWN,
            Mode: "close");

        await store.WriteAsync(alert);

        var lines = File.ReadAllLines(path);
        var fields = lines[1].Split(',');

        Assert.Equal("2024-03-15 14:30:00", fields[0]);
        Assert.Equal("2024-03-15 14:25:00", fields[1]);
        Assert.Equal("MIN", fields[2]);
        Assert.Equal("ES", fields[3]);
        Assert.Equal("5200.25", fields[4]);
        Assert.Equal("5201.00", fields[5]);
        Assert.Equal("5199.50", fields[6]);
        Assert.Equal("20", fields[7]);
        Assert.Equal("5210.75", fields[8]);
        Assert.Equal("DOWN", fields[9]);
        Assert.Equal("close", fields[10]);
    }
}
