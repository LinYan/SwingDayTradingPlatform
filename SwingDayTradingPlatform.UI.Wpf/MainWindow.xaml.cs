using System.Windows;
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

    private void OnCalendarDateSelected1(object sender, RoutedEventArgs e) => OpenDayDetail(sender, "EmaPullback", "EMA Pullback");
    private void OnCalendarDateSelected5(object sender, RoutedEventArgs e) => OpenDayDetail(sender, "EmaPullbackBarBreak", "EMA Bar Break");
    private void OnCalendarDateSelected7(object sender, RoutedEventArgs e) => OpenDayDetail(sender, "SecondLeg", "Second Leg");
    private void OnCalendarDateSelected9(object sender, RoutedEventArgs e) => OpenDayDetail(sender, "BrooksPA", "Brooks PA");

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
