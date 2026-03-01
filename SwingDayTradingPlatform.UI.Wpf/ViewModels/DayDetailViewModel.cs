using System.Collections.ObjectModel;
using SwingDayTradingPlatform.Backtesting;
using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.Strategy;
using SwingDayTradingPlatform.UI.Wpf.Infrastructure;

namespace SwingDayTradingPlatform.UI.Wpf.ViewModels;

public sealed class DayDetailViewModel : ObservableObject
{
    private IReadOnlyList<MarketBar>? _dayBars;
    private IReadOnlyList<decimal>? _ema20Values;
    private IReadOnlyList<decimal>? _vwapValues;
    private IReadOnlyList<TradeMarker>? _tradeMarkers;
    private DateOnly _selectedDate;
    private string _selectedDateDisplay = "Select a day";

    public IReadOnlyList<MarketBar>? DayBars
    {
        get => _dayBars;
        set => SetProperty(ref _dayBars, value);
    }

    public IReadOnlyList<decimal>? Ema20Values
    {
        get => _ema20Values;
        set => SetProperty(ref _ema20Values, value);
    }

    public IReadOnlyList<decimal>? VwapValues
    {
        get => _vwapValues;
        set => SetProperty(ref _vwapValues, value);
    }

    public IReadOnlyList<TradeMarker>? TradeMarkers
    {
        get => _tradeMarkers;
        set => SetProperty(ref _tradeMarkers, value);
    }

    public DateOnly SelectedDate
    {
        get => _selectedDate;
        set => SetProperty(ref _selectedDate, value);
    }

    public string SelectedDateDisplay
    {
        get => _selectedDateDisplay;
        set => SetProperty(ref _selectedDateDisplay, value);
    }

    public ObservableCollection<BacktestTrade> DayTrades { get; } = [];

    public void LoadDay(DateOnly date, List<MarketBar> allBars, List<BacktestTrade> trades, string timezone)
    {
        SelectedDate = date;
        var tz = ResolveTimeZone(timezone);

        // Filter bars for the selected date
        var dayBars = allBars.Where(b =>
        {
            var localTime = TimeZoneInfo.ConvertTime(b.OpenTimeUtc, tz);
            return DateOnly.FromDateTime(localTime.DateTime) == date;
        }).ToList();

        DayBars = dayBars;

        // Compute EMA20 — seed from prior bars for accuracy
        if (dayBars.Count > 0)
        {
            // Find index of first day bar in allBars
            var firstDayBarTime = dayBars[0].OpenTimeUtc;
            var startIdx = allBars.FindIndex(b => b.OpenTimeUtc == firstDayBarTime);
            // Include up to 100 prior bars for EMA seeding
            var seedStart = Math.Max(0, startIdx - 100);
            var seedBars = allBars.GetRange(seedStart, startIdx - seedStart);
            var allCloses = seedBars.Select(b => b.Close).Concat(dayBars.Select(b => b.Close)).ToList();
            var fullEma = Indicators.EmaSeries(allCloses, 20);
            // Return only the EMA values for the day bars (skip seed portion)
            Ema20Values = fullEma.GetRange(seedBars.Count, dayBars.Count);
            VwapValues = Indicators.SessionVwap(dayBars, tz);
        }
        else
        {
            Ema20Values = null;
            VwapValues = null;
        }

        // Filter trades for this day — include trades whose entry OR exit falls on the date
        var dayTrades = trades.Where(t =>
        {
            var localEntry = TimeZoneInfo.ConvertTime(t.EntryTime, tz);
            var localExit = TimeZoneInfo.ConvertTime(t.ExitTime, tz);
            return DateOnly.FromDateTime(localEntry.DateTime) == date
                || DateOnly.FromDateTime(localExit.DateTime) == date;
        }).ToList();

        // Convert times to trading timezone for grid display (HH:mm format)
        DayTrades.Clear();
        foreach (var t in dayTrades)
            DayTrades.Add(t with
            {
                EntryTime = TimeZoneInfo.ConvertTime(t.EntryTime, tz),
                ExitTime = TimeZoneInfo.ConvertTime(t.ExitTime, tz)
            });

        // Compute selected date display string
        var dt = date.ToDateTime(TimeOnly.MinValue);
        if (dayTrades.Count > 0)
        {
            var totalPnl = dayTrades.Sum(t => t.PnLDollars);
            var wins = dayTrades.Count(t => t.PnLDollars > 0);
            var losses = dayTrades.Count(t => t.PnLDollars <= 0);
            var sign = totalPnl >= 0 ? "+" : "";
            SelectedDateDisplay = $"{dt:ddd, MMM dd yyyy}   {sign}${totalPnl:N0}   {wins}W {losses}L   ({dayTrades.Count} trades)";
        }
        else
        {
            SelectedDateDisplay = $"{dt:ddd, MMM dd yyyy}   No trades";
        }

        // Build trade markers — only add markers whose time falls within this day's bars
        var markers = new List<TradeMarker>();
        var dayStart = dayBars.Count > 0 ? dayBars[0].OpenTimeUtc : DateTimeOffset.MaxValue;
        var dayEnd = dayBars.Count > 0 ? dayBars[^1].CloseTimeUtc : DateTimeOffset.MinValue;

        foreach (var trade in dayTrades)
        {
            var isLong = trade.Direction == "Long";

            // Entry marker — only if within this day's bar range
            if (trade.EntryTime >= dayStart && trade.EntryTime <= dayEnd)
            {
                markers.Add(new TradeMarker(
                    trade.EntryTime,
                    trade.EntryPrice,
                    true,
                    isLong,
                    $"Entry: {trade.Direction} @ {trade.EntryPrice:F2}\n{trade.EntryReason}"));
            }

            // Exit marker — only if within this day's bar range
            if (trade.ExitTime >= dayStart && trade.ExitTime <= dayEnd)
            {
                markers.Add(new TradeMarker(
                    trade.ExitTime,
                    trade.ExitPrice,
                    false,
                    isLong,
                    $"Exit: {trade.ExitReason} @ {trade.ExitPrice:F2}\nP&L: {trade.PnLPoints:F2} pts (${trade.PnLDollars:N2})\nMAE: {trade.MAE:F2} MFE: {trade.MFE:F2}"));
            }
        }
        TradeMarkers = markers;
    }

    private static TimeZoneInfo ResolveTimeZone(string configured)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(configured); }
        catch { return TimeZoneInfo.Local; }
    }
}
