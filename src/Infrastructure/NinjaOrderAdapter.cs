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
    /// Indicators do not expose an Account property — the Account must be
    /// provided externally (e.g. from ChartControl.OwnerChart.ChartTrader.Account).
    /// </summary>
    public sealed class NinjaOrderAdapter : IOrderAdapter, IDisposable
    {
        private readonly Indicator _indicator;
        private readonly Account   _account;

        private Order _entryOrder;
        private Order _stopOrder;
        private Order _tpOrder;
        private int   _contracts;
        private bool  _entryFilledFired;

        public event Action<double> EntryFilled;
        public event Action<string> OrderCancelled;
        public event Action         PositionClosed;

        public NinjaOrderAdapter(Indicator indicator, Account account)
        {
            _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
            _account   = account   ?? throw new ArgumentNullException(nameof(account));
            _account.OrderUpdate += OnOrderUpdate;
        }

        public void SubmitEntry(TradePlan plan)
        {
            _contracts        = plan.Contracts;
            _entryFilledFired = false;

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

        public void SubmitBracket(double stopPrice, double tpPrice, int contracts)
        {
            bool isLong = _entryOrder != null && _entryOrder.OrderAction == OrderAction.Buy;

            _stopOrder = _account.CreateOrder(
                _indicator.Instrument,
                isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.StopMarket,
                OrderEntry.Manual,
                TimeInForce.Gtc,
                contracts,
                0, stopPrice,
                null,
                "TA_Stop",
                DateTime.MaxValue,
                null);

            _tpOrder = _account.CreateOrder(
                _indicator.Instrument,
                isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.Limit,
                OrderEntry.Manual,
                TimeInForce.Gtc,
                contracts,
                tpPrice, 0,
                null,
                "TA_TP",
                DateTime.MaxValue,
                null);

            _account.Submit(new[] { _stopOrder, _tpOrder });
        }

        public void ModifyStop(double newStopPrice)
        {
            if (_stopOrder == null) return;
            _stopOrder.StopPriceChanged = newStopPrice;
            _account.Change(new[] { _stopOrder });
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
                _account.Cancel(new[] { order });
            }
        }

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            Order order = e.Order;

            if (!_entryFilledFired &&
                _entryOrder != null &&
                order.Name  == "TA_Entry" &&
                order.OrderState == OrderState.Filled)
            {
                _entryFilledFired = true;
                EntryFilled?.Invoke(order.AverageFillPrice);
                return;
            }

            if ((order.Name == "TA_Entry" || order.Name == "TA_Stop" || order.Name == "TA_TP") &&
                (order.OrderState == OrderState.Cancelled || order.OrderState == OrderState.Rejected))
            {
                OrderCancelled?.Invoke($"{order.Name} {order.OrderState}");
                return;
            }

            if ((order.Name == "TA_Stop" || order.Name == "TA_TP") &&
                order.OrderState == OrderState.Filled)
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
