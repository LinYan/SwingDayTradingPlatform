using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.UI.Wpf.Controls;

public sealed class DailyPnLBarChart : FrameworkElement
{
    private const double LeftPadding = 10;
    private const double RightAxisWidth = 70;
    private const double TopPadding = 8;
    private const double BottomAxisHeight = 28;
    private const double SummaryPadding = 8;

    private Point _mousePosition;
    private bool _showCrosshair;

    public static readonly DependencyProperty DailySummariesProperty =
        DependencyProperty.Register(nameof(DailySummaries), typeof(IReadOnlyList<DailySummary>), typeof(DailyPnLBarChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<DailySummary>? DailySummaries
    {
        get => (IReadOnlyList<DailySummary>?)GetValue(DailySummariesProperty);
        set => SetValue(DailySummariesProperty, value);
    }

    // ── Static brushes & pens (frozen for perf) ──
    private static readonly Brush ChartBg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
    private static readonly Brush ProfitBarBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0x94));
    private static readonly Brush LossBarBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x70, 0x55));
    private static readonly Brush ProfitBarHoverBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xD8, 0xB4));
    private static readonly Brush LossBarHoverBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x90, 0x75));
    private static readonly Brush AxisBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
    private static readonly Brush EmptyMsgBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
    private static readonly Brush CrosshairLabelBg = new SolidColorBrush(Color.FromArgb(210, 0x1B, 0x30, 0x44));
    private static readonly Brush MetricsPanelBg = new SolidColorBrush(Color.FromArgb(180, 0x16, 0x16, 0x2E));
    private static readonly Brush MetricLabelBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
    private static readonly Brush ProfitValueBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0x94));
    private static readonly Brush LossValueBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x70, 0x55));
    private static readonly Pen ZeroLinePen;
    private static readonly Pen GridPen;
    private static readonly Pen CrosshairPen;
    private static readonly Pen MetricsPanelBorder;
    private static readonly Pen MonthBoundaryPen;
    private static readonly Typeface LabelTypeface = new("Consolas");

    static DailyPnLBarChart()
    {
        ChartBg.Freeze();
        ProfitBarBrush.Freeze();
        LossBarBrush.Freeze();
        ProfitBarHoverBrush.Freeze();
        LossBarHoverBrush.Freeze();
        AxisBrush.Freeze();
        EmptyMsgBrush.Freeze();
        CrosshairLabelBg.Freeze();
        MetricsPanelBg.Freeze();
        MetricLabelBrush.Freeze();
        ProfitValueBrush.Freeze();
        LossValueBrush.Freeze();

        ZeroLinePen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);
        ZeroLinePen.Freeze();
        GridPen = new Pen(new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)), 1) { DashStyle = DashStyles.Dot };
        GridPen.Freeze();
        CrosshairPen = new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1) { DashStyle = DashStyles.Dash };
        CrosshairPen.Freeze();
        MetricsPanelBorder = new Pen(new SolidColorBrush(Color.FromArgb(40, 0x38, 0xBD, 0xF8)), 1);
        MetricsPanelBorder.Freeze();
        MonthBoundaryPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1) { DashStyle = DashStyles.Dot };
        MonthBoundaryPen.Freeze();
    }

    public DailyPnLBarChart()
    {
        ClipToBounds = true;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _mousePosition = e.GetPosition(this);
        _showCrosshair = true;
        InvalidateVisual();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _showCrosshair = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 100 || h < 60) return;

        dc.DrawRectangle(ChartBg, null, new Rect(0, 0, w, h));

        var data = DailySummaries;
        if (data is null || data.Count == 0)
        {
            var ft = MakeText("Run a backtest to see daily P&L distribution", 13, EmptyMsgBrush);
            dc.DrawText(ft, new Point(w / 2 - ft.Width / 2, h / 2 - ft.Height / 2));
            return;
        }

        var chartW = w - LeftPadding - RightAxisWidth;
        var chartH = h - TopPadding - BottomAxisHeight;
        if (chartW < 50 || chartH < 30) return;

        // Find max absolute PnL for symmetric Y-axis
        var maxAbs = 0m;
        foreach (var d in data)
        {
            var abs = Math.Abs(d.PnLDollars);
            if (abs > maxAbs) maxAbs = abs;
        }
        if (maxAbs == 0) maxAbs = 1;
        var yPadding = maxAbs * 0.12m;
        var yMax = maxAbs + yPadding;

        double ValueToY(decimal val) => TopPadding + chartH / 2.0 - (double)(val / yMax) * (chartH / 2.0);
        var zeroY = ValueToY(0);

        // Grid lines
        var gridStep = CalculateGridStep(maxAbs);
        for (var p = gridStep; p <= yMax; p += gridStep)
        {
            var yUp = ValueToY(p);
            var yDown = ValueToY(-p);
            dc.DrawLine(GridPen, new Point(LeftPadding, yUp), new Point(LeftPadding + chartW, yUp));
            dc.DrawLine(GridPen, new Point(LeftPadding, yDown), new Point(LeftPadding + chartW, yDown));

            var ftUp = MakeText($"+${p:N0}", 10, AxisBrush);
            dc.DrawText(ftUp, new Point(LeftPadding + chartW + 4, yUp - ftUp.Height / 2));
            var ftDown = MakeText($"-${p:N0}", 10, AxisBrush);
            dc.DrawText(ftDown, new Point(LeftPadding + chartW + 4, yDown - ftDown.Height / 2));
        }

        // Zero line
        dc.DrawLine(ZeroLinePen, new Point(LeftPadding, zeroY), new Point(LeftPadding + chartW, zeroY));
        var zeroFt = MakeText("$0", 10, AxisBrush);
        dc.DrawText(zeroFt, new Point(LeftPadding + chartW + 4, zeroY - zeroFt.Height / 2));

        // Bars
        var barSpacing = chartW / data.Count;
        var barWidth = Math.Max(1, barSpacing * 0.7);
        var barGap = (barSpacing - barWidth) / 2;

        var hoverIndex = -1;
        if (_showCrosshair && _mousePosition.X >= LeftPadding && _mousePosition.X <= LeftPadding + chartW)
        {
            hoverIndex = (int)((_mousePosition.X - LeftPadding) / barSpacing);
            if (hoverIndex >= data.Count) hoverIndex = data.Count - 1;
            if (hoverIndex < 0) hoverIndex = 0;
        }

        // Month boundary lines and labels
        var lastMonth = data[0].Date.Month;
        var lastYear = data[0].Date.Year;
        for (var i = 1; i < data.Count; i++)
        {
            var curMonth = data[i].Date.Month;
            var curYear = data[i].Date.Year;
            if (curMonth != lastMonth)
            {
                var bx = LeftPadding + i * barSpacing;
                dc.DrawLine(MonthBoundaryPen, new Point(bx, TopPadding), new Point(bx, TopPadding + chartH));

                var isNewYear = curYear != lastYear;
                var label = isNewYear
                    ? data[i].Date.ToString("MMM yyyy")
                    : data[i].Date.ToString("MMM");
                var labelBrush = isNewYear ? Brushes.White : AxisBrush;
                var ft = MakeText(label, isNewYear ? 10.5 : 10, labelBrush);
                // Only draw if it fits
                var labelX = bx + 2;
                if (labelX + ft.Width < LeftPadding + chartW)
                    dc.DrawText(ft, new Point(labelX, h - BottomAxisHeight + 6));

                lastYear = curYear;
            }
            lastMonth = curMonth;
        }

        // Draw first month label
        {
            var firstLabel = data[0].Date.ToString("MMM yyyy");
            var firstFt = MakeText(firstLabel, 10, AxisBrush);
            dc.DrawText(firstFt, new Point(LeftPadding + 2, h - BottomAxisHeight + 6));
        }

        // Draw bars
        for (var i = 0; i < data.Count; i++)
        {
            var d = data[i];
            if (d.TradeCount == 0) continue;

            var x = LeftPadding + i * barSpacing + barGap;
            var barY = ValueToY(d.PnLDollars);
            var isProfit = d.PnLDollars >= 0;
            var isHover = i == hoverIndex;

            Brush brush;
            if (isHover)
                brush = isProfit ? ProfitBarHoverBrush : LossBarHoverBrush;
            else
                brush = isProfit ? ProfitBarBrush : LossBarBrush;

            if (isProfit)
            {
                var barH = zeroY - barY;
                if (barH < 1) barH = 1;
                dc.DrawRectangle(brush, null, new Rect(x, barY, barWidth, barH));
            }
            else
            {
                var barH = barY - zeroY;
                if (barH < 1) barH = 1;
                dc.DrawRectangle(brush, null, new Rect(x, zeroY, barWidth, barH));
            }
        }

        // Summary overlay (top-right)
        DrawSummaryOverlay(dc, data, w);

        // Crosshair + tooltip
        if (_showCrosshair && hoverIndex >= 0 && hoverIndex < data.Count &&
            _mousePosition.Y >= TopPadding && _mousePosition.Y <= TopPadding + chartH)
        {
            // Vertical crosshair line
            var crossX = LeftPadding + hoverIndex * barSpacing + barSpacing / 2;
            dc.DrawLine(CrosshairPen, new Point(crossX, TopPadding), new Point(crossX, TopPadding + chartH));
            // Horizontal crosshair line
            dc.DrawLine(CrosshairPen, new Point(LeftPadding, _mousePosition.Y), new Point(LeftPadding + chartW, _mousePosition.Y));

            var d = data[hoverIndex];
            var sign = d.PnLDollars >= 0 ? "+" : "";
            var pnlColor = d.PnLDollars >= 0 ? ProfitValueBrush : LossValueBrush;
            var line1 = $"{d.Date:ddd, MMM dd yyyy}";
            var line2 = $"P&L: {sign}${d.PnLDollars:N2}";
            var line3 = $"Trades: {d.TradeCount}  W: {d.Wins}  L: {d.Losses}";

            var ft1 = MakeText(line1, 11, Brushes.White);
            var ft2 = MakeText(line2, 12, pnlColor);
            var ft3 = MakeText(line3, 10.5, AxisBrush);

            var tipW = Math.Max(Math.Max(ft1.Width, ft2.Width), ft3.Width) + 16;
            var tipH = ft1.Height + ft2.Height + ft3.Height + 12;
            var tipX = _mousePosition.X + 14;
            var tipY = _mousePosition.Y - tipH - 4;

            if (tipX + tipW > w - 4) tipX = _mousePosition.X - tipW - 8;
            if (tipY < TopPadding) tipY = _mousePosition.Y + 10;

            dc.DrawRoundedRectangle(CrosshairLabelBg, null, new Rect(tipX, tipY, tipW, tipH), 5, 5);
            dc.DrawText(ft1, new Point(tipX + 8, tipY + 4));
            dc.DrawText(ft2, new Point(tipX + 8, tipY + 4 + ft1.Height));
            dc.DrawText(ft3, new Point(tipX + 8, tipY + 4 + ft1.Height + ft2.Height));
        }
    }

    private void DrawSummaryOverlay(DrawingContext dc, IReadOnlyList<DailySummary> data, double w)
    {
        var totalPnL = 0m;
        var totalWins = 0;
        var totalTrades = 0;
        var tradingDays = 0;
        foreach (var d in data)
        {
            if (d.TradeCount > 0)
            {
                totalPnL += d.PnLDollars;
                totalWins += d.Wins;
                totalTrades += d.TradeCount;
                tradingDays++;
            }
        }
        var winRate = totalTrades > 0 ? (double)totalWins / totalTrades * 100 : 0;
        var pnlSign = totalPnL >= 0 ? "+" : "";
        var pnlBrush = totalPnL >= 0 ? ProfitValueBrush : LossValueBrush;

        var lblPnl = MakeText("Total P&L", 9.5, MetricLabelBrush);
        var valPnl = MakeText($"{pnlSign}${totalPnL:N0}", 11, pnlBrush);
        var lblWr = MakeText("Win Rate", 9.5, MetricLabelBrush);
        var valWr = MakeText($"{winRate:F1}%", 11, AxisBrush);
        var lblDays = MakeText("Trading Days", 9.5, MetricLabelBrush);
        var valDays = MakeText($"{tradingDays}", 11, AxisBrush);

        var mw = Math.Max(Math.Max(valPnl.Width, valWr.Width), valDays.Width) + 18;
        mw = Math.Max(mw, Math.Max(Math.Max(lblPnl.Width, lblWr.Width), lblDays.Width) + 18);
        var mh = (lblPnl.Height + valPnl.Height) * 3 + 18;

        var mx = w - RightAxisWidth - mw - SummaryPadding;
        var my = TopPadding + SummaryPadding;

        dc.DrawRoundedRectangle(MetricsPanelBg, MetricsPanelBorder, new Rect(mx, my, mw, mh), 5, 5);

        var cy = my + 4;
        dc.DrawText(lblPnl, new Point(mx + 8, cy)); cy += lblPnl.Height;
        dc.DrawText(valPnl, new Point(mx + 8, cy)); cy += valPnl.Height + 2;
        dc.DrawText(lblWr, new Point(mx + 8, cy)); cy += lblWr.Height;
        dc.DrawText(valWr, new Point(mx + 8, cy)); cy += valWr.Height + 2;
        dc.DrawText(lblDays, new Point(mx + 8, cy)); cy += lblDays.Height;
        dc.DrawText(valDays, new Point(mx + 8, cy));
    }

    private static decimal CalculateGridStep(decimal maxAbs)
    {
        if (maxAbs <= 100) return 25m;
        if (maxAbs <= 250) return 50m;
        if (maxAbs <= 500) return 100m;
        if (maxAbs <= 1000) return 250m;
        if (maxAbs <= 2500) return 500m;
        if (maxAbs <= 5000) return 1000m;
        if (maxAbs <= 10000) return 2000m;
        if (maxAbs <= 25000) return 5000m;
        return 10000m;
    }

    private FormattedText MakeText(string text, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
