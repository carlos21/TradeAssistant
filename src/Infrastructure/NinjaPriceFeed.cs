using System;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using TradeAssistant.Application.Ports;

namespace TradeAssistant.Infrastructure
{
    /// <summary>
    /// Publishes last-trade prices from the instrument's MarketData stream.
    /// Event-driven — never polled.
    /// </summary>
    public sealed class NinjaPriceFeed : IPriceFeed, IDisposable
    {
        private readonly MarketData _marketData;

        public double LastPrice { get; private set; }

        public event Action<double> PriceUpdated;

        public NinjaPriceFeed(Indicator indicator)
        {
            if (indicator == null) throw new ArgumentNullException(nameof(indicator));
            _marketData = indicator.Bars.Instrument.MarketData;
            _marketData.Update += OnMarketDataUpdate;
        }

        private void OnMarketDataUpdate(object sender, MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last) return;
            LastPrice = e.Price;
            PriceUpdated?.Invoke(e.Price);
        }

        public void Dispose()
        {
            _marketData.Update -= OnMarketDataUpdate;
        }
    }
}
