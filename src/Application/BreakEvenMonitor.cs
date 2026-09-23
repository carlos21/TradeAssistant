using System;
using TradeAssistant.Application.Ports;
using TradeAssistant.Domain;

namespace TradeAssistant.Application
{
    /// <summary>Outcome of a manual break-even attempt, so callers can explain rejections.</summary>
    public enum BreakEvenResult
    {
        /// <summary>Stop moved to the fill price.</summary>
        Moved,
        /// <summary>No active trade — the state machine is not in Active.</summary>
        NoActiveTrade,
        /// <summary>Break-even was already applied to this position.</summary>
        AlreadyAtBreakEven,
        /// <summary>Bracket not placed yet (no fill price) or the stop order move failed.</summary>
        NoWorkingStop
    }

    /// <summary>
    /// Moves the live stop order to break-even — automatically when price reaches
    /// the configured RR multiple beyond the fill price, or manually on demand
    /// (button / key). Event-driven via IPriceFeed; never polls.
    /// </summary>
    public sealed class BreakEvenMonitor : IDisposable
    {
        private readonly IPriceFeed        _priceFeed;
        private IOrderGateway              _gateway;
        private readonly TradeStateMachine _stateMachine;

        private IDirectionStrategy _strategy;
        private double _fillPrice;
        private double _triggerPrice;
        private bool   _armed;
        private bool   _triggered;

        public event Action<string> StatusMessage;
        public event Action<string> Error;

        public BreakEvenMonitor(IPriceFeed priceFeed, IOrderGateway gateway, TradeStateMachine stateMachine)
        {
            _priceFeed    = priceFeed    ?? throw new ArgumentNullException(nameof(priceFeed));
            _gateway      = gateway      ?? throw new ArgumentNullException(nameof(gateway));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _priceFeed.PriceUpdated += OnPriceUpdated;
        }

        public bool   IsArmed      => _armed;
        public bool   IsTriggered  => _triggered;
        public double TriggerPrice => _armed ? _triggerPrice : 0;

        /// <summary>
        /// Starts monitoring after the bracket is live. fillPrice is the ACTUAL
        /// entry fill; stopPrice the live stop. beRr &lt;= 0 disables the
        /// automatic trigger (manual break-even stays available).
        /// </summary>
        public void Activate(double fillPrice, double stopPrice, double beRr, TradeDirection direction)
        {
            _strategy  = direction == TradeDirection.Long
                ? (IDirectionStrategy)LongStrategy.Instance
                : ShortStrategy.Instance;
            _fillPrice = fillPrice;
            _triggered = false;

            if (beRr <= 0)
            {
                _armed        = false;
                _triggerPrice = 0;
                return;
            }

            _triggerPrice = _strategy.CalcBreakEven(fillPrice, stopPrice, beRr);
            _armed        = true;
        }

        /// <summary>Stops monitoring (e.g. position closed / trade reset).</summary>
        public void Deactivate()
        {
            _armed     = false;
            _triggered = false;
        }

        /// <summary>
        /// Manual break-even. Returns <see cref="BreakEvenResult.Moved"/> only when
        /// the stop was actually moved; any other value explains why it was not.
        /// </summary>
        public BreakEvenResult MoveToBreakEven()
        {
            if (_fillPrice <= 0 || _strategy == null) return BreakEvenResult.NoWorkingStop;
            if (_triggered) return BreakEvenResult.AlreadyAtBreakEven;
            if (_stateMachine.Current != TradeState.Active) return BreakEvenResult.NoActiveTrade;

            try
            {
                _gateway.MoveStopTo(_fillPrice);
                _triggered = true;
                _armed     = false;
                _stateMachine.TryTransitionTo(TradeState.BreakEvenTriggered);
                StatusMessage?.Invoke($"Stop moved to break-even ({_fillPrice:F2}).");
                return BreakEvenResult.Moved;
            }
            catch (Exception ex)
            {
                Error?.Invoke($"Break-even failed: {ex.Message}");
                return BreakEvenResult.NoWorkingStop;
            }
        }

        /// <summary>Replaces the gateway (e.g. when the ChartTrader account changes).</summary>
        public void ReplaceOrderGateway(IOrderGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        private void OnPriceUpdated(double price)
        {
            if (!_armed || _triggered || _strategy == null) return;
            if (_stateMachine.Current != TradeState.Active) return;

            if (_strategy.IsPriceAtOrBeyondBreakEven(price, _triggerPrice))
                MoveToBreakEven();
        }

        public void Dispose()
        {
            _priceFeed.PriceUpdated -= OnPriceUpdated;
        }
    }
}
