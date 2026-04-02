using System;
using TradeAssistant.Domain;

namespace TradeAssistant.Infrastructure
{
    /// <summary>
    /// Abstracts NinjaTrader order operations.
    /// Supports both unmanaged orders and ATM strategy attachment.
    /// </summary>
    public interface IOrderAdapter
    {
        // ── Commands ────────────────────────────────────────────────────────

        /// <summary>
        /// Submit an unmanaged market entry order.
        /// Legacy method - prefer StartAtmStrategy for native ChartTrader visualization.
        /// </summary>
        void SubmitEntry(TradePlan plan);

        /// <summary>
        /// Submit entry with ATM strategy attachment.
        /// Provides native ChartTrader SL/TP visualization.
        /// Entry order name MUST be "Entry".
        /// </summary>
        void StartAtmStrategy(string atmStrategyName, TradePlan plan);

        /// <summary>
        /// Attach stop market and limit TP orders after entry fill.
        /// Deprecated: Use StartAtmStrategy instead.
        /// </summary>
        [Obsolete("Use StartAtmStrategy instead")]
        void SubmitBracket(double stopPrice, double tpPrice, int contracts);

        /// <summary>
        /// Modify the stop order price (used for break-even move).
        /// Limited support when using ATM strategies.
        /// </summary>
        void ModifyStop(double newStopPrice);

        /// <summary>
        /// Cancel all open orders for this trade.
        /// </summary>
        void CancelAll();

        // ── Events ──────────────────────────────────────────────────────────

        /// <summary>Fires when the entry order is fully filled. Arg = actual fill price.</summary>
        event Action<double> EntryFilled;

        /// <summary>Fires when any order is cancelled or rejected. Arg = reason string.</summary>
        event Action<string> OrderCancelled;

        /// <summary>Fires when the position is fully closed (stop or TP hit).</summary>
        event Action PositionClosed;
    }
}
