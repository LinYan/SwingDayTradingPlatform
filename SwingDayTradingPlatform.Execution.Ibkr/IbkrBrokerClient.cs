using System.Collections.Concurrent;
using System.Globalization;
using IBApi;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Execution.Ibkr;

public sealed class IbkrBrokerClient : DefaultEWrapper, IBrokerAdapter
{
    private const int DefaultOrderSeed = 1000;
    private const string AccountSummaryTags = "NetLiquidation,AvailableFunds,BuyingPower,RealizedPnL,UnrealizedPnL";

    private readonly IbkrConfig _config;
    private readonly decimal _pointValue;
    private readonly string _tradingSymbol;
    private readonly ConcurrentDictionary<int, OrderTicket> _orders = new();
    private readonly ConcurrentDictionary<string, PositionSnapshot> _positions = new();
    private readonly ConcurrentDictionary<string, string> _accountValues = new();
    private readonly SemaphoreSlim _orderLock = new(1, 1);
    private readonly object _connectionGate = new();
    private readonly object _accountGate = new();

    private EReaderMonitorSignal? _readerSignal;
    private EClientSocket? _client;
    private EReader? _reader;
    private Thread? _readerThread;
    private CancellationTokenSource? _reconnectCts;
    private readonly object _reconnectLock = new();
    private TaskCompletionSource<bool>? _connectedTcs;
    private TaskCompletionSource<bool>? _syncTcs;
    private int _syncRemainingCallbacks;
    private volatile bool _disposed;
    private volatile bool _manualDisconnect;
    private int _nextOrderId = DefaultOrderSeed;
    private int _accountSummaryRequestId = 9001;
    private int _executionRequestId = 9101;

    public IbkrBrokerClient(IbkrConfig config, decimal pointValue = 50m, string tradingSymbol = "ES")
    {
        _config = config;
        _pointValue = pointValue;
        _tradingSymbol = tradingSymbol;
    }

    public event EventHandler<ConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<OrderStatusEventArgs>? OrderStatusChanged;
    public event EventHandler<ExecutionEventArgs>? ExecutionReceived;
    public event EventHandler<PositionEventArgs>? PositionChanged;
    public event EventHandler<AccountSummaryEventArgs>? AccountSummaryReceived;

    public BrokerConnectionState ConnectionState { get; private set; } = BrokerConnectionState.Disconnected;
    public IReadOnlyCollection<OrderTicket> ActiveOrders => _orders.Values.OrderByDescending(x => x.CreatedAtUtc).ToList();
    public IReadOnlyCollection<PositionSnapshot> Positions => _positions.Values.OrderBy(x => x.Symbol).ToList();
    public AccountSnapshot? LatestAccountSnapshot { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IbkrBrokerClient));

        if (_client?.IsConnected() == true)
            return;

        lock (_connectionGate)
        {
            if (_client?.IsConnected() == true)
                return;

            // Clean up old connection before creating new one
            if (_client is not null)
            {
                try { _client.eDisconnect(); } catch { /* ignore */ }
            }

            _manualDisconnect = false;
            _connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _readerSignal = new EReaderMonitorSignal();
            _client = new EClientSocket(this, _readerSignal);
            _client.SetConnectOptions("+PACEAPI");
            UpdateConnectionState(BrokerConnectionState.Connecting, $"Connecting to {_config.Host}:{_config.Port} with client id {_config.ClientId}.");
            _client.eConnect(_config.Host, _config.Port, _config.ClientId);
            StartReaderLoop();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
        await _connectedTcs!.Task.WaitAsync(timeoutCts.Token);
        UpdateConnectionState(BrokerConnectionState.Connected, $"Connected to IBKR {_config.Host}:{_config.Port}.");
    }

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();

        _syncTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _syncRemainingCallbacks, 3); // positionEnd + execDetailsEnd + accountSummaryEnd
        _accountValues.Clear();
        _client!.reqOpenOrders();
        _client.reqPositions();
        _client.reqExecutions(Interlocked.Increment(ref _executionRequestId), new ExecutionFilter());
        _client.reqAccountSummary(Interlocked.Increment(ref _accountSummaryRequestId), "All", AccountSummaryTags);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
        await _syncTcs.Task.WaitAsync(timeoutCts.Token);
        OnConnectionChanged("Sync complete: positions, open orders, executions, and account summary.");
    }

    public async Task<IReadOnlyCollection<OrderTicket>> PlaceBracketAsync(PlaceBracketRequest request, CancellationToken cancellationToken)
    {
        EnsureConnected();

        await _orderLock.WaitAsync(cancellationToken);
        try
        {
            var bracketSize = request.TakeProfitPrice is null ? 2 : 3;
            var parentId = ReserveOrderIds(bracketSize);
            var exitSide = request.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
            var created = DateTimeOffset.UtcNow;
            var contract = CreateFuturesContract(request.ContractMonth);

            var parentOrder = BuildParentOrder(parentId, request);
            var parentTicket = new OrderTicket(parentId, request.SignalId, request.Symbol, request.ContractMonth, request.Side, OrderIntent.Entry,
                request.Quantity, request.UseMarketEntry ? null : request.EntryPrice, null, "Submitted", created, parentId.ToString(CultureInfo.InvariantCulture), request.SignalId);

            // OCA group links stop and target so filling one auto-cancels the other
            var ocaGroup = request.TakeProfitPrice is not null ? $"OCA-{request.SignalId}" : null;

            var stopId = parentId + 1;
            var stopOrder = BuildStopOrder(stopId, parentId, request, exitSide, request.TakeProfitPrice is null, ocaGroup);
            var stopTicket = new OrderTicket(stopId, request.SignalId, request.Symbol, request.ContractMonth, exitSide, OrderIntent.StopLoss,
                request.Quantity, null, request.StopPrice, "Submitted", created, stopId.ToString(CultureInfo.InvariantCulture), request.SignalId);

            _orders[parentId] = parentTicket;
            _orders[stopId] = stopTicket;
            OrderStatusChanged?.Invoke(this, new OrderStatusEventArgs(parentTicket));
            OrderStatusChanged?.Invoke(this, new OrderStatusEventArgs(stopTicket));

            _client!.placeOrder(parentId, contract, parentOrder);
            _client.placeOrder(stopId, contract, stopOrder);

            var result = new List<OrderTicket> { parentTicket, stopTicket };
            if (request.TakeProfitPrice is not null)
            {
                var targetId = parentId + 2;
                var targetOrder = BuildTargetOrder(targetId, parentId, request, exitSide, ocaGroup);
                var targetTicket = new OrderTicket(targetId, request.SignalId, request.Symbol, request.ContractMonth, exitSide, OrderIntent.TakeProfit,
                    request.Quantity, request.TakeProfitPrice, null, "Submitted", created, targetId.ToString(CultureInfo.InvariantCulture), request.SignalId);

                _orders[targetId] = targetTicket;
                OrderStatusChanged?.Invoke(this, new OrderStatusEventArgs(targetTicket));
                _client.placeOrder(targetId, contract, targetOrder);
                result.Add(targetTicket);
            }

            return result;
        }
        finally
        {
            _orderLock.Release();
        }
    }

    public Task CancelAllAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();
        _client!.reqGlobalCancel(new OrderCancel());
        OnConnectionChanged("Requested global cancel.");
        return Task.CompletedTask;
    }

    public Task FlattenAsync(string symbol, string contractMonth, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var current = _positions.Values.FirstOrDefault(x => x.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) && x.Quantity != 0);
        if (current is null || current.Quantity == 0)
            return Task.CompletedTask;

        var side = current.Side == PositionSide.Long ? OrderSide.Sell : OrderSide.Buy;
        var orderId = ReserveOrderIds(1);
        var order = new Order
        {
            OrderId = orderId,
            Action = ToIbAction(side),
            TotalQuantity = Math.Abs(current.Quantity),
            OrderType = "MKT",
            Tif = "DAY",
            OutsideRth = false,
            Transmit = true,
            OrderRef = $"FLATTEN-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
        };

        var ticket = new OrderTicket(orderId, order.OrderRef, symbol, contractMonth, side, OrderIntent.Flatten, Math.Abs(current.Quantity),
            null, null, "Submitted", DateTimeOffset.UtcNow, orderId.ToString(CultureInfo.InvariantCulture), order.OrderRef);

        _orders[orderId] = ticket;
        OrderStatusChanged?.Invoke(this, new OrderStatusEventArgs(ticket));
        _client!.placeOrder(orderId, CreateFuturesContract(contractMonth), order);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _manualDisconnect = true;
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _readerThread = null;
        if (_client?.IsConnected() == true)
            _client.eDisconnect();

        _orderLock.Dispose();
        return ValueTask.CompletedTask;
    }

    public void PushMark(decimal lastPrice)
    {
        foreach (var kvp in _positions.ToArray())
        {
            var current = kvp.Value;
            var unrealized = current.Side switch
            {
                PositionSide.Long => (lastPrice - current.AveragePrice) * Math.Abs(current.Quantity) * _pointValue,
                PositionSide.Short => (current.AveragePrice - lastPrice) * Math.Abs(current.Quantity) * _pointValue,
                _ => 0m
            };

            var updated = current with
            {
                MarketPrice = lastPrice,
                UnrealizedPnL = unrealized,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            _positions[kvp.Key] = updated;
            PositionChanged?.Invoke(this, new PositionEventArgs(updated));
        }

        if (LatestAccountSnapshot is not null)
        {
            var totalUnrealized = _positions.Values.Sum(x => x.UnrealizedPnL);
            LatestAccountSnapshot = LatestAccountSnapshot with { UnrealizedPnL = totalUnrealized };
            AccountSummaryReceived?.Invoke(this, new AccountSummaryEventArgs(LatestAccountSnapshot));
        }
    }

    public override void nextValidId(int orderId)
    {
        _nextOrderId = Math.Max(_nextOrderId, orderId);
        _connectedTcs?.TrySetResult(true);
        OnConnectionChanged($"IBKR next valid order id received: {orderId}.");
    }

    public override void managedAccounts(string accountsList)
    {
        OnConnectionChanged($"Managed accounts: {accountsList}");
    }

    public override void openOrder(int orderId, Contract contract, Order order, OrderState orderState)
    {
        var ticket = _orders.TryGetValue(orderId, out var existing)
            ? existing
            : new OrderTicket(orderId, order.OrderRef ?? string.Empty, contract.Symbol, contract.LastTradeDateOrContractMonth ?? string.Empty,
                ToOrderSide(order.Action), InferIntent(order), (int)order.TotalQuantity, order.OrderType == "LMT" ? (decimal)order.LmtPrice : null,
                IsStopOrder(order) ? (decimal)order.AuxPrice : null, orderState.Status ?? "Open", DateTimeOffset.UtcNow,
                orderId.ToString(CultureInfo.InvariantCulture), order.ParentId == 0 ? order.OrderRef : order.ParentId.ToString(CultureInfo.InvariantCulture));

        var updated = ticket with
        {
            Status = string.IsNullOrWhiteSpace(orderState.Status) ? ticket.Status : orderState.Status,
            LimitPrice = order.OrderType == "LMT" ? (decimal)order.LmtPrice : ticket.LimitPrice,
            StopPrice = IsStopOrder(order) ? (decimal)order.AuxPrice : ticket.StopPrice,
            BrokerOrderId = orderId.ToString(CultureInfo.InvariantCulture)
        };

        _orders[orderId] = updated;
        OrderStatusChanged?.Invoke(this, new OrderStatusEventArgs(updated));
    }

    public override void orderStatus(int orderId, string status, decimal filled, decimal remaining, double avgFillPrice, long permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
    {
        if (_orders.TryGetValue(orderId, out var ticket))
        {
            var updated = ticket with { Status = status };
            _orders[orderId] = updated;
            OrderStatusChanged?.Invoke(this, new OrderStatusEventArgs(updated));
        }
    }

    public override void execDetails(int reqId, Contract contract, IBApi.Execution execution)
    {
        var fill = new ExecutionFill(
            execution.ExecId,
            execution.OrderId,
            contract.Symbol,
            (int)execution.Shares,
            (decimal)execution.Price,
            ParseIbTime(execution.Time),
            execution.Side);

        ExecutionReceived?.Invoke(this, new ExecutionEventArgs(fill));
    }

    public override void execDetailsEnd(int reqId)
    {
        DecrementSyncCounter();
    }

    public override void position(string account, Contract contract, decimal pos, double avgCost)
    {
        var side = pos switch
        {
            > 0 => PositionSide.Long,
            < 0 => PositionSide.Short,
            _ => PositionSide.Flat
        };

        var symbolKey = BuildPositionKey(contract.Symbol);
        _positions.TryGetValue(symbolKey, out var existing);
        // IBKR returns avgCost as price * multiplier for futures; divide by pointValue to get average fill price
        var avgPrice = avgCost == 0 || _pointValue == 0 ? 0m : (decimal)avgCost / _pointValue;
        var updated = new PositionSnapshot(
            contract.Symbol,
            contract.LastTradeDateOrContractMonth ?? string.Empty,
            side,
            (int)pos,
            avgPrice,
            existing?.MarketPrice ?? 0m,
            existing?.UnrealizedPnL ?? 0m,
            DateTimeOffset.UtcNow);

        _positions[symbolKey] = updated;
        PositionChanged?.Invoke(this, new PositionEventArgs(updated));
    }

    public override void positionEnd()
    {
        DecrementSyncCounter();
    }

    public override void accountSummary(int reqId, string account, string tag, string value, string currency)
    {
        lock (_accountGate)
        {
            _accountValues[tag] = value;
            var snapshot = new AccountSnapshot(
                ParseDecimal(_accountValues.GetValueOrDefault("NetLiquidation")),
                ParseDecimal(_accountValues.GetValueOrDefault("AvailableFunds")),
                ParseDecimal(_accountValues.GetValueOrDefault("BuyingPower")),
                ParseDecimal(_accountValues.GetValueOrDefault("RealizedPnL")),
                ParseDecimal(_accountValues.GetValueOrDefault("UnrealizedPnL")),
                string.IsNullOrWhiteSpace(currency) ? "USD" : currency);

            LatestAccountSnapshot = snapshot;
            AccountSummaryReceived?.Invoke(this, new AccountSummaryEventArgs(snapshot));
        }
    }

    public override void accountSummaryEnd(int reqId)
    {
        DecrementSyncCounter();
    }

    private void DecrementSyncCounter()
    {
        if (Interlocked.Decrement(ref _syncRemainingCallbacks) <= 0)
            _syncTcs?.TrySetResult(true);
    }

    public override void connectAck()
    {
        if (_client?.AsyncEConnect == true)
            _client.startApi();
    }

    public override void connectionClosed()
    {
        UpdateConnectionState(BrokerConnectionState.Disconnected, "IBKR connection closed.");
        if (!_manualDisconnect)
            ScheduleReconnect();
    }

    public override void error(Exception e)
    {
        UpdateConnectionState(BrokerConnectionState.Faulted, $"IBKR exception: {e.Message}");
        if (!_manualDisconnect)
            ScheduleReconnect();
    }

    public override void error(string str)
    {
        OnConnectionChanged($"IBKR error: {str}");
    }

    public override void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
    {
        var message = $"IBKR error {errorCode} (req/order {id}): {errorMsg}";
        switch (errorCode)
        {
            case 2104:
            case 2106:
            case 2158:
                OnConnectionChanged(message);
                return;
            case 1100:
            case 1101:
            case 1102:
            case 1300:
            case 504:
                UpdateConnectionState(BrokerConnectionState.Reconnecting, message);
                if (!_manualDisconnect)
                    ScheduleReconnect();
                break;
            default:
                OnConnectionChanged(message);
                break;
        }
    }

    private void StartReaderLoop()
    {
        if (_client is null || _readerSignal is null)
            return;

        _reader = new EReader(_client, _readerSignal);
        _reader.Start();

        _readerThread = new Thread(() =>
        {
            while (!_disposed && _client.IsConnected())
            {
                try
                {
                    _readerSignal.waitForSignal();
                    _reader.processMsgs();
                }
                catch (Exception ex)
                {
                    if (_disposed)
                        break;

                    UpdateConnectionState(BrokerConnectionState.Faulted, $"IBKR reader loop faulted: {ex.Message}");
                    if (!_manualDisconnect)
                        ScheduleReconnect();
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "IbkrBrokerReader"
        };

        _readerThread.Start();
    }

    private void ScheduleReconnect()
    {
        if (_disposed)
            return;

        CancellationToken token;
        lock (_reconnectLock)
        {
            if (_reconnectCts is { IsCancellationRequested: false })
                return;

            _reconnectCts?.Dispose();
            _reconnectCts = new CancellationTokenSource();
            token = _reconnectCts.Token;
        }

        _ = Task.Run(async () =>
        {
            var attempt = 0;
            while (!token.IsCancellationRequested && !_disposed && _client?.IsConnected() != true)
            {
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 4))));
                UpdateConnectionState(BrokerConnectionState.Reconnecting, $"Reconnect attempt {attempt} in {delay.TotalSeconds:0}s.");

                try
                {
                    await Task.Delay(delay, token);
                    await ConnectAsync(token);
                    await SyncAsync(token);
                    _reconnectCts.Cancel();
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    UpdateConnectionState(BrokerConnectionState.Reconnecting, $"Reconnect attempt {attempt} failed: {ex.Message}");
                }
            }
        }, token);
    }

    private int ReserveOrderIds(int count)
    {
        return Interlocked.Add(ref _nextOrderId, count) - count;
    }

    private Contract CreateFuturesContract(string contractMonth)
    {
        var resolvedMonth = string.IsNullOrWhiteSpace(contractMonth) ? ResolveFrontMonth(DateTimeOffset.UtcNow) : contractMonth;
        return new Contract
        {
            Symbol = _tradingSymbol,
            SecType = "FUT",
            Exchange = "CME",
            Currency = "USD",
            Multiplier = ((int)_pointValue).ToString(),
            LastTradeDateOrContractMonth = resolvedMonth,
            IncludeExpired = false
        };
    }

    private static string ResolveFrontMonth(DateTimeOffset nowUtc)
    {
        var month = nowUtc.Month;
        var quarterMonth = month <= 3 ? 3 : month <= 6 ? 6 : month <= 9 ? 9 : 12;
        var year = nowUtc.Year;
        if (month == quarterMonth && nowUtc.Day > 20)
        {
            quarterMonth += 3;
            if (quarterMonth > 12)
            {
                quarterMonth = 3;
                year += 1;
            }
        }

        return $"{year}{quarterMonth:00}";
    }

    private static Order BuildParentOrder(int orderId, PlaceBracketRequest request)
    {
        var order = new Order
        {
            OrderId = orderId,
            Action = ToIbAction(request.Side),
            TotalQuantity = request.Quantity,
            OrderType = request.UseMarketEntry ? "MKT" : "LMT",
            Tif = "DAY",
            OutsideRth = false,
            Transmit = false,
            OrderRef = request.SignalId
        };

        if (!request.UseMarketEntry)
            order.LmtPrice = (double)request.EntryPrice;

        return order;
    }

    private static Order BuildStopOrder(int orderId, int parentId, PlaceBracketRequest request, OrderSide side, bool transmit, string? ocaGroup)
    {
        return new Order
        {
            OrderId = orderId,
            ParentId = parentId,
            Action = ToIbAction(side),
            TotalQuantity = request.Quantity,
            OrderType = "STP",
            AuxPrice = (double)request.StopPrice,
            Tif = "DAY",
            OutsideRth = false,
            Transmit = transmit,
            OrderRef = $"{request.SignalId}-STOP",
            OcaGroup = ocaGroup ?? string.Empty,
            OcaType = ocaGroup is not null ? 1 : 0 // 1 = Cancel remaining orders with block
        };
    }

    private static Order BuildTargetOrder(int orderId, int parentId, PlaceBracketRequest request, OrderSide side, string? ocaGroup)
    {
        return new Order
        {
            OrderId = orderId,
            ParentId = parentId,
            Action = ToIbAction(side),
            TotalQuantity = request.Quantity,
            OrderType = "LMT",
            LmtPrice = (double)(request.TakeProfitPrice ?? request.EntryPrice),
            Tif = "DAY",
            OutsideRth = false,
            Transmit = true,
            OrderRef = $"{request.SignalId}-TARGET",
            OcaGroup = ocaGroup ?? string.Empty,
            OcaType = ocaGroup is not null ? 1 : 0
        };
    }

    private void EnsureConnected()
    {
        if (_client?.IsConnected() != true)
            throw new InvalidOperationException("IBKR client is not connected.");
    }

    private void UpdateConnectionState(BrokerConnectionState state, string message)
    {
        ConnectionState = state;
        OnConnectionChanged(message);
    }

    private void OnConnectionChanged(string message) =>
        ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(ConnectionState, message));

    private static string BuildPositionKey(string symbol) => symbol.ToUpperInvariant();

    private static string ToIbAction(OrderSide side) => side == OrderSide.Buy ? "BUY" : "SELL";

    private static OrderSide ToOrderSide(string action) => string.Equals(action, "SELL", StringComparison.OrdinalIgnoreCase) ? OrderSide.Sell : OrderSide.Buy;

    private static bool IsStopOrder(Order order) => string.Equals(order.OrderType, "STP", StringComparison.OrdinalIgnoreCase);

    private static OrderIntent InferIntent(Order order)
    {
        if (order.ParentId == 0)
            return OrderIntent.Entry;
        if (IsStopOrder(order))
            return OrderIntent.StopLoss;
        if (string.Equals(order.OrderType, "LMT", StringComparison.OrdinalIgnoreCase))
            return OrderIntent.TakeProfit;
        return OrderIntent.Entry;
    }

    private static decimal ParseDecimal(string? raw)
    {
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }

    private static DateTimeOffset ParseIbTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DateTimeOffset.UtcNow;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            return dto;

        if (DateTime.TryParseExact(raw, "yyyyMMdd  HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
            return new DateTimeOffset(local);

        if (DateTime.TryParseExact(raw, "yyyyMMdd-HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var utc))
            return new DateTimeOffset(utc, TimeSpan.Zero);

        return DateTimeOffset.UtcNow;
    }
}
