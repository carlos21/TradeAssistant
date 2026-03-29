---
name: ninjatrader-8-dev
description: NinjaTrader 8 AddOn and Indicator development with clean architecture, SOLID principles, and design patterns. Use when working with NinjaTrader 8 C# code, NinjaScript, AddOns, Indicators, strategies, or when questions about NT8 APIs, order management, chart rendering, or architecture arise.
---

# NinjaTrader 8 Development

## Documentation Lookup

Always verify NT8 API details against the latest official documentation.

- **General API / AddOn docs**: Search `site:help.ninjatrader.com <topic>` (e.g., `site:help.ninjatrader.com unmanaged orders`)
- **Specific class/method**: Search `site:help.ninjatrader.com NinjaTrader.Cbi.Instrument OnMarketData`
- **NinjaScript reference**: Fetch `https://ninjatrader.com/support/helpGuides/nt8/?ninja_script.html` or search `site:help.ninjatrader.com ninja script`

Use `SearchWeb` or `FetchURL` before assuming an NT8 API behavior.

## Architecture Rules

Enforce a strict 4-layer architecture with zero upward dependencies:

```
UI (WPF/MVVM)
    ↓
Application Layer
    ↓
Domain Layer  ←  no NinjaTrader references, fully unit-testable
    ↓
Infrastructure Layer  ←  thin adapters over NinjaTrader.Cbi APIs
```

See [references/clean-architecture.md](references/clean-architecture.md) for layer responsibilities and examples.

## SOLID Checklist

Before approving code changes, verify:

- **S**ingle Responsibility: Each class has one reason to change. NT8 indicator logic (e.g., `OnBarUpdate`) delegates to services, not monolithic code-behind.
- **O**pen/Closed: Extend behavior via strategies (long/short), not `if/else` on enums.
- **L**iskov Substitution: Derived strategies (`LongStrategy`, `ShortStrategy`) must be interchangeable via `IDirectionStrategy`.
- **I**nterface Segregation: Keep NT8-specific interfaces thin (e.g., `IOrderAdapter`, `IInstrumentInfoProvider`).
- **D**ependency Inversion: Domain and Application depend on abstractions, not `NinjaTrader.Cbi` concrete types.

## Design Patterns

See [references/design-patterns.md](references/design-patterns.md) for NT8-specific usage of:

- MVVM (UI layer)
- Strategy (direction-specific logic)
- State (trade lifecycle)
- Command (execution triggers)
- Observer (price events)
- Adapter (Infrastructure wrappers)
- Factory (order creation)

## Key API Reminders

- **Instrument values**: Always read from `Instrument.MasterInstrument.TickSize`, `.TickValue`, `.PointValue`. Never hardcode tick sizes.
- **Order submission**: Use unmanaged orders via `Account.Submit()` with `OrderType.StopMarket`, `OrderType.Limit`, etc.
- **Event-driven only**: Subscribe to `Bars.Instrument.MarketData` or `OnMarketData`. Never poll prices in loops.
- **Rendering**: `OnRender` must be allocation-free and lightweight.
- **Cleanup**: Dispose all event handlers and unsubscribers in `OnStateChange(State.Terminated)` or `Dispose()`.

See [references/nt8-api-reference.md](references/nt8-api-reference.md) for common classes and methods.
