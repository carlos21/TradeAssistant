using TradeAssistant.Application.Ports;

namespace TradeAssistant.Tests.Fakes
{
    /// <summary>
    /// Linear mapping: y = (price - PriceAtZeroY) * PixelsPerPoint.
    /// Defaults: price 19000 sits at y=0, 2 px per point (price 20000 → y=2000).
    /// </summary>
    public sealed class FakePriceAxisConverter : IPriceAxisConverter
    {
        public double PixelsPerPoint { get; set; } = 2.0;
        public double PriceAtZeroY { get; set; } = 19_000.0;

        public double PriceFromY(double y) => y / PixelsPerPoint + PriceAtZeroY;
        public double YFromPrice(double price) => (price - PriceAtZeroY) * PixelsPerPoint;
    }
}
