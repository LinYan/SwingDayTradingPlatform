using SwingDayTradingPlatform.AlertService;
using SwingDayTradingPlatform.AlertService.Notifiers;

namespace SwingDayTradingPlatform.AlertService.Tests;

public class NotifierTests
{
    private static Alert MakeAlert(ExtremaType type = ExtremaType.MAX)
    {
        return new Alert(
            EventTime: new DateTime(2024, 1, 2, 10, 0, 0),
            PivotTime: new DateTime(2024, 1, 2, 9, 55, 0),
            Type: type,
            Symbol: "MES",
            Close: 4805.50m,
            High: 4806.50m,
            Low: 4803.50m,
            EmaPeriod: 20,
            EmaValue: 4801.91m,
            EmaDir: EmaDirection.UP,
            Mode: "close");
    }

    [Fact]
    public async Task ConsoleNotifier_DoesNotThrow()
    {
        var notifier = new ConsoleNotifier();
        await notifier.NotifyAsync(MakeAlert(ExtremaType.MAX));
        await notifier.NotifyAsync(MakeAlert(ExtremaType.MIN));
        // If we get here, it passed
    }

    [Fact]
    public void TelegramNotifier_IsDisabled_WhenNoToken()
    {
        using var notifier = new TelegramNotifier(null, null);
        Assert.False(notifier.IsEnabled);
    }

    [Fact]
    public void TelegramNotifier_IsDisabled_WhenEmptyToken()
    {
        using var notifier = new TelegramNotifier("", "");
        Assert.False(notifier.IsEnabled);
    }

    [Fact]
    public void TelegramNotifier_IsDisabled_WhenWhitespaceToken()
    {
        using var notifier = new TelegramNotifier("  ", "  ");
        Assert.False(notifier.IsEnabled);
    }

    [Fact]
    public void TelegramNotifier_IsEnabled_WhenBothSet()
    {
        using var notifier = new TelegramNotifier("123:ABC", "456");
        Assert.True(notifier.IsEnabled);
    }

    [Fact]
    public async Task TelegramNotifier_NoOps_WhenDisabled()
    {
        using var notifier = new TelegramNotifier(null, null);

        // Should not throw, should return immediately
        await notifier.NotifyAsync(MakeAlert());
    }

    [Fact]
    public void TelegramNotifier_IsDisabled_WhenOnlyTokenSet()
    {
        using var notifier = new TelegramNotifier("123:ABC", "");
        Assert.False(notifier.IsEnabled);
    }

    [Fact]
    public void TelegramNotifier_IsDisabled_WhenOnlyChatIdSet()
    {
        using var notifier = new TelegramNotifier("", "456");
        Assert.False(notifier.IsEnabled);
    }
}
