using System;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using TradeAssistant.Application.Ports;
using TradeAssistant.Domain;

namespace TradeAssistant.Infrastructure
{
    /// <summary>
    /// Submits unmanaged orders through the NinjaTrader Account API.
    /// Bracket mechanics (no ATM strategies):
    ///  - Entry: market order named "Entry", submitted via Account.Submit.
    ///  - Bracket: stop-market SL + limit TP sharing a generated OCO id, so
    ///    whichever fills first cancels the other automatically.
    /// All OrderUpdate events are translated into IOrderGateway events.
    /// </summary>
    public sealed class NinjaOrderGateway : IOrderGateway, IDisposable
    {
        private readonly Indicator _indicator;
        private readonly Account   _account;

        private Order  _entryOrder;
        private Order  _stopOrder;
        private Order  _tpOrder;
        private string _oco;
        private bool   _entryFillFired;
        private bool   _positionCloseFired;

        public event Action<double> EntryFilled;
        public event Action<string> OrderRejected;
        public event Action<string> OrderCancelled;
        public event Action         PositionClosed;

        public NinjaOrderGateway(Indicator indicator, Account account)
        {
            _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
            _account   = account   ?? throw new ArgumentNullException(nameof(account));
            _account.OrderUpdate += OnOrderUpdate;
        }

        // ── Commands ────────────────────────────────────────────────────────

        public void SubmitMarketEntry(TradeDirection direction, int quantity)
        {
            if (quantity < 1)
                throw new ArgumentException("Quantity must be at least 1.", nameof(quantity));
            if (_indicator?.Instrument == null)
                throw new InvalidOperationException("Instrument is not available.");

            _oco                 = "TA_" + Guid.NewGuid().ToString("N");
            _entryFillFired      = false;
            _positionCloseFired  = false;
            _stopOrder           = null;
            _tpOrder             = null;

            OrderAction action = direction == TradeDirection.Long
                ? OrderAction.Buy
                : OrderAction.SellShort;

            _entryOrder = _account.CreateOrder(
                _indicator.Instrument,
                action,
                OrderType.Market,
                OrderEntry.Manual,
                TimeInForce.Day,
                quantity,
                0, 0,
                string.Empty,   // no OCO on the entry itself
                "Entry",
                DateTime.MaxValue,
                null);

            if (_entryOrder == null)
                throw new InvalidOperationException(
                    "Failed to create entry order. Check account connection and instrument.");

            _account.Submit(new[] { _entryOrder });
        }

        public void SubmitBracket(double stopPrice, double tpPrice, int quantity)
        {
            if (_entryOrder == null)
                throw new InvalidOperationException("No entry order — submit the entry first.");
            if (quantity < 1)
                throw new ArgumentException("Quantity must be at least 1.", nameof(quantity));

            bool isLong = _entryOrder.OrderAction == OrderAction.Buy;
            OrderAction exitAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;

            _stopOrder = _account.CreateOrder(
                _indicator.Instrument,
                exitAction,
                OrderType.StopMarket,
                OrderEntry.Manual,
                TimeInForce.Gtc,
                quantity,
                0, stopPrice,
                _oco,
                "Stop",
                DateTime.MaxValue,
                null);

            _tpOrder = _account.CreateOrder(
                _indicator.Instrument,
                exitAction,
                OrderType.Limit,
                OrderEntry.Manual,
                TimeInForce.Gtc,
                quantity,
                tpPrice, 0,
                _oco,
                "Target",
                DateTime.MaxValue,
                null);

            if (_stopOrder == null || _tpOrder == null)
                throw new InvalidOperationException("Failed to create bracket orders.");

            _account.Submit(new[] { _stopOrder });
            _account.Submit(new[] { _tpOrder });
        }

        public void MoveStopTo(double newStopPrice)
        {
            if (_stopOrder == null || !IsWorking(_stopOrder))
                throw new InvalidOperationException("No working stop order to move.");

            _stopOrder.StopPrice = newStopPrice;
            _account.Change(new[] { _stopOrder });
        }

        public void MoveTargetTo(double newTpPrice)
        {
            if (_tpOrder == null || !IsWorking(_tpOrder))
                throw new InvalidOperationException("No working target order to move.");

            _tpOrder.LimitPrice = newTpPrice;
            _account.Change(new[] { _tpOrder });
        }

        public void CancelAll()
        {
            TryCancel(_entryOrder);
            TryCancel(_stopOrder);
            TryCancel(_tpOrder);
        }

        // ── Order event translation ─────────────────────────────────────────

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            Order order = e.Order;
            if (order == null) return;

            // Entry fill → raise once with the actual average fill price
            if (!_entryFillFired &&
                _entryOrder != null &&
                order.OrderId == _entryOrder.OrderId &&
                order.OrderState == OrderState.Filled)
            {
                _entryFillFired = true;
                EntryFilled?.Invoke(order.AverageFillPrice);
                return;
            }

            // Rejections always surface
            if (order.OrderState == OrderState.Rejected && IsOurOrder(order))
            {
                OrderRejected?.Invoke($"{order.Name} rejected");
                return;
            }

            // Entry cancelled before fill → trade-level cancel
            if (_entryOrder != null &&
                order.OrderId == _entryOrder.OrderId &&
                order.OrderState == OrderState.Cancelled)
            {
                OrderCancelled?.Invoke("Entry cancelled");
                return;
            }

            // Position close: SL or TP filled (the OCO sibling is cancelled by NT8)
            if (!_positionCloseFired &&
                order.OrderState == OrderState.Filled &&
                ((_stopOrder != null && order.OrderId == _stopOrder.OrderId) ||
                 (_tpOrder   != null && order.OrderId == _tpOrder.OrderId)))
            {
                _positionCloseFired = true;
                PositionClosed?.Invoke();
            }
        }

        private bool IsOurOrder(Order order)
        {
            return (_entryOrder != null && order.OrderId == _entryOrder.OrderId) ||
                   (_stopOrder  != null && order.OrderId == _stopOrder.OrderId)  ||
                   (_tpOrder    != null && order.OrderId == _tpOrder.OrderId);
        }

        private static bool IsWorking(Order order)
        {
            return order.OrderState == OrderState.Working ||
                   order.OrderState == OrderState.Accepted ||
                   order.OrderState == OrderState.PartFilled;
        }

        private void TryCancel(Order order)
        {
            if (order != null && IsWorking(order))
                _account.Cancel(new[] { order });
        }

        public void Dispose()
        {
            _account.OrderUpdate -= OnOrderUpdate;
        }
    }
}
