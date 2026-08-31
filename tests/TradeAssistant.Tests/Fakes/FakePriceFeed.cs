using System;
using TradeAssistant.Application.Ports;

namespace TradeAssistant.Tests.Fakes
{
    public sealed class FakePriceFeed : IPriceFeed
    {
        public double LastPrice { get; private set; }

        public event Action<double> PriceUpdated;

        public void SetPrice(double price)
        {
            LastPrice = price;
            PriceUpdated?.Invoke(price);
        }
    }
}
