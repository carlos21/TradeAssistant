# NinjaTrader 8 API Reference

> Use `SearchWeb` with `site:help.ninjatrader.com <query>` or `FetchURL` to verify the latest API details before relying on signatures below.

## Core Namespaces

- `NinjaTrader.Cbi` — Instruments, accounts, orders, positions
- `NinjaTrader.Gui.Chart` — Chart controls and WPF integration
- `NinjaTrader.NinjaScript` — Indicator/Strategy base classes
- `NinjaTrader.Core` — Utilities (e.g., `Globals`, `DrawingTools`)

## Key Classes

### Instrument
```csharp
Instrument.MasterInstrument.TickSize   // double
Instrument.MasterInstrument.TickValue  // double
Instrument.MasterInstrument.PointValue // double
Instrument.MasterInstrument.Name       // string
```

### Account
```csharp
Account.Get(AccountItem.CashValue, Currency.UsDollar) // double
Account.Submit(Order order)                            // void
Account.Cancel(Order order)                            // void
Account.ChangeOrder(Order order, ...)                  // void
```

### Order
```csharp
Order order = Account.CreateOrder(
    Instrument instrument,
    OrderAction action,
    OrderType orderType,
    OrderEntry orderEntry,
    TimeInForce tif,
    int quantity,
    double limitPrice,
    double stopPrice,
    string ocoId,
    string signalName);
```

### Position
```csharp
Position.AveragePrice
Position.Quantity
Position.MarketPosition  // Long, Short, Flat
```

## Indicator / Strategy Lifecycle

```csharp
protected override void OnStateChange()
{
    if (State == State.SetDefaults) { ... }
    if (State == State.Configure)  { ... }
    if (State == State.Active)     { ... }
    if (State == State.Terminated) { /* cleanup */ }
}

protected override void OnBarUpdate() { ... }
protected override void OnMarketData(MarketDataEventArgs args) { ... }
protected override void OnRender(ChartControl chartControl, ChartScale chartScale) { ... }
```

## Order Types

- `OrderType.Market`
- `OrderType.Limit`
- `OrderType.StopMarket`
- `OrderType.StopLimit`

## Order Actions

- `OrderAction.Buy`
- `OrderAction.Sell`
- `OrderAction.BuyToCover`
- `OrderAction.SellShort`

## Time In Force

- `TimeInForce.Day`
- `TimeInForce.Gtc`
- `TimeInForce.Ioc`

## Drawing (NinjaScript)

```csharp
Draw.Rectangle(this, "tag", true, startTime, startPrice, endTime, endPrice, Brushes.Red, Brushes.Transparent, 2);
Draw.Text(this, "tag", true, "Label", time, price, 0, Brushes.White, ...);
```

## Event-Driven Price Access

```csharp
// In an indicator
Bars.Instrument.MarketData += OnInstrumentMarketData;

private void OnInstrumentMarketData(object sender, MarketDataEventArgs e)
{
    if (e.MarketDataType == MarketDataType.Last)
        double lastPrice = e.Price;
}
```

Always unsubscribe in `State.Terminated`:
```csharp
Bars.Instrument.MarketData -= OnInstrumentMarketData;
```

## Useful Links

- NinjaScript Editor Help: `https://ninjatrader.com/support/helpGuides/nt8/`
- Unmanaged Order Methods: search `site:help.ninjatrader.com unmanaged order methods`
- AddOn Development: search `site:help.ninjatrader.com add on development`
