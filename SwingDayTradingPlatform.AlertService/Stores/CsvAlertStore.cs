using System.Globalization;

namespace SwingDayTradingPlatform.AlertService.Stores;

/// <summary>
/// Appends alerts to a CSV file. Creates the file with a header if it does not exist.
/// Thread-safe via locking.
/// </summary>
public sealed class CsvAlertStore
{
    private const string Header = "eventTime,pivotTime,type,symbol,close,high,low,emaPeriod,emaValue,emaDirection,mode";
    private readonly string _filePath;
    private readonly object _writeLock = new();
    private bool _headerWritten;

    public CsvAlertStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public Task WriteAsync(Alert alert)
    {
        lock (_writeLock)
        {
            if (!_headerWritten && !File.Exists(_filePath))
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(_filePath, Header + Environment.NewLine);
                _headerWritten = true;
            }
            else if (!_headerWritten)
            {
                _headerWritten = true;
            }

            var line = string.Join(",",
                alert.EventTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                alert.PivotTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                alert.Type.ToString(),
                alert.Symbol,
                alert.Close.ToString(CultureInfo.InvariantCulture),
                alert.High.ToString(CultureInfo.InvariantCulture),
                alert.Low.ToString(CultureInfo.InvariantCulture),
                alert.EmaPeriod.ToString(CultureInfo.InvariantCulture),
                alert.EmaValue.ToString(CultureInfo.InvariantCulture),
                alert.EmaDir.ToString(),
                alert.Mode);

            File.AppendAllText(_filePath, line + Environment.NewLine);
        }

        return Task.CompletedTask;
    }
}
