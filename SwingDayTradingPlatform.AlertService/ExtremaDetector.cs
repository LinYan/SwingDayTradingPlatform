namespace SwingDayTradingPlatform.AlertService;

/// <summary>
/// Detects local peaks and valleys based on close prices over a window of bars.
///
/// With lookback = N, uses N bars on each side of the pivot (window = 2N + 1).
/// The pivot bar is confirmed once all N bars after it have closed.
///
/// MAX (peak):   pivot close is strictly higher than all N bars before AND all N bars after.
/// MIN (valley): pivot close is strictly lower  than all N bars before AND all N bars after.
/// </summary>
public sealed class ExtremaDetector : IExtremaDetector
{
    private readonly int _halfWindow;
    private readonly string _symbol;
    private readonly EmaCalculator _ema;
    private readonly List<Bar> _bars = [];
    private readonly List<decimal> _emaValues = [];

    public ExtremaDetector(int lookback, int emaPeriod, string symbol)
    {
        // lookback = number of bars on each side of the pivot
        _halfWindow = Math.Max(1, lookback / 2);
        _symbol = symbol;
        _ema = new EmaCalculator(emaPeriod);
    }

    public event Action<Alert>? AlertDetected;

    public int BarCount => _bars.Count;

    public void OnBar(Bar bar)
    {
        var emaValue = _ema.Add(bar.Close);
        _bars.Add(bar);
        _emaValues.Add(emaValue);

        // Need at least 2*halfWindow + 1 bars for a full window
        var windowSize = 2 * _halfWindow + 1;
        if (_bars.Count < windowSize)
            return;

        // The pivot is the middle bar of the most recent window
        var pivotIdx = _bars.Count - 1 - _halfWindow;
        var pivotClose = _bars[pivotIdx].Close;

        // Check MAX: pivot close > all neighbors in the window
        var isMax = true;
        var isMin = true;
        for (var i = pivotIdx - _halfWindow; i <= pivotIdx + _halfWindow; i++)
        {
            if (i == pivotIdx) continue;
            if (_bars[i].Close >= pivotClose) isMax = false;
            if (_bars[i].Close <= pivotClose) isMin = false;
            if (!isMax && !isMin) return;
        }

        if (isMax)
            EmitAlert(ExtremaType.MAX, pivotIdx);
        else if (isMin)
            EmitAlert(ExtremaType.MIN, pivotIdx);
    }

    private void EmitAlert(ExtremaType type, int pivotIdx)
    {
        var confirmIdx = _bars.Count - 1; // latest bar that completed the window

        var emaDir = pivotIdx >= 1
            ? (_emaValues[pivotIdx] > _emaValues[pivotIdx - 1] ? EmaDirection.UP
                : _emaValues[pivotIdx] < _emaValues[pivotIdx - 1] ? EmaDirection.DOWN
                : EmaDirection.FLAT)
            : EmaDirection.NA;

        var alert = new Alert(
            EventTime: _bars[confirmIdx].Timestamp,
            PivotTime: _bars[pivotIdx].Timestamp,
            Type: type,
            Symbol: _symbol,
            Close: _bars[pivotIdx].Close,
            High: _bars[pivotIdx].High,
            Low: _bars[pivotIdx].Low,
            EmaPeriod: _ema.Period,
            EmaValue: Math.Round(_emaValues[pivotIdx], 2),
            EmaDir: emaDir,
            Mode: "close");

        AlertDetected?.Invoke(alert);
    }
}
