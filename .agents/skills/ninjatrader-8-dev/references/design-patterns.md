# Design Patterns for NinjaTrader 8

## MVVM
**Use in:** UI Layer

- `TradePlannerView.xaml` binds to `TradePlannerViewModel`
- `TradePlannerViewModel` inherits `ViewModelBase` (INotifyPropertyChanged)
- Commands exposed as `ICommand` (e.g., `RelayCommand`)
- No logic in `.xaml.cs` code-behind

## Strategy
**Use in:** Domain Layer (direction-specific calculations)

```csharp
public interface IDirectionStrategy
{
    double CalculateStopDistance(double entry, double stop);
    double CalculateTakeProfit(double entry, double stopDistance, double rr);
}

public class LongStrategy : IDirectionStrategy { ... }
public class ShortStrategy : IDirectionStrategy { ... }
```

Avoid:
```csharp
if (direction == TradeDirection.Long) { ... } else { ... }
```

## State
**Use in:** Domain Layer (trade lifecycle)

```csharp
public enum TradeState
{
    Idle, Planning, Armed, Submitted, Active, BreakEvenTriggered, Closed, Cancelled
}
```

`TradeStateMachine` enforces valid transitions explicitly. No implicit skipping.

## Command
**Use in:** UI Layer (user actions)

`ExecuteTradeCommand` bound to spacebar. Validates preconditions (`Contracts >= 1`, valid state) then invokes `ExecutionCoordinator`.

## Observer
**Use in:** Application / Infrastructure Layers (price updates)

```csharp
// Infrastructure
Bars.Instrument.MarketData += OnMarketData;

// Application
PriceMonitor exposes C# event: public event Action<double> PriceUpdated;
BreakEvenService subscribes to PriceMonitor.PriceUpdated.
```

Never poll. Always use events.

## Adapter
**Use in:** Infrastructure Layer

Wrap NT8-specific APIs so upper layers remain testable:

```csharp
public interface IOrderAdapter
{
    void SubmitEntry(TradePlan plan);
    void SubmitStop(TradePlan plan);
    void SubmitTarget(TradePlan plan);
}
```

`NinjaOrderAdapter` implements this using `Account.Submit()`.

## Factory
**Use in:** Infrastructure Layer (order creation)

```csharp
private Order CreateStopMarketOrder(TradePlan plan)
{
    return Account.CreateOrder(
        Instrument,
        OrderAction.Sell,
        OrderType.StopMarket,
        OrderEntry.Manual,
        TimeInForce.Day,
        plan.Contracts,
        plan.StopPrice,
        0,
        "StopLoss");
}
```

Factory methods keep order construction centralized and configurable.

## Performance Patterns

- **Object pooling**: Reuse `SharpDX` resources in `OnRender`
- **Lazy evaluation**: Compute derived values only when inputs change
- **Event aggregation**: Use a single `PriceMonitor` rather than multiple subscribers to `MarketData`
