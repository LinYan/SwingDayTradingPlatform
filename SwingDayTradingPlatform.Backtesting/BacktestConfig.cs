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
}
