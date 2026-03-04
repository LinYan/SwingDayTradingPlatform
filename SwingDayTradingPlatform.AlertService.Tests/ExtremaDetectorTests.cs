using SwingDayTradingPlatform.AlertService;

namespace SwingDayTradingPlatform.AlertService.Tests;

public class ExtremaDetectorTests
{
    private static Bar MakeBar(int minuteOffset, decimal close, decimal? high = null, decimal? low = null)
    {
        return new Bar(
            new DateTime(2024, 1, 2, 9, 30, 0).AddMinutes(minuteOffset * 5),
            close - 1m,
            high ?? close + 0.5m,
            low ?? close - 0.5m,
            close);
    }

    [Fact]
    public void DetectsPeak_WithLookback5()
    {
        // lookback=5 => halfWindow=2 => window=5 bars
        // pivot at center must be higher than 2 bars before and 2 bars after
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        // Closes: 100, 102, 105, 103, 101 => peak at index 2 (close=105)
        var bars = new[]
        {
            MakeBar(0, 100m),
            MakeBar(1, 102m),
            MakeBar(2, 105m),  // pivot (peak)
            MakeBar(3, 103m),
            MakeBar(4, 101m),  // completes window
        };

        foreach (var bar in bars)
            detector.OnBar(bar);

        Assert.Single(alerts);
        Assert.Equal(ExtremaType.MAX, alerts[0].Type);
        Assert.Equal(105m, alerts[0].Close);
        Assert.Equal(bars[2].Timestamp, alerts[0].PivotTime);
        Assert.Equal(bars[4].Timestamp, alerts[0].EventTime);
    }

    [Fact]
    public void DetectsValley_WithLookback5()
    {
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        // Closes: 110, 108, 105, 107, 109 => valley at index 2 (close=105)
        var bars = new[]
        {
            MakeBar(0, 110m),
            MakeBar(1, 108m),
            MakeBar(2, 105m),  // pivot (valley)
            MakeBar(3, 107m),
            MakeBar(4, 109m),  // completes window
        };

        foreach (var bar in bars)
            detector.OnBar(bar);

        Assert.Single(alerts);
        Assert.Equal(ExtremaType.MIN, alerts[0].Type);
        Assert.Equal(105m, alerts[0].Close);
        Assert.Equal(bars[2].Timestamp, alerts[0].PivotTime);
        Assert.Equal(bars[4].Timestamp, alerts[0].EventTime);
    }

    [Fact]
    public void NoAlert_WhenNotEnoughBars()
    {
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        // Only 4 bars, need 5 (2*halfWindow+1)
        for (var i = 0; i < 4; i++)
            detector.OnBar(MakeBar(i, 100m + i));

        Assert.Empty(alerts);
    }

    [Fact]
    public void NoAlert_WhenPivotNotHighestOrLowest()
    {
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        // Closes: 100, 101, 103, 104, 102 => 103 is NOT the highest (104 is)
        var bars = new[]
        {
            MakeBar(0, 100m),
            MakeBar(1, 101m),
            MakeBar(2, 103m),
            MakeBar(3, 104m),
            MakeBar(4, 102m),
        };

        foreach (var bar in bars)
            detector.OnBar(bar);

        Assert.Empty(alerts);
    }

    [Fact]
    public void NoAlert_WhenEqualToNeighbor()
    {
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        // Closes: 100, 105, 105, 103, 101 => pivot equals left neighbor
        var bars = new[]
        {
            MakeBar(0, 100m),
            MakeBar(1, 105m),
            MakeBar(2, 105m),  // equal to bar 1, not strictly higher
            MakeBar(3, 103m),
            MakeBar(4, 101m),
        };

        foreach (var bar in bars)
            detector.OnBar(bar);

        Assert.Empty(alerts);
    }

    [Fact]
    public void DetectsMultipleAlerts_InSequence()
    {
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        // Peak at index 2, valley at index 5, peak at index 8
        decimal[] closes = [100, 102, 106, 103, 101, 98, 100, 103, 107, 104, 102];
        for (var i = 0; i < closes.Length; i++)
            detector.OnBar(MakeBar(i, closes[i]));

        Assert.Equal(3, alerts.Count);
        Assert.Equal(ExtremaType.MAX, alerts[0].Type);
        Assert.Equal(106m, alerts[0].Close);
        Assert.Equal(ExtremaType.MIN, alerts[1].Type);
        Assert.Equal(98m, alerts[1].Close);
        Assert.Equal(ExtremaType.MAX, alerts[2].Type);
        Assert.Equal(107m, alerts[2].Close);
    }

    [Fact]
    public void EmaDirection_UP_WhenEmaIncreasing()
    {
        var detector = new ExtremaDetector(5, 3, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        // Rising prices => EMA should be going UP at pivot
        decimal[] closes = [100, 102, 108, 103, 101];
        for (var i = 0; i < closes.Length; i++)
            detector.OnBar(MakeBar(i, closes[i]));

        Assert.Single(alerts);
        Assert.Equal(EmaDirection.UP, alerts[0].EmaDir);
    }

    [Fact]
    public void EmaDirection_DOWN_WhenEmaDecreasing()
    {
        var detector = new ExtremaDetector(5, 3, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        // Falling prices => EMA should be going DOWN at pivot
        decimal[] closes = [110, 108, 104, 107, 109];
        for (var i = 0; i < closes.Length; i++)
            detector.OnBar(MakeBar(i, closes[i]));

        Assert.Single(alerts);
        Assert.Equal(EmaDirection.DOWN, alerts[0].EmaDir);
    }

    [Fact]
    public void Alert_ContainsCorrectSymbol()
    {
        var detector = new ExtremaDetector(5, 20, "ES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        decimal[] closes = [100, 102, 106, 103, 101];
        for (var i = 0; i < closes.Length; i++)
            detector.OnBar(MakeBar(i, closes[i]));

        Assert.Single(alerts);
        Assert.Equal("ES", alerts[0].Symbol);
    }

    [Fact]
    public void Alert_ContainsCorrectHighLow()
    {
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        var bars = new[]
        {
            MakeBar(0, 100m),
            MakeBar(1, 102m),
            MakeBar(2, 106m, high: 107.5m, low: 105.25m),  // pivot
            MakeBar(3, 103m),
            MakeBar(4, 101m),
        };

        foreach (var bar in bars)
            detector.OnBar(bar);

        Assert.Single(alerts);
        Assert.Equal(107.5m, alerts[0].High);
        Assert.Equal(105.25m, alerts[0].Low);
    }

    [Fact]
    public void Alert_ModeIsClose()
    {
        var detector = new ExtremaDetector(5, 20, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        decimal[] closes = [100, 102, 106, 103, 101];
        for (var i = 0; i < closes.Length; i++)
            detector.OnBar(MakeBar(i, closes[i]));

        Assert.Single(alerts);
        Assert.Equal("close", alerts[0].Mode);
    }

    [Fact]
    public void BarCount_TracksProcessedBars()
    {
        var detector = new ExtremaDetector(5, 20, "MES");
        Assert.Equal(0, detector.BarCount);

        detector.OnBar(MakeBar(0, 100m));
        Assert.Equal(1, detector.BarCount);

        detector.OnBar(MakeBar(1, 101m));
        Assert.Equal(2, detector.BarCount);
    }

    [Fact]
    public void DetectsPeak_WithLookback3()
    {
        // lookback=3 => halfWindow=1 => window=3 bars
        var detector = new ExtremaDetector(3, 20, "MES");
        var alerts = new List<Alert>();
        detector.AlertDetected += a => alerts.Add(a);

        // Closes: 100, 105, 102 => peak at index 1
        var bars = new[]
        {
            MakeBar(0, 100m),
            MakeBar(1, 105m),  // pivot (peak)
            MakeBar(2, 102m),  // completes window
        };

        foreach (var bar in bars)
            detector.OnBar(bar);

        Assert.Single(alerts);
        Assert.Equal(ExtremaType.MAX, alerts[0].Type);
        Assert.Equal(105m, alerts[0].Close);
    }
}
