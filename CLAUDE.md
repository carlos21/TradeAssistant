# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A NinjaTrader 8 AddOn/Indicator for manual futures trade planning and execution. Targets NQ, MNQ, ES, MES. Provides TradingView-style interactive risk visualization with strict futures contract mechanics. Not an automated strategy.

## Build & Development

This is a C# NinjaTrader 8 AddOn. NinjaTrader compiles AddOns internally via its IDE or the NinjaScript Editor. There is no standalone `.csproj` or `dotnet build` workflow — source files are placed in the NinjaTrader documents folder and compiled within the platform.

**NinjaTrader source path (typical):**
```
~/Documents/NinjaTrader 8/bin/Custom/AddOns/TradeAssistant/
```

**To compile:** Open NinjaTrader → NinjaScript Editor → right-click project → Compile. Compilation errors appear in the Output window.

**To run tests (Domain layer):** The Domain layer has no NinjaTrader dependencies and can be extracted into a standard .NET class library with xUnit or NUnit tests run via `dotnet test`.

## Architecture

Four strict layers — no upward dependencies allowed:

```
UI (WPF/MVVM)
    ↓
Application Layer
    ↓
Domain Layer  ←  fully NinjaTrader-free, unit-testable
    ↓
Infrastructure Layer  ←  wraps NinjaTrader API
```

### Domain Layer (`/Domain`)
Pure C# logic with zero NinjaTrader references. All classes here must be unit-testable in isolation.
- `RiskCalculator` — converts account risk % or fixed $ to a dollar risk amount
- `FuturesPositionSizer` — applies the position sizing formula (see below)
- `RrCalculator` — computes TP price from entry, stop, and RR ratio
- `TradeConfiguration` — immutable value object holding all user inputs
- `TradePlan` — computed result: contracts, risk $, profit $, SL price, TP price
- `TradeStateMachine` — enforces explicit state transitions (see states below)

**Position sizing formula (must never deviate):**
```
StopDistanceTicks = |Entry - Stop| / TickSize
RiskPerContract   = StopDistanceTicks × TickValue
Contracts         = floor(RiskAmount / RiskPerContract)   // integer, >= 1
```

### Application Layer (`/Application`)
Coordinates domain logic with UI and infrastructure. No direct NinjaTrader API calls.
- `TradePlannerController` — reacts to UI changes, triggers recalculation
- `ExecutionCoordinator` — orchestrates order submission sequence on spacebar press
- `BreakEvenService` — listens to price events; moves stop to entry when RR target hit
- `PriceMonitor` — wraps price event subscription (event-driven, never poll)

### Infrastructure Layer (`/Infrastructure`)
Thin adapters over NinjaTrader API. Instrument tick values must always come from `Instrument.MasterInstrument.*` — never hardcoded.
- `NinjaOrderAdapter` — submits unmanaged orders (entry, stop market, limit TP)
- `AccountDataProvider` — reads account balance
- `InstrumentInfoProvider` — exposes TickSize, TickValue, PointValue
- `ChartRendererAdapter` — draws SL/TP rectangles and labels on chart

### UI Layer (`/UI`)
WPF floating panel, strict MVVM. No trading logic in views or code-behind.
- `TradePlannerViewModel` — all bindable properties; no NinjaTrader refs
- `TradePlannerView` — XAML only

## Key Behavioral Rules

**Instrument values:** Always read from `Instrument.MasterInstrument.TickSize`, `.TickValue`, `.PointValue`. Never hardcode NQ/MNQ/ES/MES tick sizes.

**Contracts:** Always `int`, always `>= 1`. Use `Math.Floor`. Guard against zero (account too small / stop too tight).

**Chart interactivity:** Dragging SL → recalculate contracts + TP. Dragging TP → recalculate SL. RR ratio stays constant during drags.

**Spacebar execution sequence:**
1. Validate `TradePlan.Contracts >= 1`
2. Submit entry order (unmanaged)
3. Attach stop market order
4. Attach limit TP order
5. Activate `BreakEvenService`

**Break-even:** Triggered by price event (not polling). When price crosses break-even RR level, move stop to entry price.

## Trade State Machine

```
Idle → Planning → Armed → Submitted → Active → BreakEvenTriggered → Closed
                                              ↘ Cancelled
```
Only explicit transitions. No implicit state skipping.

## Design Patterns

| Pattern | Where used |
|---|---|
| MVVM | UI layer |
| Strategy | `LongStrategy` / `ShortStrategy` for direction-specific logic |
| State | `TradeStateMachine` |
| Command | `ExecuteTradeCommand` (spacebar handler) |
| Observer | Price update subscriptions in `BreakEvenService` / `PriceMonitor` |
| Adapter | Infrastructure layer wrappers |
| Factory | Order creation inside `NinjaOrderAdapter` |

## Performance Constraints

- `OnRender` must be lightweight — no recalculations, no allocations
- Dispose all event handlers on indicator removal
- No UI thread blocking — use dispatcher or async patterns
- Minimize object allocation in hot paths (tick-level callbacks)

## Risk Edge Cases to Handle

- `Contracts == 0` → show error, block execution
- Stop == Entry → divide-by-zero guard
- Account balance insufficient → surface error in ViewModel
- Market gaps / trading halts → order rejection handling in `ExecutionCoordinator`
- Partial fills → `BreakEvenService` must check actual fill price, not order price

## Non-Goals

- Automated/algorithmic strategy execution
- Multi-account support
- Broker abstraction beyond NinjaTrader 8
- AI-based signals
