namespace SwingDayTradingPlatform.AlertService;

/// <summary>
/// Incremental EMA calculator. Call Add() for each new close price.
/// Tracks current and previous EMA values for direction computation.
/// </summary>
public sealed class EmaCalculator
{
    private readonly decimal _multiplier;
    private decimal _currentEma;
    private int _count;

    public EmaCalculator(int period)
    {
        Period = period;
        _multiplier = 2m / (period + 1);
    }

    public int Period { get; }
    public decimal CurrentEma => _currentEma;
    public int Count => _count;

    /// <summary>
    /// Adds a new close price and returns the updated EMA value.
    /// </summary>
    public decimal Add(decimal close)
    {
        if (_count == 0)
            _currentEma = close;
        else
            _currentEma = ((close - _currentEma) * _multiplier) + _currentEma;

        _count++;
        return _currentEma;
    }
}
