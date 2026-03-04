using SwingDayTradingPlatform.Shared;
using SwingDayTradingPlatform.UI.Wpf.Infrastructure;

namespace SwingDayTradingPlatform.UI.Wpf.ViewModels;

public sealed class AlertsDayChartViewModel : ObservableObject
{
    private IReadOnlyList<MarketBar>? _dayBars;
    private IReadOnlyList<decimal>? _emaValues;
    private IReadOnlyList<TradeMarker>? _tradeMarkers;
    private string _dateDisplay = "";
    private int _alertCount;

    public IReadOnlyList<MarketBar>? DayBars
    {
        get => _dayBars;
        set => SetProperty(ref _dayBars, value);
    }

    public IReadOnlyList<decimal>? EmaValues
    {
        get => _emaValues;
        set => SetProperty(ref _emaValues, value);
    }

    public IReadOnlyList<TradeMarker>? TradeMarkers
    {
        get => _tradeMarkers;
        set => SetProperty(ref _tradeMarkers, value);
    }

    public string DateDisplay
    {
        get => _dateDisplay;
        set => SetProperty(ref _dateDisplay, value);
    }

    public int AlertCount
    {
        get => _alertCount;
        set => SetProperty(ref _alertCount, value);
    }
}
