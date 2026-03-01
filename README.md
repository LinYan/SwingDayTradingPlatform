# Swing Day Trading Platform

WPF MVVM desktop application for automated MES (Micro E-mini S&P 500) intraday swing trading on Windows, with Interactive Brokers integration and a full backtesting engine.

## Design constraints

This platform is intentionally narrow:

- MES futures only, 1 contract
- 5-minute bars, regular trading hours only
- Pacific time scheduling (entry 06:30-12:50 PT, flatten 12:55 PT)
- Max 5 trades per day, stop after 3 losers
- Reject setups needing more than a 10-point stop
- Flat by end of session every day

## Solution structure

```
SwingDayTradingPlatform.slnx
  SwingDayTradingPlatform.Shared          Core models, enums, interfaces
  SwingDayTradingPlatform.Strategy        Multi-strategy engine, indicators, pattern detection
  SwingDayTradingPlatform.Backtesting     Backtest engine, parameter sweep, result metrics
  SwingDayTradingPlatform.Execution.Ibkr  IBKR broker integration (real + simulated feeds)
  SwingDayTradingPlatform.Risk            Risk engine (daily caps, loss limits, cooldown)
  SwingDayTradingPlatform.Storage         JSON-based order/execution state persistence
  SwingDayTradingPlatform.UI.Wpf          WPF desktop dashboard
  SwingDayTradingPlatform.Tests           xUnit test suite
```

## Trading strategies

All three strategies run on 5-minute bars. They are evaluated in priority order on each bar close; first match wins.

### Strategy 1: EMA Pullback

Trend-following pullback entry after swing confirmation.

- **Long:** EMA20 > EMA50, close >= VWAP, higher-low pattern near EMA20
- **Short:** EMA20 < EMA50, close <= VWAP, lower-high pattern near EMA20
- Filters: EMA slope, RSI range, body size (ATR-based)
- **Exit:** ATR trailing stop + configurable R:R target (default 2.0x)

### Strategy 9: Brooks Price Action

Al Brooks-style price action patterns in the trend direction.

- Determines "always-in" direction from EMA20/EMA50 alignment
- **ii Breakout:** Two consecutive inside bars forming compression, breakout in trend direction
- **H2/L2 Pullback:** Two-legged pullback near EMA20 with signal bar closing in trend direction
- **Exit:** Measured-move target or R:R target (whichever is more conservative), stop one tick beyond signal bar

### Strategy 12: Slope Inflection

Detects EMA slope inflection points (trend turns) to capture large trend moves.

- **Long:** EMA slope crosses from negative/flat to positive
- **Short:** EMA slope crosses from positive/flat to negative
- Configurable smoothing period, slope epsilon, and cooldown bars
- **Exit:** Bar-by-bar trailing stop using previous bar's open (no fixed target — designed to ride extended moves)

### Shared exit mechanisms

All strategies share these exit checks (evaluated in order):

1. **ATR trailing stop** - activates after 3 bars, trails at 2.0x ATR from swing extreme
2. **Bar-break exit** (optional, `UseBarBreakExit`) - exits when bar breaks prior bar's low/high
3. **Reversal bar exit** (optional, `UseReversalBarExit`) - exits on first bar that closes opposite to trade direction
4. **Target hit** - R:R-based profit target
5. **Session flatten** - forced exit at 12:55 PT

### Hourly bias filter

Optional filter restricts entries based on 1-hour range percentile:

- Top 75% of range = short-only
- Bottom 25% of range = long-only
- Middle = both directions allowed

## UI Dashboard

The WPF desktop dashboard includes per-strategy tabs with:

- **Stat cards** — Net P&L, win rate, Sharpe, max drawdown, profit factor, trade count
- **Equity curve** — Bezier-smoothed line with drawdown shading, high watermark, and crosshair tooltip
- **Daily P&L bar chart** — Green/red vertical bars for every trading day across the full backtest period, with hover tooltips and summary overlay (total P&L, win rate, trading days)
- **P&L calendar** — Month-by-month calendar heatmap with per-day detail, color intensity by magnitude, and click-to-drill-down
- **Monthly returns table** — Tabular P&L and return % by month

## Backtesting

The backtesting engine simulates full order lifecycle with realistic fills:

- Entry slippage modeling (market order fill degradation)
- Stop/target fill simulation on each bar
- Commission: $5 per trade leg
- Daily reset of trade count, loss count, VWAP
- Forced session flatten at configured time

### Backtest metrics

- Net/Gross P&L, return %, win rate, profit factor
- Sharpe ratio, Sortino ratio
- Max drawdown (absolute and percentage)
- Per-trade MAE/MFE, hold time, strategy attribution
- Daily/monthly return aggregation
- Full equity curve with drawdown tracking

### Running a backtest

Configure parameters in `BacktestParameters` (EMA periods, ATR settings, strategy toggles, risk limits) and feed bar data from CSV (`CsvBarStorage`) or SQLite (`SqliteBarStore`).

## Indicators

All computed incrementally (not recomputed from full history each bar):

- **EMA** (20/50 period) - running multiplier state
- **ATR** (14 period) - true range average
- **Session VWAP** - cumulative price*volume / volume, resets daily

## Risk management

- **Daily trade cap:** max 5 trades/day
- **Loss limit:** kill switch after 3 consecutive losers
- **Daily loss limit:** max $1,000 daily loss
- **Cooldown:** 60-second minimum between entries
- **Stop validation:** rejects stops > 10 points
- **Contract sizing:** fixed 1 contract (risk-based mode available)

## Build

```powershell
pwsh ./build.ps1
```

Or directly:

```bash
dotnet build SwingDayTradingPlatform.slnx
```

## Run

```powershell
pwsh ./build.ps1
dotnet run --project SwingDayTradingPlatform.UI.Wpf/SwingDayTradingPlatform.UI.Wpf.csproj --no-build
```

## Test

```bash
dotnet test
```

The test suite covers:

- Strategy evaluation (EMA Pullback, Brooks PA, Slope Inflection)
- Backtest engine lifecycle and metrics
- Pattern detection (swings, S/R clustering, reversal candles, bar-break/reversal exits)
- Indicators (EMA, ATR, VWAP)
- Market context updates and hourly bias
- Multi-strategy engine signal priority and exit management
- Risk engine (daily caps, loss limits, cooldown, contract sizing)
- Simulated order fills (stop/target)
- JSON state persistence
- App config loading/saving

## IBKR setup

### Ports

| Mode           | TWS  | IB Gateway |
|----------------|------|------------|
| Paper (default)| 7497 | 4002       |
| Live           | 7496 | 4001       |

### Required TWS / Gateway settings

1. Enable API connections
2. Allow socket clients on localhost
3. Set the port to match `appsettings.json`
4. Disable `Read-Only API`
5. Log into paper first

### Market data permissions

- CME futures market data for MES
- Historical futures data access
- Active TWS or Gateway session

### API assembly

The solution references the installed IBKR C# API at:

```
C:\TWS API\source\CSharpClient\client\bin\Release\netstandard2.0\CSharpAPI.dll
```

If your API is installed elsewhere, update the reference in:

- `SwingDayTradingPlatform.Execution.Ibkr/SwingDayTradingPlatform.Execution.Ibkr.csproj`
- `SwingDayTradingPlatform.UI.Wpf/SwingDayTradingPlatform.UI.Wpf.csproj`

## Configuration

Edit `SwingDayTradingPlatform.UI.Wpf/appsettings.json`.

### Key settings

| Section    | Field                | Default   | Description                        |
|------------|----------------------|-----------|------------------------------------|
| `ibkr`     | `host`               | 127.0.0.1 | TWS/Gateway host                   |
| `ibkr`     | `port`               | 7497      | API port                           |
| `ibkr`     | `useSimulator`       | false     | Use simulated bars (no TWS needed) |
| `ibkr`     | `paperMode`          | true      | Paper trading mode                 |
| `ibkr`     | `contractMonth`      | 202606    | Active MES contract month          |
| `trading`  | `entryWindowStart`   | 09:40     | Earliest entry (ET)                |
| `trading`  | `entryWindowEnd`     | 15:50     | Latest entry (ET)                  |
| `trading`  | `flattenTime`        | 15:55     | Forced flatten (ET)                |
| `risk`     | `maxTradesPerDay`    | 5         | Daily trade limit                  |
| `risk`     | `maxLossesPerDay`    | 3         | Kill switch trigger                |
| `risk`     | `maxStopPoints`      | 10        | Max allowable stop distance        |

### Simulator mode (UI demo)

Set `ibkr.useSimulator = true` to run without TWS. The UI and strategy flow run on synthetic bars.

### Paper trading mode

Set `ibkr.useSimulator = false` and connect to TWS paper (port 7497) or Gateway paper (port 4002).

## Common issues

| Problem                    | Likely cause                                           |
|----------------------------|--------------------------------------------------------|
| Connects but no orders     | Read-Only API enabled, wrong port, or missing permissions |
| Connects but no bars       | No CME market data subscription or wrong contract month |
| Orders reject immediately  | Paper/live mismatch, expired contract, or API precautions |

## Safety

This is automated trading software. Validate in this order:

1. Simulator mode
2. IBKR paper trading
3. Live trading only after paper behavior is stable
