using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.UI.Wpf.ViewModels;

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(LogEntry entry)
    {
        TimestampLocal = entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
        Category = entry.Category.ToString();
        Message = entry.Message;
    }

    public string TimestampLocal { get; }
    public string Category { get; }
    public string Message { get; }
}
