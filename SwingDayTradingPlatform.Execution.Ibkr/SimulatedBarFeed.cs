using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Execution.Ibkr;

public sealed class SimulatedBarFeed : IBarFeed
{
    private readonly SimulationConfig _config;
    private readonly TradingConfig _trading;
    private readonly System.Timers.Timer _timer;
    private readonly Random _random = new();
    private decimal _lastPrice;
    private DateTimeOffset _cursorUtc;

    public SimulatedBarFeed(SimulationConfig config, TradingConfig trading)
    {
        _config = config;
        _trading = trading;
        _lastPrice = config.StartingPrice;
        _cursorUtc = DateTimeOffset.UtcNow.AddMinutes(-30);
        _timer = new System.Timers.Timer(Math.Max(500, config.TickSeconds * 1000));
        _timer.Elapsed += OnElapsed;
    }

    public event EventHandler<MarketBar>? BarClosed;
    public bool IsRunning { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        IsRunning = true;
        _timer.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        IsRunning = false;
        _timer.Stop();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _timer.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        var open = _lastPrice;
        var drift = (decimal)(_random.NextDouble() - 0.5) * _config.BarVolatility;
        var close = Math.Max(100m, open + drift);
        var high = Math.Max(open, close) + (decimal)_random.NextDouble() * (_config.BarVolatility * 0.4m);
        var low = Math.Min(open, close) - (decimal)_random.NextDouble() * (_config.BarVolatility * 0.4m);
        var volume = 100 + _random.Next(900);
        var bar = new MarketBar(_cursorUtc, _cursorUtc.AddMinutes(5), open, high, low, close, volume);
        _cursorUtc = _cursorUtc.AddMinutes(5);
        _lastPrice = close;
        BarClosed?.Invoke(this, bar);
    }
}
