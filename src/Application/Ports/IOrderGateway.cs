using System;
using TradeAssistant.Domain;

namespace TradeAssistant.Application.Ports
{
    /// <summary>
    /// Abstracts order submission for bracket trades (no ATM strategies).
    /// Implementations wrap the broker/exchange API (Infrastructure layer).
    /// </summary>
    public interface IOrderGateway
    {
        // ── Commands ────────────────────────────────────────────────────────

        /// <summary>Submits a market entry order in the given direction.</summary>
        void SubmitMarketEntry(TradeDirection direction, int quantity);

        /// <summary>
        /// Submits the OCO-linked protective bracket (stop-market SL + limit TP)
        /// closing the open position. Exactly one of the two will fill.
        /// </summary>
        void SubmitBracket(double stopPrice, double tpPrice, int quantity);

        /// <summary>Moves the working stop order to a new price (e.g. break-even).</summary>
        void MoveStopTo(double newStopPrice);

        /// <summary>Cancels all working orders for this trade.</summary>
        void CancelAll();

        // ── Events ──────────────────────────────────────────────────────────

        /// <summary>Fires when the entry order is fully filled. Arg = average fill price.</summary>
        event Action<double> EntryFilled;

        /// <summary>Fires when an order is rejected. Arg = reason string.</summary>
        event Action<string> OrderRejected;

        /// <summary>Fires when an order is cancelled. Arg = reason string.</summary>
        event Action<string> OrderCancelled;

        /// <summary>Fires when the position is fully closed (SL or TP filled).</summary>
        event Action PositionClosed;
    }
}
