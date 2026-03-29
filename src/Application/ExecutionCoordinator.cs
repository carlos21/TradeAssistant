using System;
using TradeAssistant.Domain;
using TradeAssistant.Infrastructure;

namespace TradeAssistant.Application
{
    /// <summary>
    /// Orchestrates the full order submission sequence on spacebar press.
    ///
    /// Flow:
    ///   1. ValidatePlan (contracts >= 1)
    ///   2. StateMachine: Planning → Armed (caller responsibility before calling Execute)
    ///   3. SubmitEntry (market order)
    ///   4. On OrderFilled: submit stop market + limit TP
    ///   5. StateMachine: Submitted → Active
    ///   6. Activate BreakEvenService
    /// </summary>
    public sealed class ExecutionCoordinator
    {
        private readonly IOrderAdapter      _orders;
        private readonly TradeStateMachine  _stateMachine;
        private readonly BreakEvenService   _breakEvenService;

        private TradePlan _pendingPlan;

        public event Action<string> ExecutionError;
        public event Action<string> StatusMessage;

        public ExecutionCoordinator(
            IOrderAdapter      orders,
            TradeStateMachine  stateMachine,
            BreakEvenService   breakEvenService)
        {
            _orders           = orders           ?? throw new ArgumentNullException(nameof(orders));
            _stateMachine     = stateMachine     ?? throw new ArgumentNullException(nameof(stateMachine));
            _breakEvenService = breakEvenService ?? throw new ArgumentNullException(nameof(breakEvenService));

            _orders.EntryFilled    += OnEntryFilled;
            _orders.OrderCancelled += OnOrderCancelled;
            _orders.PositionClosed += OnPositionClosed;
        }

        /// <summary>
        /// Called on spacebar. Validates and submits the entry order.
        /// Assumes state machine is already in Armed state.
        /// </summary>
        public void Execute(TradePlan plan)
        {
            if (plan == null || !plan.IsValid)
            {
                ExecutionError?.Invoke(plan?.ValidationError ?? "No valid trade plan.");
                return;
            }

            if (plan.Contracts < 1)
            {
                ExecutionError?.Invoke("Cannot execute: 0 contracts calculated.");
                return;
            }

            _pendingPlan = plan;

            try
            {
                _orders.SubmitEntry(plan);
                _stateMachine.TransitionTo(TradeState.Submitted);
                StatusMessage?.Invoke($"Submitted market {(plan.EntryPrice > 0 ? "order" : "")} — {plan.Contracts} contract(s). Waiting for fill…");
            }
            catch (Exception ex)
            {
                ExecutionError?.Invoke($"Order submission failed: {ex.Message}");
                _stateMachine.TryTransitionTo(TradeState.Cancelled);
            }
        }

        private void OnEntryFilled(double fillPrice)
        {
            if (_pendingPlan == null) return;

            try
            {
                // Adjust stop/TP to use the actual fill price if market moved
                _orders.SubmitBracket(_pendingPlan.StopPrice, _pendingPlan.TpPrice, _pendingPlan.Contracts);
                _stateMachine.TransitionTo(TradeState.Active);
                StatusMessage?.Invoke(
                    $"Filled at {fillPrice:F2}. Stop: {_pendingPlan.StopPrice:F2}, TP: {_pendingPlan.TpPrice:F2}");

                _breakEvenService.Start(_pendingPlan.BreakEvenPrice, fillPrice);
            }
            catch (Exception ex)
            {
                ExecutionError?.Invoke($"Bracket order failed after fill: {ex.Message}");
            }
        }

        private void OnOrderCancelled(string reason)
        {
            StatusMessage?.Invoke($"Order cancelled: {reason}");
            if (_stateMachine.TryTransitionTo(TradeState.Cancelled))
                _stateMachine.TryTransitionTo(TradeState.Idle);

            _pendingPlan = null;
        }

        private void OnPositionClosed()
        {
            _breakEvenService.Stop();

            if (_stateMachine.TryTransitionTo(TradeState.Closed))
                _stateMachine.TryTransitionTo(TradeState.Idle);

            _pendingPlan = null;
            StatusMessage?.Invoke("Position closed. Ready for next trade.");
        }

        public void Dispose()
        {
            _orders.EntryFilled    -= OnEntryFilled;
            _orders.OrderCancelled -= OnOrderCancelled;
            _orders.PositionClosed -= OnPositionClosed;
        }
    }
}
