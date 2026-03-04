using System.Windows;
using SwingDayTradingPlatform.UI.Wpf.ViewModels;

namespace SwingDayTradingPlatform.UI.Wpf;

public partial class AlertsDayDetailWindow : Window
{
    public AlertsDayDetailWindow(AlertsDayChartViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        TxtDateDisplay.Text = viewModel.DateDisplay;
        TxtAlertCount.Text = $"{viewModel.AlertCount} alerts";
        Title = $"Alerts Day Chart \u2014 {viewModel.DateDisplay}";

        // Set chart data after layout so ActualWidth/Height are valid
        Loaded += (_, _) =>
        {
            DayChart.Bars = viewModel.DayBars;
            DayChart.FastEmaValues = viewModel.EmaValues;
            DayChart.TradeMarkers = viewModel.TradeMarkers;
        };
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
