using System;
using TradeAssistant.Domain;
using TradeAssistant.Infrastructure;

namespace TradeAssistant.Application
{
    /// <summary>
    /// Orchestrates order submission on spacebar press.
    /// 
    /// Supports two modes:
    ///   1. Legacy: Unmanaged entry + manual bracket orders
    ///   2. ATM: Entry with attached ATM strategy for native ChartTrader visualization
    /// </summary>
    public sealed class ExecutionCoordinator
    {
        private readonly IOrderAdapter      _orders;
        private readonly TradeStateMachine  _stateMachine;

        private TradePlan _pendingPlan;
        private string    _pendingAtmStrategy;

        public event Action<string> ExecutionError;
        public event Action<string> StatusMessage;

        public ExecutionCoordinator(
            IOrderAdapter      orders,
            TradeStateMachine  stateMachine)
        {
            _orders           = orders           ?? throw new ArgumentNullException(nameof(orders));
            _stateMachine     = stateMachine     ?? throw new ArgumentNullException(nameof(stateMachine));

            _orders.EntryFilled    += OnEntryFilled;
            _orders.OrderCancelled += OnOrderCancelled;
            _orders.PositionClosed += OnPositionClosed;
        }

        /// <summary>
        /// Execute with ATM strategy attachment.
        /// Provides native ChartTrader SL/TP visualization.
        /// </summary>
        public void ExecuteWithAtm(TradePlan plan, string atmStrategyName)
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

            if (string.IsNullOrEmpty(atmStrategyName))
            {
                ExecutionError?.Invoke("ATM strategy not selected.");
                return;
            }

            _pendingPlan = plan;
            _pendingAtmStrategy = atmStrategyName;

            try
            {
                _orders.StartAtmStrategy(atmStrategyName, plan);
                _stateMachine.TransitionTo(TradeState.Submitted);
                StatusMessage?.Invoke($"ATM '{atmStrategyName}' — {plan.Contracts} contract(s). Waiting for fill…");
            }
            catch (Exception ex)
            {
                ExecutionError?.Invoke($"ATM execution failed: {ex.Message}");
                _stateMachine.TryTransitionTo(TradeState.Cancelled);
            }
        }

        /// <summary>
        /// Legacy: Unmanaged entry without ATM.
        /// Kept for backward compatibility.
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
            _pendingAtmStrategy = null;

            try
            {
                _orders.SubmitEntry(plan);
                _stateMachine.TransitionTo(TradeState.Submitted);
                StatusMessage?.Invoke($"Submitted market order — {plan.Contracts} contract(s). Waiting for fill…");
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

            // If using ATM strategy, ATM handles SL/TP automatically
            if (!string.IsNullOrEmpty(_pendingAtmStrategy))
            {
                _stateMachine.TransitionTo(TradeState.Active);
                StatusMessage?.Invoke(
                    $"Filled at {fillPrice:F2}. ATM '{_pendingAtmStrategy}' managing SL/TP.");
                
                // Note: Break-even with ATM is handled by ATM template settings
                // We don't activate our break-even service when using ATM
                return;
            }

            // Legacy: Manual bracket submission
            try
            {
                _orders.SubmitBracket(_pendingPlan.StopPrice, _pendingPlan.TpPrice, _pendingPlan.Contracts);
                _stateMachine.TransitionTo(TradeState.Active);
                StatusMessage?.Invoke(
                    $"Filled at {fillPrice:F2}. Stop: {_pendingPlan.StopPrice:F2}, TP: {_pendingPlan.TpPrice:F2}");
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
            _pendingAtmStrategy = null;
        }

        private void OnPositionClosed()
        {
            if (_stateMachine.TryTransitionTo(TradeState.Closed))
                _stateMachine.TryTransitionTo(TradeState.Idle);

            _pendingPlan = null;
            _pendingAtmStrategy = null;
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
