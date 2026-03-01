using System.Windows.Input;

namespace SwingDayTradingPlatform.UI.Wpf.Infrastructure;

public sealed class RelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null) : ICommand
{
    private volatile bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;
        _isExecuting = true;
        NotifyCanExecuteChanged();
        try
        {
            await execute(parameter);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RelayCommand error: {ex}");
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
