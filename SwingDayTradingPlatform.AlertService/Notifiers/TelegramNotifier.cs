using System.Net.Http.Json;

namespace SwingDayTradingPlatform.AlertService.Notifiers;

/// <summary>
/// Sends alert messages to a Telegram chat via the Bot API.
/// Retries up to 3 times with exponential backoff on failure.
/// Gracefully no-ops if token or chatId are not configured.
/// </summary>
public sealed class TelegramNotifier : INotifier, IDisposable
{
    private const int MaxRetries = 3;
    private readonly string _token;
    private readonly string _chatId;
    private readonly HttpClient _http;
    private readonly bool _enabled;

    public TelegramNotifier(string? token, string? chatId)
    {
        _token = token ?? string.Empty;
        _chatId = chatId ?? string.Empty;
        _enabled = !string.IsNullOrWhiteSpace(_token) && !string.IsNullOrWhiteSpace(_chatId);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public bool IsEnabled => _enabled;

    public async Task NotifyAsync(Alert alert)
    {
        if (!_enabled)
            return;

        var text = FormatMessage(alert);
        var url = $"https://api.telegram.org/bot{_token}/sendMessage";

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var payload = new { chat_id = _chatId, text, parse_mode = "HTML" };
                var response = await _http.PostAsJsonAsync(url, payload);

                if (response.IsSuccessStatusCode)
                    return;

                var body = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[TELEGRAM] Attempt {attempt}/{MaxRetries} failed: HTTP {(int)response.StatusCode} - {body}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TELEGRAM] Attempt {attempt}/{MaxRetries} error: {ex.Message}");
            }

            if (attempt < MaxRetries)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
        }

        Console.WriteLine("[TELEGRAM] All retry attempts exhausted. Alert was NOT delivered to Telegram.");
    }

    public void Dispose() => _http.Dispose();

    private static string FormatMessage(Alert a)
    {
        var icon = a.Type == ExtremaType.MAX ? "🔴" : "🟢";
        return $"{icon} <b>{a.Type}</b> detected\n" +
               $"Pivot: {a.PivotTime:yyyy-MM-dd HH:mm}\n" +
               $"Event: {a.EventTime:yyyy-MM-dd HH:mm}\n" +
               $"Close={a.Close}  High={a.High}  Low={a.Low}\n" +
               $"EMA{a.EmaPeriod}={a.EmaValue} ({a.EmaDir})\n" +
               $"Symbol: {a.Symbol}";
    }
}
