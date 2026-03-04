using System.Windows;
using System.Windows.Controls;
using SwingDayTradingPlatform.UI.Wpf.Controls;
using SwingDayTradingPlatform.UI.Wpf.ViewModels;

namespace SwingDayTradingPlatform.UI.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isClosing)
        {
            _isClosing = true;
            e.Cancel = true;
            try
            {
                await _viewModel.DisposeAsync();
            }
            catch
            {
                // Swallow disposal errors during shutdown
            }
            _ = Dispatcher.InvokeAsync(Close);
        }
    }

    private void OnAlertModeReplay(object sender, RoutedEventArgs e) => _viewModel.Alerts.Mode = "replay";
    private void OnAlertModeRealtime(object sender, RoutedEventArgs e) => _viewModel.Alerts.Mode = "realtime";

    private void OnCalendarDateSelected1(object sender, RoutedEventArgs e) => OpenDayDetail(sender, "EmaPullback", "EMA Pullback");
    private void OnCalendarDateSelected9(object sender, RoutedEventArgs e) => OpenDayDetail(sender, "BrooksPA", "Brooks PA");
    private void OnCalendarDateSelected12(object sender, RoutedEventArgs e) => OpenDayDetail(sender, "SlopeInflection", "Slope Inflection");

    private void OnAlertDateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not DateOnly date)
            return;

        var vm = _viewModel.Alerts.OpenDayChart(date);
        if (vm is null) return;

        var window = new AlertsDayDetailWindow(vm) { Owner = this };
        window.Show();

        lb.SelectedItem = null; // allow re-selection of same date
    }

    private void OpenDayDetail(object sender, string strategyKey, string displayName)
    {
        if (sender is not PnLCalendarCanvas cal || cal.SelectedDate is null)
            return;

        _viewModel.Backtest.OnCalendarDateSelected(strategyKey, cal.SelectedDate);

        var window = new DayDetailWindow(_viewModel.Backtest.DayDetail, displayName)
        {
            Owner = this
        };
        window.Show();
    }
}
