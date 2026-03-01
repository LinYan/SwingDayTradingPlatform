using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SwingDayTradingPlatform.Backtesting;

namespace SwingDayTradingPlatform.UI.Wpf.Controls;

public sealed class PnLCalendarCanvas : FrameworkElement
{
    public static readonly DependencyProperty DailySummariesProperty =
        DependencyProperty.Register(nameof(DailySummaries), typeof(IReadOnlyList<DailySummary>), typeof(PnLCalendarCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDailySummariesChanged));

    private static void OnDailySummariesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PnLCalendarCanvas canvas && e.NewValue is IReadOnlyList<DailySummary> summaries && summaries.Count > 0)
        {
            var lastDate = summaries[^1].Date;
            canvas.DisplayMonth = lastDate.Month;
            canvas.DisplayYear = lastDate.Year;
        }
    }

    public static readonly DependencyProperty DisplayMonthProperty =
        DependencyProperty.Register(nameof(DisplayMonth), typeof(int), typeof(PnLCalendarCanvas),
            new FrameworkPropertyMetadata(DateTime.Today.Month, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DisplayYearProperty =
        DependencyProperty.Register(nameof(DisplayYear), typeof(int), typeof(PnLCalendarCanvas),
            new FrameworkPropertyMetadata(DateTime.Today.Year, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateOnly?), typeof(PnLCalendarCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public IReadOnlyList<DailySummary>? DailySummaries
    {
        get => (IReadOnlyList<DailySummary>?)GetValue(DailySummariesProperty);
        set => SetValue(DailySummariesProperty, value);
    }

    public int DisplayMonth
    {
        get => (int)GetValue(DisplayMonthProperty);
        set => SetValue(DisplayMonthProperty, value);
    }

    public int DisplayYear
    {
        get => (int)GetValue(DisplayYearProperty);
        set => SetValue(DisplayYearProperty, value);
    }

    public DateOnly? SelectedDate
    {
        get => (DateOnly?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public static readonly RoutedEvent DateSelectedEvent =
        EventManager.RegisterRoutedEvent(nameof(DateSelected), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(PnLCalendarCanvas));

    public event RoutedEventHandler DateSelected
    {
        add => AddHandler(DateSelectedEvent, value);
        remove => RemoveHandler(DateSelectedEvent, value);
    }

    // Typography
    private static readonly Typeface UiTypeface = new("Segoe UI");
    private static readonly Typeface MonoTypeface = new("Consolas");

    // Colors — modern flat palette
    private static readonly Brush BgBrush = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF8));
    private static readonly Brush HeaderBg;
    private static readonly Brush GreenBg = new SolidColorBrush(Color.FromArgb(45, 0x0F, 0x9D, 0x7A));
    private static readonly Brush RedBg = new SolidColorBrush(Color.FromArgb(45, 0xC4, 0x4B, 0x3B));
    private static readonly Brush GrayBg = new SolidColorBrush(Color.FromArgb(20, 0x64, 0x74, 0x8B));
    private static readonly Brush HoverOverlay = new SolidColorBrush(Color.FromArgb(30, 0x2B, 0x6C, 0xB0));
    private static readonly Brush GreenTextBrush = new SolidColorBrush(Color.FromRgb(0x05, 0x7A, 0x5E));
    private static readonly Brush RedTextBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0x3C, 0x2C));
    private static readonly Brush SelectedBorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0));
    private static readonly Brush DayNumBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
    private static readonly Brush DayHeaderBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
    private static readonly Brush ArrowBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
    private static readonly Brush SubtleBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xAE, 0xC0));
    private static readonly Brush TooltipBg = new SolidColorBrush(Color.FromArgb(230, 0x1B, 0x30, 0x44));
    private static readonly Brush WeekendDim = new SolidColorBrush(Color.FromArgb(18, 0x64, 0x74, 0x8B));
    private static readonly Brush TodayDotBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0));
    private static readonly Brush ArrowHoverBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush MonthSummaryBg = new SolidColorBrush(Color.FromArgb(30, 0x2B, 0x6C, 0xB0));
    private static readonly Brush DayCountBrush = new SolidColorBrush(Color.FromArgb(140, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BestDayBadgeBg = new SolidColorBrush(Color.FromArgb(200, 0x0F, 0x9D, 0x7A));
    private static readonly Brush WorstDayBadgeBg = new SolidColorBrush(Color.FromArgb(200, 0xC4, 0x4B, 0x3B));
    private static readonly Brush PnlBarGreen = new SolidColorBrush(Color.FromArgb(120, 0x0F, 0x9D, 0x7A));
    private static readonly Brush PnlBarRed = new SolidColorBrush(Color.FromArgb(120, 0xC4, 0x4B, 0x3B));
    private static readonly Brush StreakGreenDot = new SolidColorBrush(Color.FromArgb(180, 0x0F, 0x9D, 0x7A));
    private static readonly Brush StreakRedDot = new SolidColorBrush(Color.FromArgb(180, 0xC4, 0x4B, 0x3B));
    private static readonly Pen CellPen = new(new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)), 1);
    private static readonly Pen HoverPen = new(new SolidColorBrush(Color.FromArgb(60, 0x2B, 0x6C, 0xB0)), 1.5);
    private static readonly Pen SelectedPen;
    private static readonly Pen TodayPen;
    private static readonly Pen TooltipBorderPen;
    private static readonly string[] DayHeaders = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    // Hover tracking
    private (int col, int row)? _hoverCell;
    private bool _hoverLeftArrow;
    private bool _hoverRightArrow;

    static PnLCalendarCanvas()
    {
        BgBrush.Freeze();
        GreenBg.Freeze();
        RedBg.Freeze();
        GrayBg.Freeze();
        HoverOverlay.Freeze();
        GreenTextBrush.Freeze();
        RedTextBrush.Freeze();
        SelectedBorderBrush.Freeze();
        DayNumBrush.Freeze();
        DayHeaderBrush.Freeze();
        ArrowBrush.Freeze();
        SubtleBrush.Freeze();
        TooltipBg.Freeze();
        WeekendDim.Freeze();
        TodayDotBrush.Freeze();
        ArrowHoverBrush.Freeze();
        MonthSummaryBg.Freeze();
        DayCountBrush.Freeze();
        BestDayBadgeBg.Freeze();
        WorstDayBadgeBg.Freeze();
        PnlBarGreen.Freeze();
        PnlBarRed.Freeze();
        StreakGreenDot.Freeze();
        StreakRedDot.Freeze();
        CellPen.Freeze();
        HoverPen.Freeze();

        SelectedPen = new Pen(SelectedBorderBrush, 2.5);
        SelectedPen.Freeze();

        TodayPen = new Pen(TodayDotBrush, 1.5) { DashStyle = DashStyles.Dot };
        TodayPen.Freeze();

        TooltipBorderPen = new Pen(new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)), 1);
        TooltipBorderPen.Freeze();

        HeaderBg = new LinearGradientBrush(
            Color.FromRgb(0x14, 0x20, 0x32),
            Color.FromRgb(0x1E, 0x3C, 0x58),
            new Point(0, 0), new Point(1, 0.5));
        HeaderBg.Freeze();
    }

    public PnLCalendarCanvas()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        MouseLeftButtonDown += OnMouseClick;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 100 || h < 100) { _hoverCell = null; return; }

        var headerH = 52.0;
        var dayHeaderH = 28.0;

        // Track arrow hovers
        var newLeft = pos.Y < headerH && pos.X < 48;
        var newRight = pos.Y < headerH && pos.X > w - 48;
        if (newLeft != _hoverLeftArrow || newRight != _hoverRightArrow)
        {
            _hoverLeftArrow = newLeft;
            _hoverRightArrow = newRight;
            InvalidateVisual();
        }

        if (pos.Y < headerH + dayHeaderH)
        {
            if (_hoverCell != null) { _hoverCell = null; InvalidateVisual(); }
            return;
        }

        var summaryBarH = 36.0;
        var cellW = w / 7;
        var gridH = h - headerH - dayHeaderH - summaryBarH;
        var cellH = gridH / 6;
        var col = (int)(pos.X / cellW);
        var row = (int)((pos.Y - headerH - dayHeaderH) / cellH);

        var newHover = (col, row);
        if (_hoverCell != newHover)
        {
            _hoverCell = newHover;
            InvalidateVisual();
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_hoverCell != null)
        {
            _hoverCell = null;
            InvalidateVisual();
        }
    }

    private void OnMouseClick(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 100 || h < 100) return;

        var headerH = 52.0;
        var dayHeaderH = 28.0;

        // Check nav arrows
        if (pos.Y < headerH)
        {
            if (pos.X < 48)
            {
                // Previous month
                if (DisplayMonth == 1) { DisplayMonth = 12; DisplayYear--; }
                else DisplayMonth--;
                InvalidateVisual();
                return;
            }
            if (pos.X > w - 48)
            {
                // Next month
                if (DisplayMonth == 12) { DisplayMonth = 1; DisplayYear++; }
                else DisplayMonth++;
                InvalidateVisual();
                return;
            }
        }

        if (pos.Y < headerH + dayHeaderH) return;

        var summaryBarH = 36.0;
        var cellW = w / 7;
        var gridH = h - headerH - dayHeaderH - summaryBarH;
        var cellH = gridH / 6;
        var col = (int)(pos.X / cellW);
        var row = (int)((pos.Y - headerH - dayHeaderH) / cellH);

        var firstDay = new DateTime(DisplayYear, DisplayMonth, 1);
        var startCol = ((int)firstDay.DayOfWeek + 6) % 7;
        var dayIndex = row * 7 + col - startCol + 1;
        var daysInMonth = DateTime.DaysInMonth(DisplayYear, DisplayMonth);

        if (dayIndex >= 1 && dayIndex <= daysInMonth)
        {
            SelectedDate = new DateOnly(DisplayYear, DisplayMonth, dayIndex);
            RaiseEvent(new RoutedEventArgs(DateSelectedEvent));
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 100 || h < 140) return;

        // Background
        dc.DrawRoundedRectangle(BgBrush, null, new Rect(0, 0, w, h), 8, 8);

        var headerH = 52.0;
        var dayHeaderH = 28.0;
        var summaryBarH = 36.0;
        var cellW = w / 7;
        var gridH = h - headerH - dayHeaderH - summaryBarH;
        var cellH = gridH / 6;

        // Tiered cell typography — enlarged for readability
        var isCompact = cellH < 42;
        var isLarge = cellH >= 60;
        var dayNumSize = isCompact ? 11.0 : isLarge ? 14.0 : 12.5;
        var pnlSize = isCompact ? 12.0 : isLarge ? 16.0 : 14.0;
        var countSize = isCompact ? 8.5 : isLarge ? 11.0 : 10.0;
        var wlSize = countSize;

        // Header with gradient
        dc.DrawRoundedRectangle(HeaderBg, null, new Rect(0, 0, w, headerH), 8, 8);
        // Flatten bottom corners with rect overlay
        dc.DrawRectangle(HeaderBg, null, new Rect(0, headerH - 8, w, 8));

        var monthLabel = new DateTime(DisplayYear, DisplayMonth, 1).ToString("MMMM yyyy").ToUpper();
        var headerText = MakeText(monthLabel, 18, Brushes.White, UiTypeface, FontWeights.Bold);
        dc.DrawText(headerText, new Point(w / 2 - headerText.Width / 2, headerH / 2 - headerText.Height / 2 - 2));

        // Trading day count in header
        var tradingDays = 0;
        var dailySums = DailySummaries;
        if (dailySums is not null)
            foreach (var s in dailySums)
                if (s.Date.Year == DisplayYear && s.Date.Month == DisplayMonth) tradingDays++;
        if (tradingDays > 0)
        {
            var dayCountText = MakeText($"{tradingDays} days", 10, DayCountBrush, MonoTypeface);
            dc.DrawText(dayCountText, new Point(w / 2 - dayCountText.Width / 2, headerH / 2 + headerText.Height / 2 - 1));
        }

        // Nav arrows (with hover highlight and background pill)
        if (_hoverLeftArrow)
            dc.DrawRoundedRectangle(HoverOverlay, null, new Rect(8, headerH / 2 - 14, 32, 28), 6, 6);
        var leftArrow = MakeText("\u25C0", 14, _hoverLeftArrow ? ArrowHoverBrush : ArrowBrush, UiTypeface);
        dc.DrawText(leftArrow, new Point(16, headerH / 2 - leftArrow.Height / 2));

        if (_hoverRightArrow)
            dc.DrawRoundedRectangle(HoverOverlay, null, new Rect(w - 40, headerH / 2 - 14, 32, 28), 6, 6);
        var rightArrow = MakeText("\u25B6", 14, _hoverRightArrow ? ArrowHoverBrush : ArrowBrush, UiTypeface);
        dc.DrawText(rightArrow, new Point(w - 30, headerH / 2 - rightArrow.Height / 2));

        // Day headers with bottom separator
        for (var i = 0; i < 7; i++)
        {
            var headerColor = i >= 5 ? SubtleBrush : DayHeaderBrush;
            var dayHdrText = MakeText(DayHeaders[i], 12, headerColor, UiTypeface, FontWeights.Bold);
            dc.DrawText(dayHdrText, new Point(i * cellW + cellW / 2 - dayHdrText.Width / 2, headerH + 6));
        }
        dc.DrawLine(CellPen, new Point(0, headerH + dayHeaderH - 1), new Point(w, headerH + dayHeaderH - 1));

        // Build lookup
        var lookup = new Dictionary<DateOnly, DailySummary>();
        var summaries = DailySummaries;
        if (summaries is not null)
            foreach (var s in summaries)
                lookup[s.Date] = s;

        // Calendar grid
        var firstDay = new DateTime(DisplayYear, DisplayMonth, 1);
        var startCol = ((int)firstDay.DayOfWeek + 6) % 7;
        var daysInMonth = DateTime.DaysInMonth(DisplayYear, DisplayMonth);

        // Compute max absolute P&L for magnitude bars
        decimal maxAbsPnL = 0;
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(DisplayYear, DisplayMonth, day);
            if (lookup.TryGetValue(date, out var ds) && Math.Abs(ds.PnLDollars) > maxAbsPnL)
                maxAbsPnL = Math.Abs(ds.PnLDollars);
        }

        // Compute streaks per day (consecutive profitable/losing days)
        var streaks = new Dictionary<DateOnly, int>(); // positive = win streak, negative = loss streak
        var currentStreak = 0;
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(DisplayYear, DisplayMonth, day);
            if (lookup.TryGetValue(date, out var ds))
            {
                if (ds.PnLDollars > 0)
                    currentStreak = currentStreak > 0 ? currentStreak + 1 : 1;
                else if (ds.PnLDollars < 0)
                    currentStreak = currentStreak < 0 ? currentStreak - 1 : -1;
                else
                    currentStreak = 0;
                streaks[date] = currentStreak;
            }
        }

        // Best and worst day of the month
        DateOnly? bestDay = null, worstDay = null;
        decimal bestPnL = decimal.MinValue, worstPnL = decimal.MaxValue;
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(DisplayYear, DisplayMonth, day);
            if (lookup.TryGetValue(date, out var ds))
            {
                if (ds.PnLDollars > bestPnL) { bestPnL = ds.PnLDollars; bestDay = date; }
                if (ds.PnLDollars < worstPnL) { worstPnL = ds.PnLDollars; worstDay = date; }
            }
        }

        // Weekend column dimming
        for (var col = 5; col <= 6; col++)
        {
            var wx = col * cellW;
            dc.DrawRectangle(WeekendDim, null, new Rect(wx, headerH + dayHeaderH, cellW, gridH));
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        // Track hovered day for tooltip
        DailySummary? hoveredSummary = null;
        DateOnly? hoveredDate = null;
        Rect hoveredCellRect = default;

        // Monthly totals
        decimal monthPnL = 0;
        var monthTrades = 0;
        var monthWins = 0;
        var monthLosses = 0;

        for (var day = 1; day <= daysInMonth; day++)
        {
            var idx = startCol + day - 1;
            var col = idx % 7;
            var row = idx / 7;
            var x = col * cellW;
            var y = headerH + dayHeaderH + row * cellH;
            var cellRect = new Rect(x + 1, y + 1, cellW - 2, cellH - 2);
            var date = new DateOnly(DisplayYear, DisplayMonth, day);

            // Background coloring with intensity based on P&L magnitude
            if (lookup.TryGetValue(date, out var summary))
            {
                if (maxAbsPnL > 0 && summary.PnLDollars != 0)
                {
                    var intensity = (byte)Math.Clamp((double)(Math.Abs(summary.PnLDollars) / maxAbsPnL) * 55 + 20, 20, 75);
                    var bg = summary.PnLDollars > 0
                        ? new SolidColorBrush(Color.FromArgb(intensity, 0x0F, 0x9D, 0x7A))
                        : new SolidColorBrush(Color.FromArgb(intensity, 0xC4, 0x4B, 0x3B));
                    bg.Freeze();
                    dc.DrawRoundedRectangle(bg, null, cellRect, 5, 5);
                }
                else
                {
                    dc.DrawRoundedRectangle(GrayBg, null, cellRect, 5, 5);
                }
                monthPnL += summary.PnLDollars;
                monthTrades += summary.TradeCount;
                monthWins += summary.Wins;
                monthLosses += summary.Losses;
            }

            // Grid lines
            dc.DrawRoundedRectangle(null, CellPen, cellRect, 5, 5);

            // Today highlight
            if (date == today)
            {
                dc.DrawRoundedRectangle(null, TodayPen, new Rect(x + 2, y + 2, cellW - 4, cellH - 4), 5, 5);
                // Small dot indicator
                dc.DrawEllipse(TodayDotBrush, null, new Point(x + cellW - 9, y + 7), 3, 3);
            }

            // Hover highlight
            if (_hoverCell.HasValue && _hoverCell.Value.col == col && _hoverCell.Value.row == row)
            {
                dc.DrawRoundedRectangle(HoverOverlay, HoverPen, cellRect, 5, 5);
                hoveredDate = date;
                hoveredCellRect = cellRect;
                if (lookup.TryGetValue(date, out var hoverSummary))
                    hoveredSummary = hoverSummary;
            }

            // Selected highlight
            if (SelectedDate.HasValue && SelectedDate.Value == date)
            {
                dc.DrawRoundedRectangle(null, SelectedPen, new Rect(x + 2, y + 2, cellW - 4, cellH - 4), 5, 5);
            }

            // Day number — top-left
            var dayOfWeek = new DateTime(DisplayYear, DisplayMonth, day).DayOfWeek;
            var isWeekday = dayOfWeek != DayOfWeek.Saturday && dayOfWeek != DayOfWeek.Sunday;
            var dayText = MakeText(day.ToString(), dayNumSize, DayNumBrush, UiTypeface, FontWeights.SemiBold);
            dc.DrawText(dayText, new Point(x + 6, y + 4));

            // P&L info
            if (lookup.TryGetValue(date, out var s))
            {
                // Trade count badge — top-right (skip if today dot is there)
                var countX = date == today ? x + cellW - 20 : x + cellW - 6;
                var countText = MakeText($"{s.TradeCount}T", countSize, SubtleBrush, MonoTypeface);
                dc.DrawText(countText, new Point(countX - countText.Width, y + 4));

                // P&L amount — centered
                var pnlColor = s.PnLDollars >= 0 ? GreenTextBrush : RedTextBrush;
                var sign = s.PnLDollars >= 0 ? "+" : "";
                var pnlText = MakeText($"{sign}${s.PnLDollars:N0}", pnlSize, pnlColor, MonoTypeface, FontWeights.Bold);
                dc.DrawText(pnlText, new Point(x + cellW / 2 - pnlText.Width / 2, y + cellH / 2 - pnlText.Height / 2 - 1));

                // P&L magnitude bar — thin bar between P&L text and W-L (skip when compact)
                if (!isCompact && maxAbsPnL > 0)
                {
                    var barMaxW = cellW * 0.65;
                    var barW = (double)(Math.Abs(s.PnLDollars) / maxAbsPnL) * barMaxW;
                    if (barW > 2)
                    {
                        var barH = 4.0;
                        var barX = x + cellW / 2 - barW / 2;
                        var barY = y + cellH / 2 + 12;
                        var barBrush = s.PnLDollars >= 0 ? PnlBarGreen : PnlBarRed;
                        dc.DrawRoundedRectangle(barBrush, null, new Rect(barX, barY, barW, barH), 2, 2);
                    }
                }

                // W-L — bottom
                var wlText = MakeText($"{s.Wins}W {s.Losses}L", wlSize, SubtleBrush, MonoTypeface);
                dc.DrawText(wlText, new Point(x + cellW / 2 - wlText.Width / 2, y + cellH - wlText.Height - 4));

                // Streak dots — small dots at bottom showing streak length (skip when compact)
                if (!isCompact && streaks.TryGetValue(date, out var streak) && Math.Abs(streak) >= 2)
                {
                    var dotCount = Math.Min(Math.Abs(streak), 5);
                    var dotR = 2.5;
                    var dotSpacing = 7.0;
                    var dotsW = dotCount * dotSpacing - (dotSpacing - dotR * 2);
                    var dotStartX = x + cellW / 2 - dotsW / 2;
                    var dotY = y + cellH - 4;
                    var dotBrush = streak > 0 ? StreakGreenDot : StreakRedDot;
                    for (var d = 0; d < dotCount; d++)
                        dc.DrawEllipse(dotBrush, null, new Point(dotStartX + d * dotSpacing + dotR, dotY), dotR, dotR);
                }

                // Best/worst day badge — star at top-right corner
                if (bestDay.HasValue && date == bestDay.Value && bestPnL > 0)
                {
                    var badge = MakeText("\u2605", 10, Brushes.White, UiTypeface);
                    dc.DrawRoundedRectangle(BestDayBadgeBg, null,
                        new Rect(x + cellW - 18, y + 2, 15, 15), 4, 4);
                    dc.DrawText(badge, new Point(x + cellW - 16, y + 2));
                }
                else if (worstDay.HasValue && date == worstDay.Value && worstPnL < 0)
                {
                    var badge = MakeText("\u2605", 10, Brushes.White, UiTypeface);
                    dc.DrawRoundedRectangle(WorstDayBadgeBg, null,
                        new Rect(x + cellW - 18, y + 2, 15, 15), 4, 4);
                    dc.DrawText(badge, new Point(x + cellW - 16, y + 2));
                }
            }
            else if (isWeekday && date < today)
            {
                // Empty weekday (no trades) — subtle dash indicator
                var noTradeFt = MakeText("—", 10, SubtleBrush, UiTypeface);
                dc.DrawText(noTradeFt, new Point(x + cellW / 2 - noTradeFt.Width / 2, y + cellH / 2 - noTradeFt.Height / 2));
            }
        }

        // Monthly summary bar at bottom (positioned below the 6-row grid)
        if (monthTrades > 0)
        {
            var summaryY = headerH + dayHeaderH + gridH + 2;
            var barH = summaryBarH - 4;
            dc.DrawRoundedRectangle(MonthSummaryBg, null, new Rect(4, summaryY, w - 8, barH), 5, 5);

            // Left accent bar (green/red)
            var accentBrush = monthPnL >= 0 ? GreenTextBrush : RedTextBrush;
            dc.DrawRoundedRectangle(accentBrush, null, new Rect(4, summaryY, 5, barH), 3, 3);

            // P&L in bold color
            var monthSign = monthPnL >= 0 ? "+" : "";
            var monthColor = monthPnL >= 0 ? GreenTextBrush : RedTextBrush;
            var pnlPart = MakeText($"Month: {monthSign}${monthPnL:N0}", 11.5, monthColor, MonoTypeface, FontWeights.Bold);

            // Stats in subtle gray
            var monthWinRate = monthTrades > 0 ? (double)monthWins / monthTrades * 100 : 0;
            var avgTrade = monthTrades > 0 ? monthPnL / monthTrades : 0;
            var avgSign = avgTrade >= 0 ? "+" : "";
            var statsPart = MakeText(
                $"   |   {monthTrades} trades   |   {monthWinRate:F0}% WR   |   Avg: {avgSign}${avgTrade:N0}",
                11, SubtleBrush, MonoTypeface);

            var totalW = pnlPart.Width + statsPart.Width;
            var startX = w / 2 - totalW / 2;
            var textY = summaryY + barH / 2 - pnlPart.Height / 2;
            dc.DrawText(pnlPart, new Point(startX, textY));
            dc.DrawText(statsPart, new Point(startX + pnlPart.Width, textY));
        }

        // Draw hover tooltip
        if (hoveredDate.HasValue && hoveredSummary != null)
        {
            var s = hoveredSummary;
            var winRate = s.TradeCount > 0 ? (double)s.Wins / s.TradeCount * 100 : 0;
            var sign = s.PnLDollars >= 0 ? "+" : "";
            var streakLabel = "";
            if (streaks.TryGetValue(hoveredDate.Value, out var hStreak) && Math.Abs(hStreak) >= 2)
                streakLabel = $"\n{Math.Abs(hStreak)}-day {(hStreak > 0 ? "win" : "loss")} streak";

            // Best/worst day label
            var rankLabel = "";
            if (bestDay.HasValue && hoveredDate.Value == bestDay.Value && bestPnL > 0)
                rankLabel = "  \u2605 Best Day";
            else if (worstDay.HasValue && hoveredDate.Value == worstDay.Value && worstPnL < 0)
                rankLabel = "  \u2605 Worst Day";

            var tooltipLines = $"{hoveredDate.Value:ddd, MMM dd yyyy}{rankLabel}\n{sign}${s.PnLDollars:N0}  |  {s.TradeCount} trades  |  {winRate:F0}% win{streakLabel}";
            var tooltipFt = MakeText(tooltipLines, 12, Brushes.White, MonoTypeface);

            var tooltipW = tooltipFt.Width + 20;
            var tooltipH = tooltipFt.Height + 12;
            var tooltipX = hoveredCellRect.X + hoveredCellRect.Width / 2 - tooltipW / 2;
            var tooltipY = hoveredCellRect.Y - tooltipH - 4;

            // Keep tooltip within bounds
            if (tooltipX < 2) tooltipX = 2;
            if (tooltipX + tooltipW > w - 2) tooltipX = w - tooltipW - 2;
            if (tooltipY < 0) tooltipY = hoveredCellRect.Bottom + 4;

            dc.DrawRoundedRectangle(TooltipBg, TooltipBorderPen,
                new Rect(tooltipX, tooltipY, tooltipW, tooltipH), 6, 6);
            dc.DrawText(tooltipFt, new Point(tooltipX + 8, tooltipY + 5));
        }
    }

    private FormattedText MakeText(string text, double size, Brush brush, Typeface typeface,
        FontWeight? weight = null) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            weight.HasValue ? new Typeface(typeface.FontFamily, FontStyles.Normal, weight.Value, FontStretches.Normal) : typeface,
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
