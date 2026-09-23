# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A NinjaTrader 8 AddOn/Indicator for manual futures trade planning and execution. Targets NQ, MNQ, ES, MES. Provides TradingView-style interactive risk visualization with strict futures contract mechanics. Not an automated strategy. **No ATM strategies** — execution is a native bracket: market entry, then OCO-linked SL (stop-market) + TP (limit) anchored to the actual fill price.

## Git Workflow

Never ask to commit or push. The user manages git themselves.

## Build & Development

This is a C# NinjaTrader 8 AddOn. NinjaTrader compiles AddOns internally via its IDE or the NinjaScript Editor. There is no standalone `.csproj` for `src/`.

**Repo structure:** All compilable NT8 source lives under `src/`. A symlink connects it to NinjaTrader:
```
src/ → ~/Documents/NinjaTrader 8/bin/Custom/AddOns/TradeAssistant/
```

**To compile:** Open NinjaTrader → NinjaScript Editor → right-click project → Compile. Compilation errors appear in the Output window.

**To run tests:** Domain, Application, and the ViewModel have no NinjaTrader dependencies and are covered by an xUnit suite under `tests/`:
```
tests\run-coverage.ps1        # Windows PowerShell
# or directly:
dotnet test tests\TradeAssistant.Tests\TradeAssistant.Tests.csproj -p:CollectCoverage=true -p:Threshold=98 -p:ThresholdType=line -p:ThresholdStat=total
```
- `tests/TradeAssistant.Core` links the testable sources (`src/Domain/**`, `src/Application/**`, `ViewModelBase`, `RelayCommand`, `TradePlannerViewModel`) into a net8.0-windows library compiled with **LangVersion 7.3** — this guards .NET Framework 4.8 / NT8 compatibility. Do not use newer C# features in `src/`.
- Coverage gate: **≥98% line coverage** on `TradeAssistant.Core` (coverlet; the build fails below the threshold).
- **Compile check (run after ANY change to `src/`):** `dotnet build tests/TradeAssistant.CompileCheck` compiles ALL of `src/` — including Infrastructure and the indicator — against the real NT8 assemblies (`NinjaTrader.Core.dll`, `NinjaTrader.Gui.dll`, SharpDX, net48). It uses `tests/TradeAssistant.CompileCheck/Nt8Stubs.cs`, a stub for the `Indicator` partial class that NT8 normally generates into `NinjaTrader.Custom.dll`. This catches NT8 API/namespace errors locally without opening the NinjaScript Editor.

## Architecture

Four strict layers — no upward dependencies allowed:

```
UI (WPF/MVVM)
    ↓
Application Layer  (+ Ports: interfaces it consumes)
    ↓
Domain Layer  ←  fully NinjaTrader-free, unit-testable
    ↓
Infrastructure Layer  ←  wraps NinjaTrader API
```

### Domain Layer (`/src/Domain`)
Pure C# logic with zero NinjaTrader references. All classes here must be unit-testable in isolation.
- `RiskCalculator` — converts account risk % or fixed $ to a dollar risk amount
- `FuturesPositionSizer` — applies the position sizing formula (see below)
- `StopSnapper` — **SL distance is always a positive multiple of 5 points** (`SnapDistance`, min one step); `SnapToTick` aligns prices to the instrument tick
- `LongStrategy` / `ShortStrategy` — direction-specific TP/BE math behind `IDirectionStrategy`
- `TradeConfiguration` — immutable value object holding all user inputs (risk, RR, break-even RR, SL step, default SL)
- `TradePlan` — computed result: contracts, risk $, profit $, SL/TP prices, SL distance, RR, direction
- `TradeStateMachine` — enforces explicit state transitions (see states below)

**Position sizing formula (must never deviate):**
```
StopDistanceTicks = |Entry - Stop| / TickSize
RiskPerContract   = StopDistanceTicks × TickValue
Contracts         = floor(RiskAmount / RiskPerContract)   // integer, >= 1
Min-risk rule     = if Contracts >= 1 and realized risk < $100 (TradeConfiguration.MinRiskDollars),
                    add 1 contract; if Contracts == 0 but one contract risks >= $100, take 1.
```

### Application Layer (`/src/Application`)
Coordinates domain logic with UI and infrastructure. No direct NinjaTrader API calls.
- `TradePlannerController` — owns inputs; SL distance snapped to 5-pt steps; **TP always derived as SL distance × configured RR (RR never mutates)**; `NudgeStop(±1)` for the ▲▼ panel buttons; emits `PlanUpdated`
- `BracketExecutionCoordinator` — market entry → on fill submits OCO bracket anchored to `AverageFillPrice`; drives the state machine
- `BreakEvenMonitor` — event-driven via `IPriceFeed` (never polls); auto-moves stop to entry at the configured RR multiple; manual `MoveToBreakEven()` for the B key / panel button
- `ChartInteractionController` — testable drag state machine: X-bounds + Y-tolerance hit test, idempotent begin, unconditional end/cancel (fixes stuck-drag bug)
- `Ports/` — `IInstrumentInfoProvider`, `IAccountDataProvider`, `IOrderGateway`, `IPriceFeed`, `IPriceAxisConverter`

### Infrastructure Layer (`/src/Infrastructure`)
Thin adapters over NinjaTrader API. Instrument tick values must always come from `Instrument.MasterInstrument.*` — never hardcoded.
- `NinjaOrderGateway` — unmanaged orders via `Account.CreateOrder` + `Account.Submit`; entry named `"Entry"`; SL/TP share a generated OCO id; translates `OrderUpdate` into gateway events
- `NinjaPriceFeed` — wraps `Bars.Instrument.MarketData.Update` (last-price events)
- `NinjaInstrumentInfoProvider` — exposes TickSize, TickValue, PointValue
- `NinjaAccountDataProvider` — reads account balance

### UI Layer (`/src/UI`)
WPF panel embedded in the chart, strict MVVM. No trading logic in views or code-behind.
- `TradePlannerViewModel` — all bindable properties and commands; no NinjaTrader refs (unit-tested)
- `TradePlannerView` — **two surfaces must stay in sync**: code-built `BuildLayout()` in `TradePlannerView.xaml.cs` (primary, on chart) and `TradePlannerView.xaml` markup

### Indicator (`/src/TradeAssistantIndicator.cs`)
Thin composition root: wires layers, translates chart mouse/keyboard events into controller calls, renders zones/labels allocation-free. Hotkeys: **B** manual break-even. SL adjusts via drag or the ▲▼ panel buttons (±5 pts) — ↑/↓ keep NT8 default chart scrolling. Execution is triggered from the panel's EXECUTE button.

## Key Behavioral Rules

**Instrument values:** Always read from `Instrument.MasterInstrument.TickSize`, `.TickValue`, `.PointValue`. Never hardcode NQ/MNQ/ES/MES tick sizes.

**Contracts:** Always `int`, always `>= 1`. Use `Math.Floor`. Guard against zero (account too small / stop too tight). Realized risk (contracts × risk-per-contract) must never fall below `TradeConfiguration.MinRiskDollars` ($100) — the sizer adds one contract, or rescues a 0-contract result to 1 contract, to honor the floor.

**Chart interactivity:** Only the SL line is draggable. Dragging SL → distance snapped to a multiple of 5 pts → TP re-derived from configured RR. TP is never draggable.

**Bracket execution sequence (no ATM), triggered by the panel EXECUTE button:**
1. Validate `TradePlan.IsValid` and `Contracts >= 1`
2. Submit market entry (unmanaged, name `"Entry"`)
3. On entry fill: SL = fill ∓ snapped SL distance, TP = fill ± distance × RR (both tick-snapped)
4. Submit SL (stop-market) + TP (limit) with a shared OCO id
5. Activate `BreakEvenMonitor` with the actual fill price

**Break-even:** Auto when price crosses fill ± distance × BreakEvenRr (event-driven, once), or manual via **B** key / panel button — moves the live stop order to the fill price via `Account.Change`.

## Trade State Machine

```
Idle → Planning → Armed → Submitted → Active → BreakEvenTriggered → Closed
                       ↘ Cancelled  ↗            ↘ Cancelled
```
Only explicit transitions. No implicit state skipping. (`Armed → Cancelled` allowed for entry-submission rollback.)

## Design Patterns

| Pattern | Where used |
|---|---|
| MVVM | UI layer |
| Strategy | `LongStrategy` / `ShortStrategy` for direction-specific logic |
| State | `TradeStateMachine`, `ChartInteractionController` (drag state) |
| Command | `RelayCommand` (panel buttons / hotkeys) |
| Observer | `IPriceFeed` events in `BreakEvenMonitor`; `IOrderGateway` events in `BracketExecutionCoordinator` |
| Adapter | Infrastructure layer wrappers; `ChartScaleAxisConverter` in the indicator |
| Factory | Order creation inside `NinjaOrderGateway` |
| Ports & Adapters | `Application/Ports` interfaces implemented by Infrastructure |

## Performance Constraints

- `OnRender` must be lightweight — no recalculations, no allocations
- Dispose all event handlers on indicator removal (`State.Terminated`)
- No UI thread blocking — use dispatcher or async patterns
- Minimize object allocation in hot paths (tick-level callbacks)

## Risk Edge Cases to Handle

- `Contracts == 0` → show error, block execution
- Stop == Entry → divide-by-zero guard in sizer
- Account balance insufficient → surface error in ViewModel
- Market gaps / trading halts → order rejection handling in `BracketExecutionCoordinator` (`Cancelled → Idle` rollback)
- Bracket submission failure after fill → state moves to `Active`, loud error: position open without SL/TP
- Mouse capture stolen mid-drag → `LostMouseCapture` cancels drag; interaction controller can never get stuck

## Non-Goals

- Automated/algorithmic strategy execution
- ATM strategies (removed — native OCO brackets only)
- Multi-account support
- Broker abstraction beyond NinjaTrader 8
- AI-based signals
