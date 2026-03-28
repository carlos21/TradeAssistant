using System;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using TradeAssistant.Domain;

namespace TradeAssistant.Infrastructure
{
    /// <summary>
    /// Submits and manages unmanaged orders through the NinjaTrader Account API.
    ///
    /// Unmanaged orders allow direct bracket control after the entry fill.
    /// All order operations must be called from the NT UI thread or dispatched appropriately.
    /// </summary>
    public sealed class NinjaOrderAdapter : IOrderAdapter, IDisposable
    {
        private readonly Indicator _indicator;

        private Order _entryOrder;
        private Order _stopOrder;
        private Order _tpOrder;
        private int   _contracts;
        private bool  _entryFilledFired;

        public event Action<double> EntryFilled;
        public event Action<string> OrderCancelled;
        public event Action         PositionClosed;

        public NinjaOrderAdapter(Indicator indicator)
        {
            _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
            _indicator.Account.OrderUpdate += OnOrderUpdate;
        }

        public void SubmitEntry(TradePlan plan)
        {
            _contracts        = plan.Contracts;
            _entryFilledFired = false;

            OrderAction action = plan.EntryPrice > 0   // direction inferred from plan context
                ? OrderAction.Buy                       // adjusted below
                : OrderAction.Buy;

            // Determine direction from stop vs entry relationship
            // (plan does not carry direction directly to keep infra decoupled from domain enum)
            bool isLong = plan.StopPrice < plan.EntryPrice;
            action = isLong ? OrderAction.Buy : OrderAction.SellShort;

            _entryOrder = _indicator.Account.CreateOrder(
                _indicator.Instrument,
                action,
                OrderType.Market,
                OrderEntry.Automatic,
                TimeInForce.Day,
                _contracts,
                0, 0,
                null,
                "TA_Entry",
                DateTime.MaxValue,
                null);

            _indicator.Account.Submit(new[] { _entryOrder });
        }

        public void SubmitBracket(double stopPrice, double tpPrice, int contracts)
        {
            bool isLong = _entryOrder != null && _entryOrder.OrderAction == OrderAction.Buy;

            _stopOrder = _indicator.Account.CreateOrder(
                _indicator.Instrument,
                isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.StopMarket,
                OrderEntry.Automatic,
                TimeInForce.GoodTillCancelled,
                contracts,
                0, stopPrice,
                null,
                "TA_Stop",
                DateTime.MaxValue,
                null);

            _tpOrder = _indicator.Account.CreateOrder(
                _indicator.Instrument,
                isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.Limit,
                OrderEntry.Automatic,
                TimeInForce.GoodTillCancelled,
                contracts,
                tpPrice, 0,
                null,
                "TA_TP",
                DateTime.MaxValue,
                null);

            _indicator.Account.Submit(new[] { _stopOrder, _tpOrder });
        }

        public void ModifyStop(double newStopPrice)
        {
            if (_stopOrder == null) return;

            // Account.Change(orders, limitPrice, stopPrice, quantity)
            _indicator.Account.Change(new[] { _stopOrder }, 0, newStopPrice, _contracts);
        }

        public void CancelAll()
        {
            if (_entryOrder != null) TryCancel(_entryOrder);
            if (_stopOrder  != null) TryCancel(_stopOrder);
            if (_tpOrder    != null) TryCancel(_tpOrder);
        }

        private void TryCancel(Order order)
        {
            if (order.OrderState == OrderState.Working ||
                order.OrderState == OrderState.Accepted ||
                order.OrderState == OrderState.PartFilled)
            {
                _indicator.Account.Cancel(new[] { order });
            }
        }

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            Order order = e.Order;

            // Entry order filled
            if (!_entryFilledFired &&
                _entryOrder != null &&
                order.Name  == "TA_Entry" &&
                order.OrderState == OrderState.Filled)
            {
                _entryFilledFired = true;
                double fillPrice  = order.AverageFillPrice;
                EntryFilled?.Invoke(fillPrice);
                return;
            }

            // Cancellation / rejection of any of our orders
            if ((order.Name == "TA_Entry" || order.Name == "TA_Stop" || order.Name == "TA_TP") &&
                (order.OrderState == OrderState.Cancelled || order.OrderState == OrderState.Rejected))
            {
                OrderCancelled?.Invoke($"{order.Name} {order.OrderState}");
                return;
            }

            // TP or stop hit → position closed
            if ((order.Name == "TA_Stop" || order.Name == "TA_TP") &&
                order.OrderState == OrderState.Filled)
            {
                PositionClosed?.Invoke();
            }
        }

        public void Dispose()
        {
            if (_indicator?.Account != null)
                _indicator.Account.OrderUpdate -= OnOrderUpdate;

            CancelAll();
        }
    }
}
