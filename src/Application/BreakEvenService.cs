using System;
using TradeAssistant.Domain;
using TradeAssistant.Infrastructure;

namespace TradeAssistant.Application
{
    /// <summary>
    /// Subscribes to price ticks. When price reaches the break-even RR level,
    /// moves the stop to the entry price.
    ///
    /// Uses event-driven price updates — never polls.
    /// </summary>
    public sealed class BreakEvenService
    {
        private readonly IOrderAdapter     _orders;
        private readonly TradeStateMachine _stateMachine;

        private bool   _isActive;
        private double _breakEvenPrice;
        private double _entryPrice;
        private TradeDirection _direction;

        public event Action<string> StatusMessage;

        public BreakEvenService(IOrderAdapter orders, TradeStateMachine stateMachine)
        {
            _orders       = orders       ?? throw new ArgumentNullException(nameof(orders));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        }

        /// <summary>
        /// Activates BE monitoring. Called after entry fill with the actual fill price.
        /// </summary>
        public void Start(double breakEvenPrice, double fillPrice)
        {
            _breakEvenPrice = breakEvenPrice;
            _entryPrice     = fillPrice;
            _direction      = _stateMachine.Current == TradeState.Active
                ? TradeDirection.Long   // direction is embedded in breakEvenPrice relationship
                : TradeDirection.Long;

            _isActive = true;
        }

        public void Stop() => _isActive = false;

        /// <summary>
        /// Called by the indicator on every price update (OnBarUpdate / tick callback).
        /// Must be fast — no allocations, no blocking.
        /// </summary>
        public void OnPriceUpdate(double currentPrice, TradeDirection direction)
        {
            if (!_isActive) return;
            if (_stateMachine.Current != TradeState.Active) return;

            bool triggered = direction == TradeDirection.Long
                ? currentPrice >= _breakEvenPrice
                : currentPrice <= _breakEvenPrice;

            if (!triggered) return;

            _isActive = false;

            try
            {
                _orders.ModifyStop(_entryPrice);
                _stateMachine.TransitionTo(TradeState.BreakEvenTriggered);
                StatusMessage?.Invoke($"Break-even triggered. Stop moved to entry: {_entryPrice:F2}");
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"BE stop modification failed: {ex.Message}");
                _isActive = false;
            }
        }
    }
}
