using System;
using NinjaTrader.Cbi;

namespace TradeAssistant.Infrastructure
{
    /// <summary>
    /// Tracks active SL and TP orders for the current position.
    /// Decoupled from NinjaTrader rendering for testability.
    /// </summary>
    public sealed class OrderLevelTracker : IDisposable
    {
        private readonly Account _account;
        private readonly Instrument _instrument;

        public Order StopOrder { get; private set; }
        public Order TpOrder { get; private set; }
        public double AverageEntryPrice { get; private set; }
        public MarketPosition PositionDirection { get; private set; }

        public double? StopPrice => StopOrder?.StopPrice;
        public double? TpPrice => TpOrder?.LimitPrice;

        public event Action OrdersUpdated;
        public event Action LevelsCleared;

        public OrderLevelTracker(Account account, Instrument instrument)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
            _account.OrderUpdate += OnOrderUpdate;
        }

        /// <summary>
        /// Scans Account.Orders to find working SL/TP orders for this instrument.
        /// Shows levels for ANY working orders, regardless of how they were entered.
        /// </summary>
        public void DiscoverWorkingOrders()
        {
            StopOrder = null;
            TpOrder = null;

            foreach (Order order in _account.Orders)
            {
                if (order.Instrument != _instrument) continue;
                if (order.OrderState != OrderState.Working && order.OrderState != OrderState.Accepted) continue;

                // ANY StopMarket order = SL
                if (order.OrderType == OrderType.StopMarket)
                {
                    StopOrder = order;
                    InferPositionDirection(order);
                }
                // ANY Limit order = TP
                else if (order.OrderType == OrderType.Limit)
                {
                    TpOrder = order;
                    InferPositionDirection(order);
                }
            }

            if (StopOrder != null || TpOrder != null)
                OrdersUpdated?.Invoke();
        }

        /// <summary>
        /// Clears tracked orders (e.g., when position closes).
        /// </summary>
        public void Clear()
        {
            StopOrder = null;
            TpOrder = null;
            PositionDirection = MarketPosition.Flat;
            AverageEntryPrice = 0;
            LevelsCleared?.Invoke();
        }

        /// <summary>
        /// Updates the stop order price. Returns true if successful.
        /// </summary>
        public bool ModifyStopPrice(double newPrice)
        {
            if (StopOrder == null) return false;
            if (StopOrder.OrderState != OrderState.Working && StopOrder.OrderState != OrderState.Accepted) return false;

            StopOrder.StopPriceChanged = newPrice;
            _account.Change(new[] { StopOrder });
            return true;
        }

        /// <summary>
        /// Updates the TP order price. Returns true if successful.
        /// </summary>
        public bool ModifyTpPrice(double newPrice)
        {
            if (TpOrder == null) return false;
            if (TpOrder.OrderState != OrderState.Working && TpOrder.OrderState != OrderState.Accepted) return false;

            TpOrder.LimitPriceChanged = newPrice;
            _account.Change(new[] { TpOrder });
            return true;
        }

        /// <summary>
        /// Sets the position information from the Position object.
        /// </summary>
        public void UpdatePosition(Position position)
        {
            if (position == null || position.MarketPosition == MarketPosition.Flat)
            {
                if (PositionDirection != MarketPosition.Flat)
                    Clear();
                return;
            }

            PositionDirection = position.MarketPosition;
            AverageEntryPrice = position.AveragePrice;
        }

        public bool HasWorkingOrders => StopOrder != null || TpOrder != null;

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            Order order = e.Order;
            if (order.Instrument != _instrument) return;

            bool wasUpdated = false;

            // Track ANY StopMarket order as SL (not just TA-named ones)
            if (order.OrderType == OrderType.StopMarket)
            {
                if (IsTerminalState(order.OrderState))
                {
                    if (StopOrder == order)
                    {
                        StopOrder = null;
                        wasUpdated = true;
                    }
                }
                else if (order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted)
                {
                    StopOrder = order;
                    InferPositionDirection(order);
                    wasUpdated = true;
                }
            }
            // Track ANY Limit order as TP (not just TA-named ones)
            else if (order.OrderType == OrderType.Limit)
            {
                if (IsTerminalState(order.OrderState))
                {
                    if (TpOrder == order)
                    {
                        TpOrder = null;
                        wasUpdated = true;
                    }
                }
                else if (order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted)
                {
                    TpOrder = order;
                    InferPositionDirection(order);
                    wasUpdated = true;
                }
            }

            if (wasUpdated)
            {
                if (StopOrder == null && TpOrder == null)
                    LevelsCleared?.Invoke();
                else
                    OrdersUpdated?.Invoke();
            }
        }

        private void InferPositionDirection(Order order)
        {
            // Infer direction from order action
            if (order.OrderAction == OrderAction.Sell || order.OrderAction == OrderAction.SellShort)
                PositionDirection = MarketPosition.Long;  // Closing a long position
            else
                PositionDirection = MarketPosition.Short; // Closing a short position
        }

        private static bool IsTerminalState(OrderState state)
        {
            return state == OrderState.Filled ||
                   state == OrderState.Cancelled ||
                   state == OrderState.Rejected;
        }

        public void Dispose()
        {
            _account.OrderUpdate -= OnOrderUpdate;
        }
    }
}
