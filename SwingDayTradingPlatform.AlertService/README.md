# Extrema Alert Service (MES/ES)

A .NET 8 Console App that detects local maxima/minima on 5-minute bars for ES/MES futures and sends real-time alerts via console, Telegram, and CSV.

## Architecture

```
IBarFeed (pluggable data source)
  ├── CsvBarFeed    – CSV replay for debugging
  └── IbBarFeed     – Live data via IB Gateway (TWS API)
         │
         ▼
IExtremaDetector    – Detects MAX/MIN pivot points
         │
         ▼
  ┌──────┴──────────┐
  │                 │
INotifier        CsvAlertStore
  ├── ConsoleNotifier (always on)
  └── TelegramNotifier (optional)
```

## Quick Start

### Prerequisites

- .NET 8 SDK
- IBKR TWS API C# client DLL (for realtime mode): install [TWS API](https://interactivebrokers.github.io/tws-api/) to `C:\TWS API`
- IB Gateway or TWS running (for realtime mode)
- Telegram Bot token (optional, for mobile alerts)

### Build & Run

```bash
# Build
cd SwingDayTradingPlatform.AlertService
dotnet build

# Run in replay mode (default)
dotnet run -- --file ./data.csv

# Run in realtime mode (IB Gateway)
dotnet run -- --mode realtime --symbol MES --ibPort 4002

# Full example with Telegram
dotnet run -- --mode realtime --symbol MES --ibPort 4002 \
  --telegramToken "123456:ABC-DEF..." \
  --telegramChatId "-100123456789" \
  --output ./alerts.csv
```

## Configuration

All settings can be specified via `appsettings.json` or command-line arguments (CLI overrides JSON).

| Parameter | CLI Flag | Default | Description |
|-----------|----------|---------|-------------|
| Mode | `--mode` | `replay` | `realtime` or `replay` |
| File | `--file` | `./data.csv` | CSV file path (replay mode) |
| Symbol | `--symbol` | `MES` | `MES` or `ES` |
| Exchange | `--exchange` | `CME` | Exchange |
| TickSize | `--tick` | `0.25` | Tick size |
| BarSize | `--bar` | `5m` | Bar interval (fixed 5-minute) |
| Lookback | `--lookback` | `5` | Number of bars for monotonic run |
| EmaPeriod | `--ema` | `20` | EMA period |
| IbHost | `--ibHost` | `127.0.0.1` | IB Gateway host |
| IbPort | `--ibPort` | `4002` | IB Gateway port |
| ClientId | `--clientId` | `12` | IB client ID |
| TelegramToken | `--telegramToken` | *(empty)* | Telegram Bot API token |
| TelegramChatId | `--telegramChatId` | *(empty)* | Telegram chat ID |
| Output | `--output` | `./alerts.csv` | Alert CSV output path |

## Extrema Detection Logic

Uses **close-price mode** with a configurable lookback window (default 5):

**MAX at bar `i`** (lookback=5):
```
close[i-4] < close[i-3] < close[i-2] < close[i-1] < close[i]   (5 strictly increasing closes)
AND close[i+1] < close[i]                                        (confirmation: next bar drops)
```

**MIN at bar `i`** (lookback=5):
```
close[i-4] > close[i-3] > close[i-2] > close[i-1] > close[i]   (5 strictly decreasing closes)
AND close[i+1] > close[i]                                        (confirmation: next bar rises)
```

The alert fires when bar `i+1` closes (that's when we can confirm bar `i` is a pivot).

Each alert includes:
- `eventTime` = timestamp of bar `i+1` (trigger time)
- `pivotTime` = timestamp of bar `i` (the actual extremum)
- `type` = MAX or MIN
- `close`, `high`, `low` = OHLC of bar `i`
- `emaValue` = EMA(20) at bar `i`
- `emaDirection` = UP / DOWN / FLAT (based on EMA[i] − EMA[i−1])

## CSV Replay Mode

Create a CSV file with header `timestamp,open,high,low,close`:

```csv
timestamp,open,high,low,close
2024-01-02 09:35:00,4800.25,4802.50,4799.00,4801.75
2024-01-02 09:40:00,4801.75,4803.00,4800.50,4802.25
...
```

Supported timestamp formats: `yyyy-MM-dd HH:mm:ss`, `MM/dd/yyyy HH:mm:ss`, ISO 8601, Unix epoch.

Run:
```bash
dotnet run -- --mode replay --file ./data.csv
```

## IB Gateway Setup

### Port Configuration

| Mode | Port | Description |
|------|------|-------------|
| Paper Trading | **4002** | IB Gateway paper account (default) |
| Live Trading | **4001** | IB Gateway live account |
| TWS Paper | 7497 | TWS paper account |
| TWS Live | 7496 | TWS live account |

### Enabling the API in IB Gateway

1. Open IB Gateway (or TWS)
2. Go to **Configure** → **Settings** → **API** → **Settings**
3. Check **Enable ActiveX and Socket Clients**
4. Set **Socket port** to `4002` (paper) or `4001` (live)
5. Uncheck **Read-Only API** if you need order placement (not needed for this alert service)
6. Add `127.0.0.1` to **Trusted IPs** (or check **Allow connections from localhost only**)

### Market Data Subscription

You **must** have a CME market data subscription to receive real-time ES/MES data:
- Log in to [IBKR Account Management](https://www.interactivebrokers.com/sso/Login)
- Go to **Settings** → **Market Data Subscriptions**
- Subscribe to **CME Real-Time** (for ES/MES futures)
- Without this subscription, you will get delayed data or error code 10197

### How It Works (Realtime)

1. Connects to IB Gateway via the TWS API (EClientSocket/EWrapper)
2. Requests 2 days of 5-minute historical bars with `keepUpToDate=true`
3. Historical bars stream in for backfill, then live updates continue
4. When a new bar timestamp appears, the previous bar is confirmed as closed
5. On disconnect: exponential backoff reconnect (2s, 4s, 8s, 16s, max 30s)
6. On reconnect: re-requests 2 days of history to fill any gaps

## Telegram Bot Setup

1. **Create a bot**: message [@BotFather](https://t.me/BotFather) on Telegram, send `/newbot`, follow prompts. Copy the **token**.

2. **Get your chat ID**:
   - Send any message to your new bot
   - Visit `https://api.telegram.org/bot<YOUR_TOKEN>/getUpdates`
   - Find `"chat":{"id":123456789}` in the response — that's your chat ID
   - For group chats, add the bot to the group first, then check getUpdates

3. **Configure**:
   ```bash
   dotnet run -- --mode realtime --telegramToken "123456:ABC-DEF..." --telegramChatId "123456789"
   ```

If token or chatId are not set, Telegram is silently disabled — the app still runs with console + CSV output.

Alert message format:
```
🔴 MAX detected
Pivot: 2024-01-02 10:30
Event: 2024-01-02 10:35
Close=4825.50  High=4826.00  Low=4824.25
EMA20=4820.15 (UP)
Symbol: MES
```

## Output

### Console
Real-time bar status and color-coded alerts (red for MAX, green for MIN).

### alerts.csv
```csv
eventTime,pivotTime,type,symbol,close,high,low,emaPeriod,emaValue,emaDirection,mode
2024-01-02 10:35:00,2024-01-02 10:30:00,MAX,MES,4825.50,4826.00,4824.25,20,4820.15,UP,close
```

## Timezone

All timestamps use **Eastern Time** (America/New_York), which is the standard for CME futures trading:
- ET = UTC−5 (EST) or UTC−4 (EDT during daylight saving)
- IB Gateway returns bar times in the exchange's local timezone
- The alert service stores and displays all times in ET

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Cannot connect to IB Gateway" | Ensure IB Gateway is running and API is enabled on the configured port |
| No market data / error 10197 | Subscribe to CME real-time data in IBKR Account Management |
| "CSharpAPI.dll not found" | Install TWS API to `C:\TWS API` and build the C# client |
| Telegram not sending | Verify token and chatId; check bot has permission to send to the chat |
| Reconnecting repeatedly | Check network, IB Gateway auto-restart settings, and daily maintenance window (Sun 23:45–00:45 ET) |
| Bars missing during reconnect | Normal — the service re-requests 2 days of history on reconnect to fill gaps |

## Project Structure

```
SwingDayTradingPlatform.AlertService/
├── Program.cs                    Entry point, config, event wiring
├── Bar.cs                        Bar record + IBarFeed interface
├── Alert.cs                      Alert record + enums + interfaces
├── EmaCalculator.cs              Incremental EMA computation
├── ExtremaDetector.cs            MAX/MIN detection engine
├── Feeds/
│   ├── CsvBarFeed.cs             CSV replay data source
│   └── IbBarFeed.cs              IB Gateway live data source
├── Notifiers/
│   ├── ConsoleNotifier.cs        Console output (always on)
│   └── TelegramNotifier.cs       Telegram Bot API (optional)
├── Stores/
│   └── CsvAlertStore.cs          CSV alert persistence
├── appsettings.json              Default configuration
└── README.md                     This file
```
