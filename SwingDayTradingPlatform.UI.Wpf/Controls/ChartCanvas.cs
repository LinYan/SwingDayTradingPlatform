using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.UI.Wpf.Controls;

public sealed class ChartCanvas : Canvas
{
    private const double DefaultBarWidth = 8;
    private const double BarGap = 2;
    private const double PriceAxisWidth = 70;
    private const double TimeAxisHeight = 34;
    private const double TopPadding = 10;
    private const double VolumePanelRatio = 0.18;

    private static readonly TimeZoneInfo EasternTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private double _barWidth = DefaultBarWidth;
    private double _scrollOffset; // bars from right edge
    private bool _isPanning;
    private Point _panStart;
    private double _panStartOffset;
    private Point _mousePosition;
    private bool _showCrosshair;
    private Popup? _tooltipPopup;

    public static readonly DependencyProperty BarsProperty =
        DependencyProperty.Register(nameof(Bars), typeof(IReadOnlyList<MarketBar>), typeof(ChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FastEmaValuesProperty =
        DependencyProperty.Register(nameof(FastEmaValues), typeof(IReadOnlyList<decimal>), typeof(ChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SlowEmaValuesProperty =
        DependencyProperty.Register(nameof(SlowEmaValues), typeof(IReadOnlyList<decimal>), typeof(ChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SignalsProperty =
        DependencyProperty.Register(nameof(Signals), typeof(IReadOnlyList<StrategySignal>), typeof(ChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VwapValuesProperty =
        DependencyProperty.Register(nameof(VwapValues), typeof(IReadOnlyList<decimal>), typeof(ChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TradeMarkersProperty =
        DependencyProperty.Register(nameof(TradeMarkers), typeof(IReadOnlyList<TradeMarker>), typeof(ChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<MarketBar>? Bars
    {
        get => (IReadOnlyList<MarketBar>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public IReadOnlyList<decimal>? FastEmaValues
    {
        get => (IReadOnlyList<decimal>?)GetValue(FastEmaValuesProperty);
        set => SetValue(FastEmaValuesProperty, value);
    }

    public IReadOnlyList<decimal>? SlowEmaValues
    {
        get => (IReadOnlyList<decimal>?)GetValue(SlowEmaValuesProperty);
        set => SetValue(SlowEmaValuesProperty, value);
    }

    public IReadOnlyList<StrategySignal>? Signals
    {
        get => (IReadOnlyList<StrategySignal>?)GetValue(SignalsProperty);
        set => SetValue(SignalsProperty, value);
    }

    public IReadOnlyList<decimal>? VwapValues
    {
        get => (IReadOnlyList<decimal>?)GetValue(VwapValuesProperty);
        set => SetValue(VwapValuesProperty, value);
    }

    public IReadOnlyList<TradeMarker>? TradeMarkers
    {
        get => (IReadOnlyList<TradeMarker>?)GetValue(TradeMarkersProperty);
        set => SetValue(TradeMarkersProperty, value);
    }

    private static readonly Brush BullBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0x94));
    private static readonly Brush BearBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x70, 0x55));
    private static readonly Brush BullVolumeBrush = new SolidColorBrush(Color.FromArgb(80, 0x00, 0xB8, 0x94));
    private static readonly Brush BearVolumeBrush = new SolidColorBrush(Color.FromArgb(80, 0xE1, 0x70, 0x55));
    private static readonly Pen BullPen = new(BullBrush, 1);
    private static readonly Pen BearPen = new(BearBrush, 1);
    private static readonly Pen BullWickPen = new(new SolidColorBrush(Color.FromRgb(0x00, 0x90, 0x76)), 1);
    private static readonly Pen BearWickPen = new(new SolidColorBrush(Color.FromRgb(0xC8, 0x5C, 0x44)), 1);
    private static readonly Pen SessionSeparatorPen;
    private static readonly Pen FastEmaPen = new(new SolidColorBrush(Color.FromRgb(0x09, 0x84, 0xE3)), 1.5);
    private static readonly Pen SlowEmaPen = new(new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)), 1.5);
    private static readonly Pen VwapPen = new(new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6)), 1.5);
    private static readonly Pen GridPen;
    private static readonly Pen CrosshairPen;
    private static readonly Pen VolumeSeparatorPen;
    private static readonly Pen CurrentPricePen;
    private static readonly Brush AxisBrush = Brushes.DimGray;
    private static readonly Brush ChartBg;
    private static readonly Brush ChartBgSolid = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
    private static readonly Brush PriceAxisBg = new SolidColorBrush(Color.FromArgb(40, 0x10, 0x10, 0x20));
    private static readonly Brush CrosshairLabelBg = new SolidColorBrush(Color.FromArgb(210, 0x1B, 0x30, 0x44));
    private static readonly Brush CurrentPriceLabelBg = new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0x94));
    private static readonly Brush LongMarkerBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0x94));
    private static readonly Brush ShortMarkerBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x70, 0x55));
    private static readonly Brush ExitMarkerBrushLong = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
    private static readonly Brush ExitMarkerBrushShort = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly Pen ExitMarkerPenLong = new(new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)), 2.0);
    private static readonly Pen ExitMarkerPenShort = new(new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), 2.0);
    private static readonly Pen ExitGlowPenLong;
    private static readonly Pen ExitGlowPenShort;
    private static readonly Brush EmptySubtitleBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x98));
    private static readonly Brush LegendBg = new SolidColorBrush(Color.FromArgb(150, 0x1E, 0x1E, 0x2E));
    private static readonly Brush BarHighlightBrush = new SolidColorBrush(Color.FromArgb(25, 0x38, 0xBD, 0xF8));
    private static readonly Brush EmaFillBullBrush = new SolidColorBrush(Color.FromArgb(20, 0x09, 0x84, 0xE3));
    private static readonly Brush EmaFillBearBrush = new SolidColorBrush(Color.FromArgb(15, 0xE6, 0x7E, 0x22));
    private static readonly Brush InfoPanelBg = new SolidColorBrush(Color.FromArgb(180, 0x16, 0x16, 0x2E));
    private static readonly Pen InfoPanelBorder;
    private static readonly Brush BullGradientTop;
    private static readonly Brush BearGradientTop;
    private static readonly Pen EmptyDecoPen;
    private static readonly Pen HighLowDashPen;
    private static readonly Brush HighLabelBrush = new SolidColorBrush(Color.FromArgb(220, 0x00, 0xB8, 0x94));
    private static readonly Brush LowLabelBrush = new SolidColorBrush(Color.FromArgb(220, 0xE1, 0x70, 0x55));
    private static readonly Brush ScrollIndicatorBg = new SolidColorBrush(Color.FromArgb(120, 0x16, 0x16, 0x2E));
    private static readonly Brush ScrollIndicatorFg = new SolidColorBrush(Color.FromArgb(80, 0x38, 0xBD, 0xF8));
    private static readonly Brush CrossoverBullBrush = new SolidColorBrush(Color.FromArgb(200, 0x00, 0xB8, 0x94));
    private static readonly Brush CrossoverBearBrush = new SolidColorBrush(Color.FromArgb(200, 0xE1, 0x70, 0x55));
    private static readonly Pen EntryGlowPen;
    private static readonly Typeface LabelTypeface = new("Consolas");

    static ChartCanvas()
    {
        BullBrush.Freeze();
        BearBrush.Freeze();
        BullVolumeBrush.Freeze();
        BearVolumeBrush.Freeze();
        BullPen.Freeze();
        BearPen.Freeze();
        BullWickPen.Freeze();
        BearWickPen.Freeze();
        SessionSeparatorPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 0x38, 0xBD, 0xF8)), 1) { DashStyle = DashStyles.Dot };
        SessionSeparatorPen.Freeze();
        FastEmaPen.Freeze();
        SlowEmaPen.Freeze();
        VwapPen.Freeze();
        ChartBg = new LinearGradientBrush(
            Color.FromRgb(0x1A, 0x1A, 0x2C), Color.FromRgb(0x22, 0x22, 0x36),
            new Point(0, 0), new Point(0, 1));
        ChartBg.Freeze();
        ChartBgSolid.Freeze();
        CrosshairLabelBg.Freeze();
        LongMarkerBrush.Freeze();
        ShortMarkerBrush.Freeze();
        ExitMarkerBrushLong.Freeze();
        ExitMarkerBrushShort.Freeze();
        ExitMarkerPenLong.Freeze();
        ExitMarkerPenShort.Freeze();
        ExitGlowPenLong = new Pen(new SolidColorBrush(Color.FromArgb(70, 0x38, 0xBD, 0xF8)), 4);
        ExitGlowPenLong.Freeze();
        ExitGlowPenShort = new Pen(new SolidColorBrush(Color.FromArgb(70, 0xFB, 0xBF, 0x24)), 4);
        ExitGlowPenShort.Freeze();
        EmptySubtitleBrush.Freeze();

        GridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1) { DashStyle = DashStyles.Dot };
        GridPen.Freeze();
        CrosshairPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1) { DashStyle = DashStyles.Dash };
        CrosshairPen.Freeze();
        VolumeSeparatorPen = new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1);
        VolumeSeparatorPen.Freeze();
        CurrentPricePen = new Pen(new SolidColorBrush(Color.FromArgb(140, 0x00, 0xB8, 0x94)), 1) { DashStyle = DashStyles.Dash };
        CurrentPricePen.Freeze();
        PriceAxisBg.Freeze();
        CurrentPriceLabelBg.Freeze();
        LegendBg.Freeze();
        BarHighlightBrush.Freeze();
        EmaFillBullBrush.Freeze();
        EmaFillBearBrush.Freeze();
        InfoPanelBg.Freeze();
        InfoPanelBorder = new Pen(new SolidColorBrush(Color.FromArgb(40, 0x38, 0xBD, 0xF8)), 1);
        InfoPanelBorder.Freeze();

        // Gradient brushes for candle bodies (vertical gradient: lighter top → darker bottom)
        BullGradientTop = new LinearGradientBrush(
            Color.FromRgb(0x20, 0xD0, 0xA8), Color.FromRgb(0x00, 0xA0, 0x7A),
            new Point(0, 0), new Point(0, 1));
        BullGradientTop.Freeze();
        BearGradientTop = new LinearGradientBrush(
            Color.FromRgb(0xF0, 0x88, 0x68), Color.FromRgb(0xC8, 0x50, 0x38),
            new Point(0, 0), new Point(0, 1));
        BearGradientTop.Freeze();

        EmptyDecoPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 0x38, 0xBD, 0xF8)), 1.5);
        EmptyDecoPen.Freeze();
        HighLowDashPen = new Pen(new SolidColorBrush(Color.FromArgb(50, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = DashStyles.Dot };
        HighLowDashPen.Freeze();
        HighLabelBrush.Freeze();
        LowLabelBrush.Freeze();
        ScrollIndicatorBg.Freeze();
        ScrollIndicatorFg.Freeze();
        CrossoverBullBrush.Freeze();
        CrossoverBearBrush.Freeze();
        EntryGlowPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 0x38, 0xBD, 0xF8)), 6);
        EntryGlowPen.Freeze();
    }

    public ChartCanvas()
    {
        ClipToBounds = true;
        Background = ChartBgSolid;
        MouseWheel += OnMouseWheel;
        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseLeave += OnMouseLeave;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.15 : 0.87;
        _barWidth = Math.Clamp(_barWidth * factor, 3, 30);
        InvalidateVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _mousePosition = e.GetPosition(this);
        _showCrosshair = true;

        if (_isPanning)
        {
            var dx = _mousePosition.X - _panStart.X;
            var barsShifted = dx / (_barWidth + BarGap);
            _scrollOffset = Math.Max(0, _panStartOffset + barsShifted);
        }

        HideTooltip();
        InvalidateVisual();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Check for trade marker click first
        if (TryShowTradeMarkerTooltip(e.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(this);
        _panStartOffset = _scrollOffset;
        CaptureMouse();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        ReleaseMouseCapture();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _showCrosshair = false;
        _isPanning = false;
        ReleaseMouseCapture();
        HideTooltip();
        InvalidateVisual();
    }

    private bool TryShowTradeMarkerTooltip(Point clickPos)
    {
        var markers = TradeMarkers;
        var bars = Bars;
        if (markers is null || markers.Count == 0 || bars is null || bars.Count == 0) return false;

        var w = ActualWidth;
        var h = ActualHeight;
        var chartW = w - PriceAxisWidth;
        var totalChartH = h - TimeAxisHeight - TopPadding;
        var volumeH = totalChartH * VolumePanelRatio;
        var chartH = totalChartH - volumeH;
        var (effectiveBarW, step) = GetEffectiveBarSize(chartW, bars.Count);
        var visibleCount = Math.Max(1, (int)(chartW / step));
        var scrollBars = (int)_scrollOffset;
        var endIndex = bars.Count - scrollBars;
        var startIndex = Math.Max(0, endIndex - visibleCount);
        if (endIndex <= 0) { endIndex = Math.Min(bars.Count, visibleCount); startIndex = 0; }
        if (endIndex > bars.Count) endIndex = bars.Count;

        var minPrice = decimal.MaxValue;
        var maxPrice = decimal.MinValue;
        for (var i = startIndex; i < endIndex; i++)
        {
            if (bars[i].Low < minPrice) minPrice = bars[i].Low;
            if (bars[i].High > maxPrice) maxPrice = bars[i].High;
        }
        ExpandRange(FastEmaValues, startIndex, endIndex, bars.Count, ref minPrice, ref maxPrice);
        ExpandRange(SlowEmaValues, startIndex, endIndex, bars.Count, ref minPrice, ref maxPrice);
        ExpandRange(VwapValues, startIndex, endIndex, bars.Count, ref minPrice, ref maxPrice);
        var range = maxPrice - minPrice;
        if (range == 0) range = 1;
        var padding = range * 0.10m;
        minPrice -= padding;
        maxPrice += padding;
        range = maxPrice - minPrice;

        double PriceToY(decimal price) => TopPadding + (double)((maxPrice - price) / range) * chartH;
        double IndexToX(int idx) => (idx - startIndex) * step + effectiveBarW / 2;

        foreach (var marker in markers)
        {
            for (var i = startIndex; i < endIndex; i++)
            {
                if (marker.Time >= bars[i].OpenTimeUtc && marker.Time <= bars[i].CloseTimeUtc)
                {
                    var x = IndexToX(i);
                    var y = PriceToY(marker.Price);
                    if (Math.Abs(clickPos.X - x) < 10 && Math.Abs(clickPos.Y - y) < 10)
                    {
                        ShowTooltip(marker.Tooltip, clickPos);
                        return true;
                    }
                    break;
                }
            }
        }
        return false;
    }

    private void ShowTooltip(string text, Point pos)
    {
        HideTooltip();
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(230, 0x1B, 0x30, 0x44)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8),
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.4, Color = Colors.Black },
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11
            }
        };
        _tooltipPopup = new Popup
        {
            Child = border,
            PlacementTarget = this,
            Placement = PlacementMode.Relative,
            HorizontalOffset = pos.X + 10,
            VerticalOffset = pos.Y - 20,
            IsOpen = true,
            StaysOpen = false,
            AllowsTransparency = true
        };
    }

    private void HideTooltip()
    {
        if (_tooltipPopup is not null)
        {
            _tooltipPopup.IsOpen = false;
            _tooltipPopup = null;
        }
    }

    private (double barWidth, double step) GetEffectiveBarSize(double chartW, int barCount)
    {
        var baseStep = _barWidth + BarGap;
        var visibleCount = Math.Max(1, (int)(chartW / baseStep));
        if (barCount > 0 && barCount <= visibleCount)
        {
            var effectiveBarWidth = Math.Clamp(chartW / barCount - BarGap, _barWidth, 30);
            return (effectiveBarWidth, effectiveBarWidth + BarGap);
        }
        return (_barWidth, baseStep);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 50 || h < 50) return;

        dc.DrawRectangle(ChartBg, null, new Rect(0, 0, w, h));

        var bars = Bars;
        if (bars is null || bars.Count == 0)
        {
            DrawEmptyMessage(dc, w, h);
            return;
        }

        var chartW = w - PriceAxisWidth;
        var totalChartH = h - TimeAxisHeight - TopPadding;
        var volumeH = totalChartH * VolumePanelRatio;
        var chartH = totalChartH - volumeH;
        var (effectiveBarW, step) = GetEffectiveBarSize(chartW, bars.Count);
        var visibleCount = (int)(chartW / step);
        if (visibleCount < 1) visibleCount = 1;

        var scrollBars = (int)_scrollOffset;
        var endIndex = bars.Count - scrollBars;
        var startIndex = Math.Max(0, endIndex - visibleCount);
        if (endIndex <= 0) { endIndex = Math.Min(bars.Count, visibleCount); startIndex = 0; }
        if (endIndex > bars.Count) endIndex = bars.Count;

        var minPrice = decimal.MaxValue;
        var maxPrice = decimal.MinValue;
        for (var i = startIndex; i < endIndex; i++)
        {
            if (bars[i].Low < minPrice) minPrice = bars[i].Low;
            if (bars[i].High > maxPrice) maxPrice = bars[i].High;
        }

        var fastEma = FastEmaValues;
        var slowEma = SlowEmaValues;
        var vwap = VwapValues;
        ExpandRange(fastEma, startIndex, endIndex, bars.Count, ref minPrice, ref maxPrice);
        ExpandRange(slowEma, startIndex, endIndex, bars.Count, ref minPrice, ref maxPrice);
        ExpandRange(vwap, startIndex, endIndex, bars.Count, ref minPrice, ref maxPrice);

        var range = maxPrice - minPrice;
        if (range == 0) range = 1;
        var padding = range * 0.10m;
        minPrice -= padding;
        maxPrice += padding;
        range = maxPrice - minPrice;

        double PriceToY(decimal price) => TopPadding + (double)((maxPrice - price) / range) * chartH;
        double IndexToX(int idx) => (idx - startIndex) * step + effectiveBarW / 2;

        DrawGrid(dc, chartW, chartH, minPrice, maxPrice, range, PriceToY);

        // Session separator lines (day boundaries)
        for (var i = startIndex + 1; i < endIndex; i++)
        {
            if (TimeZoneInfo.ConvertTime(bars[i].OpenTimeUtc, EasternTz).Date != TimeZoneInfo.ConvertTime(bars[i - 1].OpenTimeUtc, EasternTz).Date)
            {
                var sepX = IndexToX(i) - step / 2;
                dc.DrawLine(SessionSeparatorPen, new Point(sepX, TopPadding), new Point(sepX, TopPadding + chartH));
            }
        }

        // Candlesticks
        var useGradient = effectiveBarW >= 5; // gradient only looks good on wider bars
        for (var i = startIndex; i < endIndex; i++)
        {
            var bar = bars[i];
            var x = IndexToX(i);
            var yHigh = PriceToY(bar.High);
            var yLow = PriceToY(bar.Low);
            var yOpen = PriceToY(bar.Open);
            var yClose = PriceToY(bar.Close);
            var isBull = bar.Close >= bar.Open;

            dc.DrawLine(isBull ? BullWickPen : BearWickPen, new Point(x, yHigh), new Point(x, yLow));
            var bodyTop = Math.Min(yOpen, yClose);
            var bodyHeight = Math.Max(1, Math.Abs(yOpen - yClose));
            var bodyRect = new Rect(x - effectiveBarW / 2, bodyTop, effectiveBarW, bodyHeight);
            var bodyBrush = useGradient ? (isBull ? BullGradientTop : BearGradientTop) : (isBull ? BullBrush : BearBrush);
            dc.DrawRoundedRectangle(bodyBrush, null, bodyRect, effectiveBarW > 6 ? 1.5 : 0, effectiveBarW > 6 ? 1.5 : 0);
        }

        // EMA fill between fast and slow (trend cloud)
        DrawEmaFill(dc, fastEma, slowEma, bars.Count, startIndex, endIndex, PriceToY, IndexToX);

        // EMA lines
        DrawOverlayLine(dc, fastEma, bars.Count, startIndex, endIndex, FastEmaPen, PriceToY, IndexToX);
        DrawOverlayLine(dc, slowEma, bars.Count, startIndex, endIndex, SlowEmaPen, PriceToY, IndexToX);

        // VWAP line
        DrawOverlayLine(dc, vwap, bars.Count, startIndex, endIndex, VwapPen, PriceToY, IndexToX);

        // EMA crossover markers — small dots where fast EMA crosses slow EMA
        if (fastEma is { Count: > 0 } && slowEma is { Count: > 0 })
        {
            var fastOff = bars.Count - fastEma.Count;
            var slowOff = bars.Count - slowEma.Count;
            for (var i = startIndex + 1; i < endIndex; i++)
            {
                var fi = i - fastOff; var fi1 = fi - 1;
                var si = i - slowOff; var si1 = si - 1;
                if (fi < 1 || fi >= fastEma.Count || si < 1 || si >= slowEma.Count) continue;
                var prevAbove = fastEma[fi1] >= slowEma[si1];
                var currAbove = fastEma[fi] >= slowEma[si];
                if (prevAbove != currAbove)
                {
                    var cx = IndexToX(i);
                    var cy = PriceToY(fastEma[fi]);
                    var isBullCross = currAbove;
                    dc.DrawEllipse(isBullCross ? CrossoverBullBrush : CrossoverBearBrush, null, new Point(cx, cy), 4, 4);
                    dc.DrawEllipse(null, isBullCross ? BullPen : BearPen, new Point(cx, cy), 6, 6);
                }
            }
        }

        // Signal markers (existing)
        DrawSignals(dc, bars, startIndex, endIndex, PriceToY, IndexToX);

        // Trade markers (new)
        DrawTradeMarkers(dc, bars, startIndex, endIndex, PriceToY, IndexToX);

        // Volume bars sub-panel
        DrawVolumeBars(dc, bars, startIndex, endIndex, chartW, chartH, volumeH, effectiveBarW, step, IndexToX);

        // Price axis background panel
        dc.DrawRectangle(PriceAxisBg, null, new Rect(chartW, 0, PriceAxisWidth, h));

        DrawPriceAxis(dc, chartW, chartH, minPrice, maxPrice, range, PriceToY);
        DrawTimeAxis(dc, bars, startIndex, endIndex, h, effectiveBarW, IndexToX);

        // Current price line
        if (endIndex > 0)
        {
            var lastBar = bars[endIndex - 1];
            var lastY = PriceToY(lastBar.Close);
            if (lastY >= TopPadding && lastY <= TopPadding + chartH)
            {
                dc.DrawLine(CurrentPricePen, new Point(0, lastY), new Point(chartW, lastY));
                var lastLabel = lastBar.Close.ToString("F2");
                var lastFt = MakeText(lastLabel, 12, Brushes.White);
                dc.DrawRoundedRectangle(CurrentPriceLabelBg, null,
                    new Rect(chartW + 1, lastY - 9, PriceAxisWidth - 2, 18), 3, 3);
                dc.DrawText(lastFt, new Point(chartW + 4, lastY - 7));
            }
        }

        // Session high/low markers (dotted lines at visible high/low)
        {
            decimal visHigh = decimal.MinValue, visLow = decimal.MaxValue;
            var hiIdx = startIndex;
            var loIdx = startIndex;
            for (var i = startIndex; i < endIndex; i++)
            {
                if (bars[i].High > visHigh) { visHigh = bars[i].High; hiIdx = i; }
                if (bars[i].Low < visLow) { visLow = bars[i].Low; loIdx = i; }
            }
            var hiY = PriceToY(visHigh);
            var loY = PriceToY(visLow);
            dc.DrawLine(HighLowDashPen, new Point(0, hiY), new Point(chartW, hiY));
            dc.DrawLine(HighLowDashPen, new Point(0, loY), new Point(chartW, loY));
            // High label
            var hiFt = MakeText($"H {visHigh:F2}", 10.5, HighLabelBrush);
            dc.DrawText(hiFt, new Point(IndexToX(hiIdx) + effectiveBarW, hiY - hiFt.Height));
            // Low label
            var loFt = MakeText($"L {visLow:F2}", 10.5, LowLabelBrush);
            dc.DrawText(loFt, new Point(IndexToX(loIdx) + effectiveBarW, loY + 1));
        }

        // EMA/VWAP endpoint labels (right edge of visible area)
        DrawEndpointLabel(dc, fastEma, bars.Count, endIndex, chartW, PriceToY, FastEmaPen.Brush, "EMA-F");
        DrawEndpointLabel(dc, slowEma, bars.Count, endIndex, chartW, PriceToY, SlowEmaPen.Brush, "EMA-S");
        DrawEndpointLabel(dc, vwap, bars.Count, endIndex, chartW, PriceToY, VwapPen.Brush, "VWAP");

        // Scroll position indicator (mini bar at bottom)
        if (bars.Count > visibleCount)
        {
            var indicatorY = TopPadding + chartH + volumeH - 6;
            var indicatorW = chartW - 20;
            dc.DrawRoundedRectangle(ScrollIndicatorBg, null, new Rect(10, indicatorY, indicatorW, 4), 2, 2);
            var viewRatio = (double)visibleCount / bars.Count;
            var posRatio = (double)startIndex / bars.Count;
            var thumbW = Math.Max(20, indicatorW * viewRatio);
            var thumbX = 10 + posRatio * (indicatorW - thumbW);
            dc.DrawRoundedRectangle(ScrollIndicatorFg, null, new Rect(thumbX, indicatorY, thumbW, 4), 2, 2);
        }

        // Chart legend
        DrawLegend(dc, chartW, fastEma, slowEma, vwap);

        // OHLCV info panel at top-left on hover
        if (_showCrosshair && _mousePosition.X < chartW)
        {
            var crossBarIdx = startIndex + (int)(_mousePosition.X / step);
            if (crossBarIdx >= startIndex && crossBarIdx < endIndex)
            {
                var cb = bars[crossBarIdx];
                var isBull = cb.Close >= cb.Open;
                var priceColor = isBull ? BullBrush : BearBrush;
                var change = cb.Close - cb.Open;
                var changePct = cb.Open != 0 ? change / cb.Open * 100 : 0;
                var changeSign = change >= 0 ? "+" : "";

                var timeFt = MakeText(TimeZoneInfo.ConvertTime(cb.OpenTimeUtc, EasternTz).ToString("yyyy-MM-dd HH:mm") + " ET", 10.5, AxisBrush);
                var ohlcvLine1 = $"O {cb.Open:F2}   H {cb.High:F2}   L {cb.Low:F2}   C {cb.Close:F2}";
                var ohlcvLine2 = $"Vol {cb.Volume:N0}   Chg {changeSign}{change:F2} ({changeSign}{changePct:F2}%)";
                var ohlcvFt1 = MakeText(ohlcvLine1, 11.5, priceColor);
                var ohlcvFt2 = MakeText(ohlcvLine2, 10.5, priceColor);

                var panelW = Math.Max(Math.Max(ohlcvFt1.Width, ohlcvFt2.Width), timeFt.Width) + 20;
                var panelH = timeFt.Height + ohlcvFt1.Height + ohlcvFt2.Height + 16;
                dc.DrawRoundedRectangle(InfoPanelBg, InfoPanelBorder, new Rect(6, 2, panelW, panelH), 5, 5);
                dc.DrawText(timeFt, new Point(14, 5));
                dc.DrawText(ohlcvFt1, new Point(14, 5 + timeFt.Height + 1));
                dc.DrawText(ohlcvFt2, new Point(14, 5 + timeFt.Height + ohlcvFt1.Height + 3));
            }
        }

        // Crosshair (snaps to bar center)
        if (_showCrosshair && _mousePosition.X < chartW && _mousePosition.Y > TopPadding && _mousePosition.Y < TopPadding + chartH)
        {
            var crossBarIdx = startIndex + (int)(_mousePosition.X / step);
            crossBarIdx = Math.Clamp(crossBarIdx, startIndex, endIndex - 1);
            var snappedX = IndexToX(crossBarIdx);

            // Bar highlight column
            dc.DrawRectangle(BarHighlightBrush, null,
                new Rect(snappedX - step / 2, TopPadding, step, chartH));

            dc.DrawLine(CrosshairPen, new Point(0, _mousePosition.Y), new Point(chartW, _mousePosition.Y));
            dc.DrawLine(CrosshairPen, new Point(snappedX, TopPadding), new Point(snappedX, TopPadding + chartH));

            var crossPrice = maxPrice - (decimal)((_mousePosition.Y - TopPadding) / chartH) * range;
            var priceLabel = crossPrice.ToString("F2");
            var ft = MakeText(priceLabel, 11, Brushes.White);
            dc.DrawRoundedRectangle(CrosshairLabelBg, null,
                new Rect(chartW + 2, _mousePosition.Y - 10, PriceAxisWidth - 4, 20), 4, 4);
            dc.DrawText(ft, new Point(chartW + 4, _mousePosition.Y - 8));

            if (crossBarIdx >= startIndex && crossBarIdx < endIndex)
            {
                var crossBar = bars[crossBarIdx];
                var timeLabel = TimeZoneInfo.ConvertTime(crossBar.OpenTimeUtc, EasternTz).ToString("HH:mm");
                var timeFt = MakeText(timeLabel, 11, Brushes.White);
                dc.DrawRoundedRectangle(CrosshairLabelBg, null,
                    new Rect(snappedX - 24, TopPadding + chartH + 2, 48, 20), 4, 4);
                dc.DrawText(timeFt, new Point(snappedX - 20, TopPadding + chartH + 4));
            }
        }
    }

    private void DrawVolumeBars(DrawingContext dc, IReadOnlyList<MarketBar> bars, int startIndex, int endIndex,
        double chartW, double chartH, double volumeH, double barWidth, double step, Func<int, double> indexToX)
    {
        var volumeTop = TopPadding + chartH;

        // Separator line
        dc.DrawLine(VolumeSeparatorPen, new Point(0, volumeTop), new Point(chartW, volumeTop));

        // Find max volume
        decimal maxVolume = 0;
        for (var i = startIndex; i < endIndex; i++)
        {
            if (bars[i].Volume > maxVolume) maxVolume = bars[i].Volume;
        }
        if (maxVolume == 0) return;

        // Volume label
        var volLabel = MakeText($"Vol: {maxVolume:N0}", 10, AxisBrush);
        dc.DrawText(volLabel, new Point(4, volumeTop + 2));

        var volRadius = barWidth > 6 ? 1.5 : 0;
        for (var i = startIndex; i < endIndex; i++)
        {
            var bar = bars[i];
            var x = indexToX(i);
            var barH = (double)(bar.Volume / maxVolume) * volumeH;
            var isBull = bar.Close >= bar.Open;

            // Use gradient brush for taller volume bars (opacity increases upward)
            if (barH > 4 && barWidth >= 5)
            {
                var top = volumeTop + volumeH - barH;
                var grad = isBull
                    ? new LinearGradientBrush(
                        Color.FromArgb(120, 0x00, 0xB8, 0x94), Color.FromArgb(30, 0x00, 0xB8, 0x94),
                        new Point(0, 0), new Point(0, 1))
                    : new LinearGradientBrush(
                        Color.FromArgb(120, 0xE1, 0x70, 0x55), Color.FromArgb(30, 0xE1, 0x70, 0x55),
                        new Point(0, 0), new Point(0, 1));
                grad.Freeze();
                dc.DrawRoundedRectangle(grad, null, new Rect(x - barWidth / 2, top, barWidth, barH), volRadius, volRadius);
            }
            else
            {
                var rect = new Rect(x - barWidth / 2, volumeTop + volumeH - barH, barWidth, barH);
                dc.DrawRoundedRectangle(isBull ? BullVolumeBrush : BearVolumeBrush, null, rect, volRadius, volRadius);
            }
        }
    }

    private void DrawTradeMarkers(DrawingContext dc, IReadOnlyList<MarketBar> bars, int startIndex, int endIndex,
        Func<decimal, double> priceToY, Func<int, double> indexToX)
    {
        var markers = TradeMarkers;
        if (markers is null || markers.Count == 0) return;

        foreach (var marker in markers)
        {
            for (var i = startIndex; i < endIndex; i++)
            {
                if (marker.Time >= bars[i].OpenTimeUtc && marker.Time <= bars[i].CloseTimeUtc)
                {
                    var x = indexToX(i);
                    var y = priceToY(marker.Price);
                    var size = 10.0;

                    if (marker.IsEntry)
                    {
                        // Entry glow ring
                        dc.DrawEllipse(null, EntryGlowPen, new Point(x, y), 14, 14);

                        // Entry markers: filled triangles with outline
                        var tri = new StreamGeometry();
                        using (var ctx = tri.Open())
                        {
                            if (marker.IsLong)
                            {
                                // Up arrow
                                ctx.BeginFigure(new Point(x, y - size - 4), true, true);
                                ctx.LineTo(new Point(x - size, y + 2 - 4), true, false);
                                ctx.LineTo(new Point(x + size, y + 2 - 4), true, false);
                            }
                            else
                            {
                                // Down arrow
                                ctx.BeginFigure(new Point(x, y + size + 4), true, true);
                                ctx.LineTo(new Point(x - size, y - 2 + 4), true, false);
                                ctx.LineTo(new Point(x + size, y - 2 + 4), true, false);
                            }
                        }
                        tri.Freeze();
                        var entryBrush = marker.IsLong ? LongMarkerBrush : ShortMarkerBrush;
                        var outlinePen = new Pen(Brushes.White, 1.5);
                        outlinePen.Freeze();
                        dc.DrawGeometry(entryBrush, outlinePen, tri);
                    }
                    else
                    {
                        // Exit marker: filled X with glow ring
                        var exitBrush = marker.IsLong ? ExitMarkerBrushLong : ExitMarkerBrushShort;
                        var exitGlow = marker.IsLong ? ExitGlowPenLong : ExitGlowPenShort;
                        var exitPen = marker.IsLong ? ExitMarkerPenLong : ExitMarkerPenShort;

                        // Glow ring
                        dc.DrawEllipse(null, exitGlow, new Point(x, y), 9, 9);

                        // X mark
                        var s = size + 1;
                        dc.DrawLine(exitPen, new Point(x - s, y - s), new Point(x + s, y + s));
                        dc.DrawLine(exitPen, new Point(x - s, y + s), new Point(x + s, y - s));

                        // Center dot
                        dc.DrawEllipse(exitBrush, null, new Point(x, y), 2.5, 2.5);
                    }
                    break;
                }
            }
        }
    }

    private void DrawEmptyMessage(DrawingContext dc, double w, double h)
    {
        var cx = w / 2;
        var cy = h / 2;

        // Multiple decorative chart lines for depth
        for (var layer = 0; layer < 3; layer++)
        {
            var offsetY = layer * 12;
            var alpha = (byte)(30 - layer * 8);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 0x38, 0xBD, 0xF8));
            brush.Freeze();
            var pen = new Pen(brush, 1.5 - layer * 0.3);
            pen.Freeze();
            var decoGeom = new StreamGeometry();
            using (var ctx = decoGeom.Open())
            {
                ctx.BeginFigure(new Point(cx - 160, cy + 25 + offsetY), false, false);
                ctx.BezierTo(
                    new Point(cx - 100, cy + 5 + offsetY),
                    new Point(cx - 50, cy + 20 + offsetY),
                    new Point(cx, cy - 8 + offsetY), true, false);
                ctx.BezierTo(
                    new Point(cx + 50, cy - 30 + offsetY),
                    new Point(cx + 100, cy + 5 + offsetY),
                    new Point(cx + 160, cy - 20 + offsetY), true, false);
            }
            decoGeom.Freeze();
            dc.DrawGeometry(null, pen, decoGeom);
        }

        // Candlestick silhouettes
        var candleBrush = new SolidColorBrush(Color.FromArgb(20, 0x38, 0xBD, 0xF8));
        candleBrush.Freeze();
        var candlePen = new Pen(candleBrush, 1);
        candlePen.Freeze();
        for (var i = -3; i <= 3; i++)
        {
            var candleX = cx + i * 25;
            var candleH = 15 + Math.Abs(i) * 5;
            var candleTop = cy + 35 - candleH / 2;
            dc.DrawRectangle(candleBrush, null, new Rect(candleX - 3, candleTop, 6, candleH));
            dc.DrawLine(candlePen, new Point(candleX, candleTop - 5), new Point(candleX, candleTop + candleH + 5));
        }

        var ft = MakeText("Waiting for market data...", 16, Brushes.Gray);
        dc.DrawText(ft, new Point(w / 2 - ft.Width / 2, h / 2 - ft.Height / 2 - 40));

        var sub = MakeText("Connect to IBKR or load historical bars to begin", 11, EmptySubtitleBrush);
        dc.DrawText(sub, new Point(w / 2 - sub.Width / 2, h / 2 - sub.Height / 2 - 18));
    }

    private void DrawEmaFill(DrawingContext dc, IReadOnlyList<decimal>? fastEma, IReadOnlyList<decimal>? slowEma,
        int totalBars, int startIndex, int endIndex, Func<decimal, double> priceToY, Func<int, double> indexToX)
    {
        if (fastEma is null || slowEma is null || fastEma.Count == 0 || slowEma.Count == 0) return;

        var fastOffset = totalBars - fastEma.Count;
        var slowOffset = totalBars - slowEma.Count;

        // Build a filled region between the two EMAs
        var topPoints = new List<Point>();
        var bottomPoints = new List<Point>();
        var lastBull = true;

        for (var i = startIndex; i < endIndex; i++)
        {
            var fi = i - fastOffset;
            var si = i - slowOffset;
            if (fi < 0 || fi >= fastEma.Count || si < 0 || si >= slowEma.Count) continue;

            var fastY = priceToY(fastEma[fi]);
            var slowY = priceToY(slowEma[si]);
            var x = indexToX(i);
            var isBull = fastEma[fi] >= slowEma[si];

            if (topPoints.Count > 0 && isBull != lastBull)
            {
                // Flush current segment
                FlushEmaSegment(dc, topPoints, bottomPoints, lastBull);
                topPoints.Clear();
                bottomPoints.Clear();
            }

            topPoints.Add(new Point(x, Math.Min(fastY, slowY)));
            bottomPoints.Add(new Point(x, Math.Max(fastY, slowY)));
            lastBull = isBull;
        }

        if (topPoints.Count > 0)
            FlushEmaSegment(dc, topPoints, bottomPoints, lastBull);
    }

    private static void FlushEmaSegment(DrawingContext dc, List<Point> top, List<Point> bottom, bool isBull)
    {
        if (top.Count < 2) return;
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(top[0], true, true);
            for (var i = 1; i < top.Count; i++)
                ctx.LineTo(top[i], true, false);
            for (var i = bottom.Count - 1; i >= 0; i--)
                ctx.LineTo(bottom[i], true, false);
        }
        geom.Freeze();
        dc.DrawGeometry(isBull ? EmaFillBullBrush : EmaFillBearBrush, null, geom);
    }

    private void DrawLegend(DrawingContext dc, double chartW,
        IReadOnlyList<decimal>? fastEma, IReadOnlyList<decimal>? slowEma, IReadOnlyList<decimal>? vwap)
    {
        var bars = Bars;
        var totalBars = bars?.Count ?? 0;
        var items = new List<(string label, string? value, Brush color, Pen pen)>();
        if (fastEma is { Count: > 0 })
            items.Add(("EMA Fast", fastEma[^1].ToString("F2"), FastEmaPen.Brush, FastEmaPen));
        if (slowEma is { Count: > 0 })
            items.Add(("EMA Slow", slowEma[^1].ToString("F2"), SlowEmaPen.Brush, SlowEmaPen));
        if (vwap is { Count: > 0 })
            items.Add(("VWAP", vwap[^1].ToString("F2"), VwapPen.Brush, VwapPen));
        if (items.Count == 0) return;

        var x = chartW - 8;
        var y = TopPadding + 4;

        foreach (var (label, value, color, pen) in items)
        {
            var labelFt = MakeText(label, 10.5, color);
            var valueFt = value != null ? MakeText($" {value}", 10.5, Brushes.White) : null;
            var lineW = 14;
            var itemW = lineW + 4 + labelFt.Width + (valueFt?.Width ?? 0) + 8;
            var itemX = x - itemW;

            // Background
            dc.DrawRoundedRectangle(LegendBg, null,
                new Rect(itemX - 2, y - 1, itemW + 4, labelFt.Height + 2), 3, 3);

            // Color line
            dc.DrawLine(pen, new Point(itemX + 2, y + labelFt.Height / 2), new Point(itemX + 2 + lineW, y + labelFt.Height / 2));

            // Label + value
            dc.DrawText(labelFt, new Point(itemX + lineW + 6, y));
            if (valueFt != null)
                dc.DrawText(valueFt, new Point(itemX + lineW + 6 + labelFt.Width, y));

            y += labelFt.Height + 4;
        }
    }

    private void DrawGrid(DrawingContext dc, double chartW, double chartH, decimal minPrice, decimal maxPrice, decimal range, Func<decimal, double> priceToY)
    {
        var gridStep = CalculateGridStep(range);
        var firstGrid = Math.Ceiling(minPrice / gridStep) * gridStep;
        for (var p = firstGrid; p <= maxPrice; p += gridStep)
        {
            var y = priceToY(p);
            dc.DrawLine(GridPen, new Point(0, y), new Point(chartW, y));
        }
    }

    private void DrawPriceAxis(DrawingContext dc, double chartW, double chartH, decimal minPrice, decimal maxPrice, decimal range, Func<decimal, double> priceToY)
    {
        var gridStep = CalculateGridStep(range);
        var firstGrid = Math.Ceiling(minPrice / gridStep) * gridStep;
        for (var p = firstGrid; p <= maxPrice; p += gridStep)
        {
            var y = priceToY(p);
            var ft = MakeText(p.ToString("F2"), 11, AxisBrush);
            dc.DrawText(ft, new Point(chartW + 4, y - ft.Height / 2));
        }
    }

    private void DrawTimeAxis(DrawingContext dc, IReadOnlyList<MarketBar> bars, int startIndex, int endIndex, double totalH, double barWidth, Func<int, double> indexToX)
    {
        var labelInterval = Math.Max(1, (endIndex - startIndex) / 12);
        var y = totalH - TimeAxisHeight + 4;

        // Subtle background for the time axis area
        dc.DrawRectangle(PriceAxisBg, null, new Rect(0, totalH - TimeAxisHeight, indexToX(endIndex - 1) + barWidth, TimeAxisHeight));

        // Track which dates we've labeled
        var labeledDates = new HashSet<DateTime>();

        for (var i = startIndex; i < endIndex; i += labelInterval)
        {
            var x = indexToX(i);
            var localTime = TimeZoneInfo.ConvertTime(bars[i].OpenTimeUtc, EasternTz);
            var barDate = localTime.Date;

            // Show date for first bar of each new day, HH:mm otherwise
            if (!labeledDates.Contains(barDate))
            {
                var label = localTime.ToString("MMM dd");
                labeledDates.Add(barDate);
                var dateFt = MakeText(label, 11, Brushes.White);
                dc.DrawText(dateFt, new Point(x - dateFt.Width / 2, y));
            }
            else
            {
                var label = localTime.ToString("HH:mm");
                var ft = MakeText(label, 10.5, AxisBrush);
                dc.DrawText(ft, new Point(x - ft.Width / 2, y + 1));
            }
        }
    }

    private void DrawOverlayLine(DrawingContext dc, IReadOnlyList<decimal>? values, int totalBars, int startIndex, int endIndex, Pen pen, Func<decimal, double> priceToY, Func<int, double> indexToX)
    {
        if (values is null || values.Count == 0) return;

        var offset = totalBars - values.Count;

        // Collect valid points
        var points = new List<Point>();
        for (var i = startIndex; i < endIndex; i++)
        {
            var idx = i - offset;
            if (idx < 0 || idx >= values.Count) continue;
            points.Add(new Point(indexToX(i), priceToY(values[idx])));
        }
        if (points.Count < 2) return;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);

            // Use Catmull-Rom to Bezier conversion for smooth curves
            for (var i = 0; i < points.Count - 1; i++)
            {
                var p0 = i > 0 ? points[i - 1] : points[i];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = i + 2 < points.Count ? points[i + 2] : points[i + 1];

                var cp1 = new Point(
                    p1.X + (p2.X - p0.X) / 6.0,
                    p1.Y + (p2.Y - p0.Y) / 6.0);
                var cp2 = new Point(
                    p2.X - (p3.X - p1.X) / 6.0,
                    p2.Y - (p3.Y - p1.Y) / 6.0);

                ctx.BezierTo(cp1, cp2, p2, true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawSignals(DrawingContext dc, IReadOnlyList<MarketBar> bars, int startIndex, int endIndex, Func<decimal, double> priceToY, Func<int, double> indexToX)
    {
        var signals = Signals;
        if (signals is null || signals.Count == 0) return;

        foreach (var signal in signals)
        {
            for (var i = startIndex; i < endIndex; i++)
            {
                if (bars[i].CloseTimeUtc == signal.BarTimeUtc)
                {
                    var x = indexToX(i);
                    var isLong = signal.Direction == PositionSide.Long;
                    var price = isLong ? bars[i].Low : bars[i].High;
                    var y = priceToY(price);
                    var markerSize = 10.0;
                    var signalOutline = new Pen(Brushes.White, 1.5);
                    signalOutline.Freeze();

                    if (isLong)
                    {
                        var tri = new StreamGeometry();
                        using (var ctx = tri.Open())
                        {
                            ctx.BeginFigure(new Point(x, y + markerSize + 4), true, true);
                            ctx.LineTo(new Point(x - markerSize, y + markerSize * 2 + 4), true, false);
                            ctx.LineTo(new Point(x + markerSize, y + markerSize * 2 + 4), true, false);
                        }
                        tri.Freeze();
                        dc.DrawGeometry(LongMarkerBrush, signalOutline, tri);
                    }
                    else
                    {
                        var tri = new StreamGeometry();
                        using (var ctx = tri.Open())
                        {
                            ctx.BeginFigure(new Point(x, y - markerSize - 4), true, true);
                            ctx.LineTo(new Point(x - markerSize, y - markerSize * 2 - 4), true, false);
                            ctx.LineTo(new Point(x + markerSize, y - markerSize * 2 - 4), true, false);
                        }
                        tri.Freeze();
                        dc.DrawGeometry(ShortMarkerBrush, signalOutline, tri);
                    }
                    break;
                }
            }
        }
    }

    private void DrawEndpointLabel(DrawingContext dc, IReadOnlyList<decimal>? values, int totalBars, int endIndex,
        double chartW, Func<decimal, double> priceToY, Brush color, string name)
    {
        if (values is null || values.Count == 0) return;
        var offset = totalBars - values.Count;
        var idx = endIndex - 1 - offset;
        if (idx < 0 || idx >= values.Count) return;
        var val = values[idx];
        var y = priceToY(val);
        var ft = MakeText($"{name} {val:F2}", 10.5, color);
        var labelX = chartW - ft.Width - 4;
        dc.DrawText(ft, new Point(labelX, y - ft.Height - 1));
    }

    private static void ExpandRange(IReadOnlyList<decimal>? values, int startIndex, int endIndex, int totalBars, ref decimal min, ref decimal max)
    {
        if (values is null || values.Count == 0) return;
        var offset = totalBars - values.Count;
        for (var i = startIndex; i < endIndex; i++)
        {
            var idx = i - offset;
            if (idx < 0 || idx >= values.Count) continue;
            if (values[idx] < min) min = values[idx];
            if (values[idx] > max) max = values[idx];
        }
    }

    private static decimal CalculateGridStep(decimal range)
    {
        if (range <= 5) return 0.5m;
        if (range <= 20) return 2m;
        if (range <= 50) return 5m;
        if (range <= 100) return 10m;
        if (range <= 500) return 25m;
        return 50m;
    }

    private FormattedText MakeText(string text, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
