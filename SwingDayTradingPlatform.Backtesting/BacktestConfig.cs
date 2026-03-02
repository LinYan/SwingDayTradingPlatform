namespace SwingDayTradingPlatform.Backtesting;

public sealed class BacktestConfig
{
    public DateOnly StartDate { get; init; } = new(2016, 1, 1);
    public DateOnly EndDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal StartingCapital { get; init; } = 25_000m;
    public decimal CommissionPerTrade { get; init; } = 0m;
    public decimal SlippagePoints { get; init; } = 0m;
    public decimal PointValue { get; init; } = 50m;
    public string Timezone { get; init; } = "America/New_York";
    public string EntryWindowStart { get; init; } = "09:40";
    public string EntryWindowEnd { get; init; } = "15:50";
    public string FlattenTime { get; init; } = "15:55";
    public string CsvPath { get; init; } = "data/historical/ES_5min_RTH.csv";
    public decimal MaxDailyLossPoints { get; init; } = 20m;
    public string DbPath { get; init; } = "data/es_bars.db";
    public DateOnly? InSampleCutoff { get; init; }

    public List<string> Validate()
    {
        var errors = new List<string>();

        if (StartDate > EndDate)
            errors.Add($"StartDate ({StartDate}) must be <= EndDate ({EndDate})");
        if (StartingCapital <= 0)
            errors.Add($"StartingCapital must be > 0, got {StartingCapital}");
        if (PointValue <= 0)
            errors.Add($"PointValue must be > 0, got {PointValue}");
        if (CommissionPerTrade < 0)
            errors.Add($"CommissionPerTrade must be >= 0, got {CommissionPerTrade}");
        if (SlippagePoints < 0)
            errors.Add($"SlippagePoints must be >= 0, got {SlippagePoints}");
        if (MaxDailyLossPoints <= 0)
            errors.Add($"MaxDailyLossPoints must be > 0, got {MaxDailyLossPoints}");

        if (TimeSpan.TryParse(EntryWindowStart, out var start) &&
            TimeSpan.TryParse(EntryWindowEnd, out var end) &&
            TimeSpan.TryParse(FlattenTime, out var flatten))
        {
            if (start >= end)
                errors.Add($"EntryWindowStart ({EntryWindowStart}) must be before EntryWindowEnd ({EntryWindowEnd})");
            if (end > flatten)
                errors.Add($"EntryWindowEnd ({EntryWindowEnd}) must be <= FlattenTime ({FlattenTime})");
        }
        else
        {
            if (!TimeSpan.TryParse(EntryWindowStart, out _))
                errors.Add($"Invalid EntryWindowStart format: '{EntryWindowStart}' (expected HH:mm)");
            if (!TimeSpan.TryParse(EntryWindowEnd, out _))
                errors.Add($"Invalid EntryWindowEnd format: '{EntryWindowEnd}' (expected HH:mm)");
            if (!TimeSpan.TryParse(FlattenTime, out _))
                errors.Add($"Invalid FlattenTime format: '{FlattenTime}' (expected HH:mm)");
        }

        if (InSampleCutoff.HasValue && (InSampleCutoff.Value < StartDate || InSampleCutoff.Value > EndDate))
            errors.Add($"InSampleCutoff ({InSampleCutoff.Value}) must be between StartDate and EndDate");

        return errors;
    }
}
