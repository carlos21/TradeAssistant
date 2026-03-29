# Clean Architecture for NinjaTrader 8

## The Four Layers

### 1. Domain Layer
Pure C# with **zero** `using NinjaTrader.*` references. Fully unit-testable with standard xUnit/NUnit.

**Contains:**
- Value objects (`TradeConfiguration`, `TradePlan`)
- Domain services (`RiskCalculator`, `FuturesPositionSizer`, `RrCalculator`)
- State machines (`TradeStateMachine`)
- Strategy interfaces (`IDirectionStrategy`)

**Rules:**
- No UI, no file I/O, no NT8 API calls.
- All math and business invariants live here.

### 2. Application Layer
Coordinates domain logic with infrastructure and UI. Orchestrates use cases.

**Contains:**
- Controllers (`TradePlannerController`)
- Coordinators (`ExecutionCoordinator`)
- Services that bridge events (`BreakEvenService`, `PriceMonitor`)

**Rules:**
- May reference Domain layer types.
- Must **not** call `NinjaTrader.Cbi` directly. Talks to NT8 only through Infrastructure abstractions.

### 3. Infrastructure Layer
Thin adapters over NinjaTrader 8 APIs. The only layer allowed to reference `NinjaTrader.Cbi`.

**Contains:**
- `NinjaOrderAdapter` → wraps `Account.Submit()`, `Account.Cancel()`, etc.
- `NinjaAccountDataProvider` → wraps `Account.Get(AccountItem.CashValue, Currency.UsDollar)`
- `NinjaInstrumentInfoProvider` → exposes `Instrument.MasterInstrument.TickSize`
- `ChartRendererAdapter` → wraps `Draw.Rectangle()`, `Draw.Text()`, etc.

**Rules:**
- Keep methods small and delegate to Domain/Application for decisions.
- Map NT8-specific types to plain C# types before returning upward.

### 4. UI Layer
WPF floating panel or NT8 chart indicator UI. Strict MVVM.

**Contains:**
- Views (`TradePlannerView.xaml`)
- ViewModels (`TradePlannerViewModel`)
- Commands (`RelayCommand`, `ExecuteTradeCommand`)

**Rules:**
- No trading logic in code-behind.
- ViewModel exposes bindable properties and delegates actions to Application layer.
- No direct NT8 API references in ViewModels.

## Dependency Direction

```
UI → Application → Domain ← Infrastructure
```

Infrastructure depends on Domain (via interfaces defined in Domain or Application). UI depends on Application. Application depends on Domain. Domain depends on nothing.

## Example: Position Sizing Flow

1. **UI**: User changes stop price → ViewModel updates `TradeConfiguration`
2. **Application**: `TradePlannerController` detects change
3. **Domain**: `FuturesPositionSizer.Calculate(config, tickSize, tickValue)` returns `TradePlan`
4. **Application**: Controller pushes `TradePlan` back to ViewModel
5. **UI**: ViewModel raises `PropertyChanged` → UI updates

At no point does the Domain layer know about WPF or NinjaTrader.
