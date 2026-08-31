using System;
using TradeAssistant.Application.Ports;
using TradeAssistant.Domain;

namespace TradeAssistant.Application
{
    /// <summary>
    /// Executes a TradePlan as a native bracket order set — no ATM strategies:
    ///  1. Submit a market entry order.
    ///  2. On entry fill, submit an OCO-linked stop-market SL + limit TP anchored
    ///     to the ACTUAL fill price (planned SL distance and configured RR preserved).
    /// Drives the TradeStateMachine and reports progress via events.
    /// </summary>
    public sealed class BracketExecutionCoordinator : IDisposable
    {
        private IOrderGateway                  _gateway;
        private readonly TradeStateMachine     _stateMachine;
        private readonly IInstrumentInfoProvider _instrument;

        private TradePlan _pendingPlan;
        private bool      _bracketSubmitted;

        public event Action<string>      ExecutionError;
        public event Action<string>      StatusMessage;
        public event Action<BracketInfo> BracketPlaced;

        public BracketExecutionCoordinator(
            IOrderGateway            gateway,
            TradeStateMachine        stateMachine,
            IInstrumentInfoProvider  instrument)
        {
            _gateway      = gateway      ?? throw new ArgumentNullException(nameof(gateway));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _instrument   = instrument   ?? throw new ArgumentNullException(nameof(instrument));
            Subscribe(_gateway);
        }

        /// <summary>
        /// Arms and submits the market entry for the given plan.
        /// Validation failures and submission errors are reported, never thrown.
        /// </summary>
        public void Execute(TradePlan plan)
        {
            if (plan == null || !plan.IsValid)
            {
                ExecutionError?.Invoke(plan != null ? plan.ValidationError : "No valid trade plan.");
                return;
            }

            if (plan.Contracts < 1)
            {
                ExecutionError?.Invoke("Cannot execute: 0 contracts calculated.");
                return;
            }

            if (_stateMachine.Current != TradeState.Planning)
            {
                ExecutionError?.Invoke($"Cannot execute from state {_stateMachine.Current}.");
                return;
            }

            _pendingPlan      = plan;
            _bracketSubmitted = false;

            try
            {
                _stateMachine.TransitionTo(TradeState.Armed);
                _gateway.SubmitMarketEntry(plan.Direction, plan.Contracts);
                _stateMachine.TransitionTo(TradeState.Submitted);
                StatusMessage?.Invoke($"Entry submitted — {plan.Contracts} contract(s), awaiting fill…");
            }
            catch (Exception ex)
            {
                _pendingPlan = null;
                ExecutionError?.Invoke($"Entry submission failed: {ex.Message}");
                _stateMachine.TryTransitionTo(TradeState.Cancelled);
                _stateMachine.TryTransitionTo(TradeState.Idle);
            }
        }

        /// <summary>Replaces the gateway (e.g. when the ChartTrader account changes).</summary>
        public void ReplaceOrderGateway(IOrderGateway gateway)
        {
            if (gateway == null) throw new ArgumentNullException(nameof(gateway));
            Unsubscribe(_gateway);
            _gateway = gateway;
            Subscribe(_gateway);
        }

        // ── Gateway events ──────────────────────────────────────────────────

        private void OnEntryFilled(double fillPrice)
        {
            if (_pendingPlan == null || _bracketSubmitted) return;

            TradePlan plan = _pendingPlan;
            IDirectionStrategy strategy = plan.Direction == TradeDirection.Long
                ? (IDirectionStrategy)LongStrategy.Instance
                : ShortStrategy.Instance;

            double tick = _instrument.TickSize;
            double slPrice = StopSnapper.SnapToTick(
                plan.Direction == TradeDirection.Long
                    ? fillPrice - plan.SlDistancePoints
                    : fillPrice + plan.SlDistancePoints,
                tick);
            double tpPrice = StopSnapper.SnapToTick(
                strategy.CalcTp(fillPrice, slPrice, plan.RrRatio), tick);

            try
            {
                _gateway.SubmitBracket(slPrice, tpPrice, plan.Contracts);
                _bracketSubmitted = true;
                _stateMachine.TryTransitionTo(TradeState.Active);
                StatusMessage?.Invoke(
                    $"Filled at {fillPrice:F2}. Bracket live — SL {slPrice:F2} / TP {tpPrice:F2}.");
                BracketPlaced?.Invoke(new BracketInfo(
                    fillPrice, slPrice, tpPrice, plan.Contracts, plan.Direction));
            }
            catch (Exception ex)
            {
                // The entry IS filled — the position is open even though the
                // bracket failed. Move to Active so the state reflects reality.
                _stateMachine.TryTransitionTo(TradeState.Active);
                ExecutionError?.Invoke(
                    $"Bracket submission failed: {ex.Message}. " +
                    "Position is OPEN WITHOUT SL/TP — flatten it manually.");
            }
        }

        private void OnOrderRejected(string reason)
        {
            ExecutionError?.Invoke($"Order rejected: {reason}");
            _pendingPlan      = null;
            _bracketSubmitted = false;
            _stateMachine.TryTransitionTo(TradeState.Cancelled);
            _stateMachine.TryTransitionTo(TradeState.Idle);
        }

        private void OnOrderCancelled(string reason)
        {
            // A cancelled ENTRY (before fill) aborts the trade. Cancels of bracket
            // siblings after a close are normal OCO behaviour and are ignored here.
            if (_stateMachine.Current == TradeState.Submitted)
            {
                StatusMessage?.Invoke($"Entry cancelled: {reason}");
                _pendingPlan      = null;
                _bracketSubmitted = false;
                _stateMachine.TryTransitionTo(TradeState.Cancelled);
                _stateMachine.TryTransitionTo(TradeState.Idle);
            }
            else
            {
                StatusMessage?.Invoke($"Order cancelled: {reason}");
            }
        }

        private void OnPositionClosed()
        {
            if (_stateMachine.TryTransitionTo(TradeState.Closed))
                _stateMachine.TryTransitionTo(TradeState.Idle);

            _pendingPlan      = null;
            _bracketSubmitted = false;
            StatusMessage?.Invoke("Position closed. Ready for next trade.");
        }

        // ── Subscription management ─────────────────────────────────────────

        private void Subscribe(IOrderGateway gateway)
        {
            gateway.EntryFilled     += OnEntryFilled;
            gateway.OrderRejected   += OnOrderRejected;
            gateway.OrderCancelled  += OnOrderCancelled;
            gateway.PositionClosed  += OnPositionClosed;
        }

        private void Unsubscribe(IOrderGateway gateway)
        {
            gateway.EntryFilled     -= OnEntryFilled;
            gateway.OrderRejected   -= OnOrderRejected;
            gateway.OrderCancelled  -= OnOrderCancelled;
            gateway.PositionClosed  -= OnPositionClosed;
        }

        public void Dispose()
        {
            if (_gateway != null) Unsubscribe(_gateway);
        }
    }

    /// <summary>Details of the live bracket after the entry fill.</summary>
    public sealed class BracketInfo
    {
        public double         FillPrice { get; }
        public double         StopPrice { get; }
        public double         TpPrice   { get; }
        public int            Contracts { get; }
        public TradeDirection Direction { get; }

        public BracketInfo(double fillPrice, double stopPrice, double tpPrice,
            int contracts, TradeDirection direction)
        {
            FillPrice = fillPrice;
            StopPrice = stopPrice;
            TpPrice   = tpPrice;
            Contracts = contracts;
            Direction = direction;
        }
    }
}
