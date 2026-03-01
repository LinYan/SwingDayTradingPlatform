using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.UI.Wpf.Controls;

public sealed class EquityCurveCanvas : Canvas
{
    private const double PriceAxisWidth = 80;
    private const double TimeAxisHeight = 28;
    private const double TopPadding = 10;

    private Point _mousePosition;
    private bool _showCrosshair;

    public static readonly DependencyProperty EquityCurveProperty =
        DependencyProperty.Register(nameof(EquityCurve), typeof(IReadOnlyList<EquityPoint>), typeof(EquityCurveCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<EquityPoint>? EquityCurve
    {
        get => (IReadOnlyList<EquityPoint>?)GetValue(EquityCurveProperty);
        set => SetValue(EquityCurveProperty, value);
    }

    private static readonly Brush ChartBg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
    private static readonly Pen EquityPen = new(new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)), 2);
    private static readonly Pen EquityGlowPen = new(new SolidColorBrush(Color.FromArgb(40, 0x38, 0xBD, 0xF8)), 6);
    private static readonly Pen YearBoundaryPen;
    private static readonly Pen DrawdownPen = new(new SolidColorBrush(Color.FromArgb(100, 0xE1, 0x70, 0x55)), 1.5);
    private static readonly Brush DrawdownFill = new SolidColorBrush(Color.FromArgb(25, 0xE1, 0x70, 0x55));
    private static readonly Pen HighWatermarkPen;
    private static readonly Pen GridPen;
    private static readonly Pen CrosshairPen;
    private static readonly Pen ZeroLinePen;
    private static readonly Pen EmptyDecoPen;
    private static readonly Brush AxisBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
    private static readonly Brush EmptySubtitleBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x68));
    private static readonly Brush CrosshairLabelBg = new SolidColorBrush(Color.FromArgb(210, 0x1B, 0x30, 0x44));
    private static readonly Brush EndpointLabelBg = new SolidColorBrush(Color.FromArgb(200, 0x38, 0xBD, 0xF8));
    private static readonly Brush ProfitFillBrush;
    private static readonly Brush LossFillBrush;
    private static readonly Brush MetricsPanelBg = new SolidColorBrush(Color.FromArgb(180, 0x16, 0x16, 0x2E));
    private static readonly Pen MetricsPanelBorder;
    private static readonly Brush MetricLabelBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
    private static readonly Brush ProfitValueBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0x94));
    private static readonly Brush LossValueBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x70, 0x55));
    private static readonly Brush ReturnPctBrush = new SolidColorBrush(Color.FromArgb(100, 0x94, 0xA3, 0xB8));
    private static readonly Brush EmptyMsgBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
    private static readonly Typeface LabelTypeface = new("Consolas");

    static EquityCurveCanvas()
    {
        ChartBg.Freeze();
        EquityPen.Freeze();
        EquityGlowPen.Freeze();
        YearBoundaryPen = new Pen(new SolidColorBrush(Color.FromArgb(35, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = DashStyles.DashDot };
        YearBoundaryPen.Freeze();
        DrawdownPen.Freeze();
        DrawdownFill.Freeze();
        AxisBrush.Freeze();
        EmptySubtitleBrush.Freeze();
        CrosshairLabelBg.Freeze();
        EndpointLabelBg = new SolidColorBrush(Color.FromArgb(200, 0x38, 0xBD, 0xF8));
        EndpointLabelBg.Freeze();

        ProfitFillBrush = new LinearGradientBrush(
            Color.FromArgb(50, 0x38, 0xBD, 0xF8),
            Color.FromArgb(5, 0x38, 0xBD, 0xF8),
            new Point(0, 0), new Point(0, 1));
        ProfitFillBrush.Freeze();

        LossFillBrush = new LinearGradientBrush(
            Color.FromArgb(40, 0xE1, 0x70, 0x55),
            Color.FromArgb(5, 0xE1, 0x70, 0x55),
            new Point(0, 0), new Point(0, 1));
        LossFillBrush.Freeze();

        HighWatermarkPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 0x38, 0xBD, 0xF8)), 1) { DashStyle = DashStyles.Dot };
        HighWatermarkPen.Freeze();

        GridPen = new Pen(new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)), 1) { DashStyle = DashStyles.Dot };
        GridPen.Freeze();
        CrosshairPen = new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1) { DashStyle = DashStyles.Dash };
        CrosshairPen.Freeze();
        ZeroLinePen = new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1) { DashStyle = DashStyles.Dash };
        ZeroLinePen.Freeze();
        EmptyDecoPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 0x38, 0xBD, 0xF8)), 1.5);
        EmptyDecoPen.Freeze();
        MetricsPanelBg.Freeze();
        MetricsPanelBorder = new Pen(new SolidColorBrush(Color.FromArgb(40, 0x38, 0xBD, 0xF8)), 1);
        MetricsPanelBorder.Freeze();
        MetricLabelBrush.Freeze();
        ProfitValueBrush.Freeze();
        LossValueBrush.Freeze();
        ReturnPctBrush.Freeze();
        EmptyMsgBrush.Freeze();
    }

    public EquityCurveCanvas()
    {
        ClipToBounds = true;
        Background = ChartBg;
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
        if (w < 100 || h < 100) return;

        dc.DrawRectangle(ChartBg, null, new Rect(0, 0, w, h));

        var curve = EquityCurve;
        if (curve is null || curve.Count < 2)
        {
            DrawEmptyMessage(dc, w, h);
            return;
        }

        var chartW = w - PriceAxisWidth;
        var chartH = h - TimeAxisHeight - TopPadding;

        var minEquity = decimal.MaxValue;
        var maxEquity = decimal.MinValue;
        foreach (var pt in curve)
        {
            if (pt.Equity < minEquity) minEquity = pt.Equity;
            if (pt.Equity > maxEquity) maxEquity = pt.Equity;
        }

        var range = maxEquity - minEquity;
        if (range == 0) range = 1;
        var padding = range * 0.10m;
        minEquity -= padding;
        maxEquity += padding;
        range = maxEquity - minEquity;

        double PriceToY(decimal price) => TopPadding + (double)((maxEquity - price) / range) * chartH;
        double IndexToX(int idx) => idx * chartW / (curve.Count - 1);

        // Grid lines with alternating bands
        var gridStep = CalculateGridStep(range);
        var firstGrid = Math.Ceiling(minEquity / gridStep) * gridStep;
        var bandToggle = false;
        var bandBrush = new SolidColorBrush(Color.FromArgb(8, 255, 255, 255));
        bandBrush.Freeze();
        for (var p = firstGrid; p <= maxEquity; p += gridStep)
        {
            var y = PriceToY(p);
            dc.DrawLine(GridPen, new Point(0, y), new Point(chartW, y));
            var ft = MakeText(p.ToString("N0"), 10, AxisBrush);
            dc.DrawText(ft, new Point(chartW + 6, y - ft.Height / 2));

            // Alternating band fill
            if (bandToggle)
            {
                var nextP = p + gridStep;
                var nextY = nextP <= maxEquity ? PriceToY(nextP) : TopPadding;
                dc.DrawRectangle(bandBrush, null, new Rect(0, nextY, chartW, y - nextY));
            }
            bandToggle = !bandToggle;
        }

        // Starting capital line with label
        if (curve.Count > 0)
        {
            var startY = PriceToY(curve[0].Equity);
            if (startY >= TopPadding && startY <= TopPadding + chartH)
            {
                dc.DrawLine(ZeroLinePen, new Point(0, startY), new Point(chartW, startY));
                var startFt = MakeText($"Start ${curve[0].Equity:N0}", 8.5, AxisBrush);
                dc.DrawText(startFt, new Point(chartW - startFt.Width - 4, startY + 2));
            }
        }

        // Gradient fill under the equity curve (green if profitable, red if losing)
        var fillGeometry = new StreamGeometry();
        using (var ctx = fillGeometry.Open())
        {
            ctx.BeginFigure(new Point(IndexToX(0), TopPadding + chartH), true, true);
            for (var i = 0; i < curve.Count; i++)
                ctx.LineTo(new Point(IndexToX(i), PriceToY(curve[i].Equity)), true, false);
            ctx.LineTo(new Point(IndexToX(curve.Count - 1), TopPadding + chartH), true, false);
        }
        fillGeometry.Freeze();

        var lastEquity = curve[curve.Count - 1].Equity;
        var startEquity = curve[0].Equity;
        dc.DrawGeometry(lastEquity >= startEquity ? ProfitFillBrush : LossFillBrush, null, fillGeometry);

        // Year/quarter boundary markers
        for (var i = 1; i < curve.Count; i++)
        {
            if (curve[i].Timestamp.Year != curve[i - 1].Timestamp.Year)
            {
                var bx = IndexToX(i);
                dc.DrawLine(YearBoundaryPen, new Point(bx, TopPadding), new Point(bx, TopPadding + chartH));
                var yearFt = MakeText(curve[i].Timestamp.ToString("yyyy"), 9, AxisBrush);
                dc.DrawText(yearFt, new Point(bx + 3, TopPadding + 2));
            }
            else if (curve[i].Timestamp.Month != curve[i - 1].Timestamp.Month &&
                     curve[i].Timestamp.Month % 3 == 1) // Q2, Q3, Q4 start
            {
                var bx = IndexToX(i);
                dc.DrawLine(YearBoundaryPen, new Point(bx, TopPadding + chartH * 0.7), new Point(bx, TopPadding + chartH));
            }
        }

        // Equity curve line — Bezier-smoothed for professional look, with glow
        var points = new List<Point>();
        for (var i = 0; i < curve.Count; i++)
            points.Add(new Point(IndexToX(i), PriceToY(curve[i].Equity)));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            // Catmull-Rom to Bezier conversion
            for (var i = 0; i < points.Count - 1; i++)
            {
                var p0 = i > 0 ? points[i - 1] : points[i];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = i + 2 < points.Count ? points[i + 2] : points[i + 1];
                var cp1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                var cp2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
                ctx.BezierTo(cp1, cp2, p2, true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, EquityGlowPen, geometry);
        dc.DrawGeometry(null, EquityPen, geometry);

        // Drawdown shading — filled area between peak (top) and equity (bottom)
        var peakEquity = curve[0].Equity;
        var ddFillGeometry = new StreamGeometry();
        using (var ctx = ddFillGeometry.Open())
        {
            var inDrawdown = false;
            var segmentStartIdx = 0;
            var segmentPeak = 0m;

            for (var i = 0; i < curve.Count; i++)
            {
                if (curve[i].Equity > peakEquity)
                    peakEquity = curve[i].Equity;

                if (curve[i].DrawdownPct > 0)
                {
                    if (!inDrawdown)
                    {
                        segmentStartIdx = i;
                        segmentPeak = peakEquity;
                        inDrawdown = true;
                    }
                }
                else if (inDrawdown)
                {
                    // Draw peak line forward (top edge), then equity backward (bottom edge)
                    var peakY = PriceToY(segmentPeak);
                    ctx.BeginFigure(new Point(IndexToX(segmentStartIdx), peakY), true, true);
                    ctx.LineTo(new Point(IndexToX(i), peakY), true, false);
                    for (var j = i; j >= segmentStartIdx; j--)
                        ctx.LineTo(new Point(IndexToX(j), PriceToY(curve[j].Equity)), true, false);
                    inDrawdown = false;
                }
            }

            if (inDrawdown)
            {
                var peakY = PriceToY(segmentPeak);
                ctx.BeginFigure(new Point(IndexToX(segmentStartIdx), peakY), true, true);
                ctx.LineTo(new Point(IndexToX(curve.Count - 1), peakY), true, false);
                for (var j = curve.Count - 1; j >= segmentStartIdx; j--)
                    ctx.LineTo(new Point(IndexToX(j), PriceToY(curve[j].Equity)), true, false);
            }
        }
        ddFillGeometry.Freeze();
        dc.DrawGeometry(DrawdownFill, null, ddFillGeometry);

        // Drawdown line — separate figure per drawdown period to avoid connecting gaps
        var ddLineGeometry = new StreamGeometry();
        peakEquity = curve[0].Equity;
        using (var ctx = ddLineGeometry.Open())
        {
            var inDrawdown = false;

            for (var i = 0; i < curve.Count; i++)
            {
                if (curve[i].Equity > peakEquity)
                    peakEquity = curve[i].Equity;

                if (curve[i].DrawdownPct > 0)
                {
                    var x = IndexToX(i);
                    var yEquity = PriceToY(curve[i].Equity);

                    if (!inDrawdown)
                    {
                        ctx.BeginFigure(new Point(x, yEquity), false, false);
                        inDrawdown = true;
                    }
                    else
                    {
                        ctx.LineTo(new Point(x, yEquity), true, false);
                    }
                }
                else
                {
                    inDrawdown = false;
                }
            }
        }
        ddLineGeometry.Freeze();
        dc.DrawGeometry(null, DrawdownPen, ddLineGeometry);

        // High watermark line
        var hwmGeometry = new StreamGeometry();
        var hwmPeak = curve[0].Equity;
        using (var ctx = hwmGeometry.Open())
        {
            ctx.BeginFigure(new Point(IndexToX(0), PriceToY(hwmPeak)), false, false);
            for (var i = 1; i < curve.Count; i++)
            {
                if (curve[i].Equity > hwmPeak) hwmPeak = curve[i].Equity;
                ctx.LineTo(new Point(IndexToX(i), PriceToY(hwmPeak)), true, false);
            }
        }
        hwmGeometry.Freeze();
        dc.DrawGeometry(null, HighWatermarkPen, hwmGeometry);

        // Endpoint label — final equity value with glowing dot
        {
            var endX = IndexToX(curve.Count - 1);
            var endY = PriceToY(lastEquity);
            var endLabel = $"${lastEquity:N0}";
            var endFt = MakeText(endLabel, 10.5, Brushes.White);
            // Glow ring
            dc.DrawEllipse(null, EquityGlowPen, new Point(endX, endY), 8, 8);
            // Solid dot
            dc.DrawEllipse(EquityPen.Brush, null, new Point(endX, endY), 4, 4);
            dc.DrawEllipse(Brushes.White, null, new Point(endX, endY), 1.5, 1.5);
            // Draw label
            var labelX = endX - endFt.Width - 14;
            if (labelX < 4) labelX = endX + 8;
            dc.DrawRoundedRectangle(CrosshairLabelBg, null,
                new Rect(labelX, endY - 10, endFt.Width + 10, 20), 4, 4);
            dc.DrawText(endFt, new Point(labelX + 5, endY - 8));
        }

        // Key metrics overlay (top-left)
        {
            var totalReturn = startEquity != 0 ? (lastEquity - startEquity) / startEquity * 100 : 0;
            var maxDD = 0m;
            foreach (var pt in curve)
                if (pt.DrawdownPct > maxDD) maxDD = pt.DrawdownPct;
            var returnSign = totalReturn >= 0 ? "+" : "";
            var returnColor = totalReturn >= 0 ? ProfitValueBrush : LossValueBrush;

            var metLine1 = MakeText("Return", 9, MetricLabelBrush);
            var metVal1 = MakeText($"{returnSign}{totalReturn:F1}%", 12, returnColor);
            var metLine2 = MakeText("Max DD", 9, MetricLabelBrush);
            var metVal2 = MakeText($"-{maxDD:F1}%", 12, LossValueBrush);
            var metLine3 = MakeText("P&L", 9, MetricLabelBrush);
            var pnlSign = lastEquity >= startEquity ? "+" : "";
            var metVal3 = MakeText($"{pnlSign}${lastEquity - startEquity:N0}", 12, returnColor);

            var metLine4 = MakeText("Period", 9, MetricLabelBrush);
            var periodStr = $"{curve[0].Timestamp:MMM yy} – {curve[curve.Count - 1].Timestamp:MMM yy}";
            var metVal4 = MakeText(periodStr, 10, AxisBrush);

            var mw = Math.Max(Math.Max(Math.Max(metVal1.Width, metVal2.Width), metVal3.Width), metVal4.Width) + 20;
            var mh = (metLine1.Height + metVal1.Height) * 4 + 22;
            dc.DrawRoundedRectangle(MetricsPanelBg, MetricsPanelBorder, new Rect(6, TopPadding + 4, mw, mh), 5, 5);

            var my = TopPadding + 8;
            dc.DrawText(metLine1, new Point(14, my)); my += metLine1.Height;
            dc.DrawText(metVal1, new Point(14, my)); my += metVal1.Height + 3;
            dc.DrawText(metLine2, new Point(14, my)); my += metLine2.Height;
            dc.DrawText(metVal2, new Point(14, my)); my += metVal2.Height + 3;
            dc.DrawText(metLine3, new Point(14, my)); my += metLine3.Height;
            dc.DrawText(metVal3, new Point(14, my)); my += metVal3.Height + 3;
            dc.DrawText(metLine4, new Point(14, my)); my += metLine4.Height;
            dc.DrawText(metVal4, new Point(14, my));
        }

        // Time axis labels — show month labels at month boundaries
        var timeY = h - TimeAxisHeight + 6;
        var lastMonth = -1;
        for (var i = 0; i < curve.Count; i++)
        {
            var month = curve[i].Timestamp.Month;
            if (month != lastMonth)
            {
                var x = IndexToX(i);
                var isJan = month == 1 && lastMonth != -1;
                var label = isJan
                    ? curve[i].Timestamp.ToString("MMM yyyy")
                    : curve[i].Timestamp.ToString("MMM");
                var ft = MakeText(label, isJan ? 10 : 9.5, isJan ? Brushes.White : AxisBrush);
                dc.DrawText(ft, new Point(x, timeY));
                lastMonth = month;
            }
        }

        // Return % labels on right axis
        if (startEquity != 0)
        {
            var retGridStep = CalculateGridStep(range);
            var retFirstGrid = Math.Ceiling(minEquity / retGridStep) * retGridStep;
            for (var p = retFirstGrid; p <= maxEquity; p += retGridStep)
            {
                var y = PriceToY(p);
                var retPct = (p - startEquity) / startEquity * 100;
                var retSign = retPct >= 0 ? "+" : "";
                var retFt = MakeText($"{retSign}{retPct:F0}%", 8.5, ReturnPctBrush);
                dc.DrawText(retFt, new Point(chartW + PriceAxisWidth - retFt.Width - 2, y - retFt.Height / 2 + 8));
            }
        }

        // Crosshair
        if (_showCrosshair && _mousePosition.X < chartW && _mousePosition.Y > TopPadding && _mousePosition.Y < TopPadding + chartH)
        {
            dc.DrawLine(CrosshairPen, new Point(0, _mousePosition.Y), new Point(chartW, _mousePosition.Y));
            dc.DrawLine(CrosshairPen, new Point(_mousePosition.X, TopPadding), new Point(_mousePosition.X, TopPadding + chartH));

            var crossEquity = maxEquity - (decimal)((_mousePosition.Y - TopPadding) / chartH) * range;
            var priceLabel = crossEquity.ToString("N2");
            var priceFt = MakeText(priceLabel, 11, Brushes.White);
            dc.DrawRoundedRectangle(CrosshairLabelBg, null,
                new Rect(chartW + 2, _mousePosition.Y - 9, PriceAxisWidth - 4, 18), 4, 4);
            dc.DrawText(priceFt, new Point(chartW + 6, _mousePosition.Y - 7));

            var crossIdx = (int)(_mousePosition.X / chartW * (curve.Count - 1));
            crossIdx = Math.Clamp(crossIdx, 0, curve.Count - 1);
            var crossPoint = curve[crossIdx];
            var timeLabel = crossPoint.Timestamp.ToString("yyyy-MM-dd");
            var timeFt = MakeText(timeLabel, 11, Brushes.White);
            dc.DrawRoundedRectangle(CrosshairLabelBg, null,
                new Rect(_mousePosition.X - 38, TopPadding + chartH + 2, 76, 18), 4, 4);
            dc.DrawText(timeFt, new Point(_mousePosition.X - 34, TopPadding + chartH + 4));

            // Show equity value + drawdown + return near cursor
            var crossReturn = startEquity != 0 ? (crossPoint.Equity - startEquity) / startEquity * 100 : 0;
            var crossRetSign = crossReturn >= 0 ? "+" : "";
            var infoLine1 = $"${crossPoint.Equity:N0}  {crossRetSign}{crossReturn:F1}%";
            var infoLine2 = $"DD: {crossPoint.DrawdownPct:F1}%  |  {crossPoint.Timestamp:MMM dd, yyyy}";
            var infoFt1 = MakeText(infoLine1, 11, crossReturn >= 0 ? ProfitValueBrush : LossValueBrush);
            var infoFt2 = MakeText(infoLine2, 9.5, AxisBrush);
            var infoW = Math.Max(infoFt1.Width, infoFt2.Width) + 16;
            var infoH = infoFt1.Height + infoFt2.Height + 8;
            var infoX = _mousePosition.X + 14;
            var infoY = _mousePosition.Y - infoH - 4;
            if (infoX + infoW > chartW) infoX = _mousePosition.X - infoW - 8;
            if (infoY < TopPadding) infoY = _mousePosition.Y + 10;
            dc.DrawRoundedRectangle(CrosshairLabelBg, null,
                new Rect(infoX, infoY, infoW, infoH), 5, 5);
            dc.DrawText(infoFt1, new Point(infoX + 8, infoY + 4));
            dc.DrawText(infoFt2, new Point(infoX + 8, infoY + 4 + infoFt1.Height));
        }
    }

    private void DrawEmptyMessage(DrawingContext dc, double w, double h)
    {
        var cx = w / 2;
        var cy = h / 2;

        // Multiple decorative chart lines with different offsets for depth
        for (var layer = 0; layer < 3; layer++)
        {
            var offsetY = layer * 10;
            var alpha = (byte)(30 - layer * 8);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 0x38, 0xBD, 0xF8));
            brush.Freeze();
            var pen = new Pen(brush, 1.5 - layer * 0.3);
            pen.Freeze();
            var decoGeom = new StreamGeometry();
            using (var ctx = decoGeom.Open())
            {
                ctx.BeginFigure(new Point(cx - 140, cy + 25 + offsetY), false, false);
                ctx.BezierTo(
                    new Point(cx - 80, cy + 5 + offsetY),
                    new Point(cx - 30, cy + 20 + offsetY),
                    new Point(cx, cy - 5 + offsetY), true, false);
                ctx.BezierTo(
                    new Point(cx + 30, cy - 25 + offsetY),
                    new Point(cx + 80, cy + 5 + offsetY),
                    new Point(cx + 140, cy - 20 + offsetY), true, false);
            }
            decoGeom.Freeze();
            dc.DrawGeometry(null, pen, decoGeom);
        }

        // Decorative grid lines
        for (var i = 0; i < 5; i++)
        {
            var gy = cy - 30 + i * 18;
            dc.DrawLine(GridPen, new Point(cx - 150, gy), new Point(cx + 150, gy));
        }

        var ft = MakeText("Run a backtest to see equity curve", 15, EmptyMsgBrush);
        dc.DrawText(ft, new Point(w / 2 - ft.Width / 2, h / 2 - ft.Height / 2 - 35));

        var sub = MakeText("Configure parameters and click 'Run' to begin", 11, EmptySubtitleBrush);
        dc.DrawText(sub, new Point(w / 2 - sub.Width / 2, h / 2 - sub.Height / 2 - 15));
    }

    private static decimal CalculateGridStep(decimal range)
    {
        if (range <= 500) return 50m;
        if (range <= 2000) return 200m;
        if (range <= 5000) return 500m;
        if (range <= 20000) return 2000m;
        if (range <= 50000) return 5000m;
        return 10000m;
    }

    private FormattedText MakeText(string text, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
