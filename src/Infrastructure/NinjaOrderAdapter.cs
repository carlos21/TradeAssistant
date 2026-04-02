using System;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using TradeAssistant.Domain;

namespace TradeAssistant.Infrastructure
{
    /// <summary>
    /// Submits orders through the NinjaTrader Account API.
    /// Supports both unmanaged orders and ATM strategy attachment.
    /// </summary>
    public sealed class NinjaOrderAdapter : IOrderAdapter, IDisposable
    {
        private readonly Indicator _indicator;
        private readonly Account   _account;

        private Order _entryOrder;
        private int   _contracts;
        private bool  _entryFilledFired;
        private string _currentAtmStrategy;

        public event Action<double> EntryFilled;
        public event Action<string> OrderCancelled;
        public event Action         PositionClosed;

        public NinjaOrderAdapter(Indicator indicator, Account account)
        {
            _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
            _account   = account   ?? throw new ArgumentNullException(nameof(account));
            _account.OrderUpdate += OnOrderUpdate;
        }

        /// <summary>
        /// Submits a market entry order attached to an ATM strategy.
        /// The ATM strategy manages SL/TP orders automatically.
        /// </summary>
        public void StartAtmStrategy(string atmStrategyName, TradePlan plan)
        {
            if (string.IsNullOrEmpty(atmStrategyName))
                throw new ArgumentException("ATM strategy name is required", nameof(atmStrategyName));

            if (_account == null)
                throw new InvalidOperationException("Account is not available. Make sure ChartTrader is enabled and an account is selected.");

            if (_indicator?.Instrument == null)
                throw new InvalidOperationException("Instrument is not available.");

            _contracts        = plan.Contracts;
            _entryFilledFired = false;
            _currentAtmStrategy = atmStrategyName;

            bool isLong = plan.StopPrice < plan.EntryPrice;
            OrderAction action = isLong ? OrderAction.Buy : OrderAction.SellShort;

            try
            {
                // Create entry order - name MUST be "Entry" for ATM to work
                _entryOrder = _account.CreateOrder(
                    _indicator.Instrument,
                    action,
                    OrderType.Market,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    _contracts,
                    0, 0,
                    null,
                    "Entry",  // CRITICAL: Must be exactly "Entry" for ATM
                    DateTime.MaxValue,
                    null);

                if (_entryOrder == null)
                    throw new InvalidOperationException("Failed to create entry order. Check account connection and instrument.");

                // Start ATM strategy - this submits the entry and manages SL/TP
                NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(atmStrategyName, _entryOrder);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to start ATM strategy '{atmStrategyName}': {ex.Message}. Make sure the ATM strategy template exists in NinjaTrader (Chart Trader > ATM Strategy).", ex);
            }
        }

        /// <summary>
        /// Legacy method for unmanaged entry without ATM.
        /// Kept for backward compatibility.
        /// </summary>
        public void SubmitEntry(TradePlan plan)
        {
            _contracts        = plan.Contracts;
            _entryFilledFired = false;
            _currentAtmStrategy = null;

            bool isLong = plan.StopPrice < plan.EntryPrice;
            OrderAction action = isLong ? OrderAction.Buy : OrderAction.SellShort;

            _entryOrder = _account.CreateOrder(
                _indicator.Instrument,
                action,
                OrderType.Market,
                OrderEntry.Manual,
                TimeInForce.Day,
                _contracts,
                0, 0,
                null,
                "TA_Entry",
                DateTime.MaxValue,
                null);

            _account.Submit(new[] { _entryOrder });
        }

        /// <summary>
        /// No longer used with ATM strategy approach.
        /// ATM strategies handle bracket orders automatically.
        /// </summary>
        [Obsolete("Use StartAtmStrategy instead. ATM strategies handle brackets automatically.")]
        public void SubmitBracket(double stopPrice, double tpPrice, int contracts)
        {
            // ATM strategies manage SL/TP automatically
            // This method is kept for interface compatibility but does nothing
        }

        /// <summary>
        /// Modify stop is handled by ATM strategy when used.
        /// </summary>
        public void ModifyStop(double newStopPrice)
        {
            // With ATM strategies, users modify stops via ChartTrader drag
            // Manual modification not supported when using ATM
            if (string.IsNullOrEmpty(_currentAtmStrategy))
            {
                // Only for legacy non-ATM mode
                // Implementation removed as ATM is now primary
            }
        }

        public void CancelAll()
        {
            if (_entryOrder != null) TryCancel(_entryOrder);
        }

        private void TryCancel(Order order)
        {
            if (order.OrderState == OrderState.Working ||
                order.OrderState == OrderState.Accepted ||
                order.OrderState == OrderState.PartFilled)
            {
                _account.Cancel(new[] { order });
            }
        }

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            Order order = e.Order;

            // Track entry fill for both ATM and non-ATM modes
            if (!_entryFilledFired &&
                _entryOrder != null &&
                order.Name == _entryOrder.Name &&
                order.OrderState == OrderState.Filled)
            {
                _entryFilledFired = true;
                EntryFilled?.Invoke(order.AverageFillPrice);
                return;
            }

            // Handle cancellations
            if (order.Name == _entryOrder?.Name &&
                (order.OrderState == OrderState.Cancelled || order.OrderState == OrderState.Rejected))
            {
                OrderCancelled?.Invoke($"{order.Name} {order.OrderState}");
                return;
            }

            // With ATM strategies, position close is tracked via ATM events
            // We can detect this by monitoring for stop/target fills
            if (order.OrderState == OrderState.Filled &&
                (order.Name.Contains("Stop") || order.Name.Contains("Target") || order.Name.Contains("TP")))
            {
                PositionClosed?.Invoke();
            }
        }

        public void Dispose()
        {
            _account.OrderUpdate -= OnOrderUpdate;
            CancelAll();
        }
    }
}
