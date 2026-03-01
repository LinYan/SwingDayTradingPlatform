using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SwingDayTradingPlatform.Backtesting;
using SwingDayTradingPlatform.UI.Wpf.ViewModels;

namespace SwingDayTradingPlatform.UI.Wpf;

public partial class DayDetailWindow : Window
{
    private static readonly Brush PositiveBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0x94));
    private static readonly Brush NegativeBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x70, 0x55));
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

    // P&L column index in the DataGrid (0-based: Dir, EntryTime, ExitTime, Entry$, Exit$, PnLPts, PnL$)
    private const int PnlDollarColIndex = 6;

    static DayDetailWindow()
    {
        PositiveBrush.Freeze();
        NegativeBrush.Freeze();
        NeutralBrush.Freeze();
    }

    public DayDetailWindow(DayDetailViewModel viewModel, string strategyName)
    {
        InitializeComponent();
        DataContext = viewModel;

        TxtDateDisplay.Text = viewModel.SelectedDateDisplay;
        TxtStrategyName.Text = strategyName;
        Title = $"Day Detail \u2014 {strategyName}";

        TradesGrid.ItemsSource = viewModel.DayTrades;
        TradesGrid.LoadingRow += OnTradesGridLoadingRow;

        var trades = viewModel.DayTrades;
        int count = trades.Count;
        int wins = trades.Count(t => t.PnLDollars > 0);
        decimal totalPnl = trades.Sum(t => t.PnLDollars);
        decimal avgTrade = count > 0 ? totalPnl / count : 0;
        double winRate = count > 0 ? (double)wins / count * 100 : 0;

        // Best and worst trade
        decimal bestTrade = 0, worstTrade = 0;
        foreach (var t in trades)
        {
            if (t.PnLDollars > bestTrade) bestTrade = t.PnLDollars;
            if (t.PnLDollars < worstTrade) worstTrade = t.PnLDollars;
        }

        // Max intraday drawdown
        decimal peak = 0, maxDD = 0, running = 0;
        foreach (var t in trades)
        {
            running += t.PnLDollars;
            if (running > peak) peak = running;
            var dd = peak - running;
            if (dd > maxDD) maxDD = dd;
        }

        TxtTradeCount.Text = count.ToString();
        TxtTradeCountGrid.Text = $"{count} trades  |  {wins}W {count - wins}L  |  Best: +${bestTrade:N0}  Worst: ${worstTrade:N0}";

        TxtTotalPnl.Text = $"{(totalPnl >= 0 ? "+" : "")}${totalPnl:N2}";
        TxtTotalPnl.Foreground = totalPnl >= 0 ? PositiveBrush : NegativeBrush;

        TxtWinRate.Text = $"{winRate:F0}%";
        TxtWinRate.Foreground = winRate >= 50 ? PositiveBrush : NegativeBrush;

        TxtAvgTrade.Text = $"{(avgTrade >= 0 ? "+" : "")}${avgTrade:N2}";
        TxtAvgTrade.Foreground = avgTrade >= 0 ? PositiveBrush : NegativeBrush;

        TxtMaxDD.Text = maxDD == 0 ? "$0.00" : $"-${maxDD:N2}";
    }

    private void OnTradesGridLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is BacktestTrade trade)
        {
            // Color the entire row subtly based on P&L
            if (trade.PnLDollars > 0)
                e.Row.Background = new SolidColorBrush(Color.FromArgb(15, 0x00, 0xB8, 0x94));
            else if (trade.PnLDollars < 0)
                e.Row.Background = new SolidColorBrush(Color.FromArgb(15, 0xE1, 0x70, 0x55));
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
