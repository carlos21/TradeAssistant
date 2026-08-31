using System;

namespace TradeAssistant.Application.Ports
{
    /// <summary>
    /// Event-driven stream of last-trade prices. Never polled.
    /// </summary>
    public interface IPriceFeed
    {
        double LastPrice { get; }

        /// <summary>Fires on every last-price update.</summary>
        event Action<double> PriceUpdated;
    }
}
