namespace TradeAssistant.Application.Ports
{
    /// <summary>
    /// Converts between chart Y pixels and prices. Implemented over the
    /// NinjaTrader ChartScale in the indicator (composition root).
    /// </summary>
    public interface IPriceAxisConverter
    {
        double PriceFromY(double y);
        double YFromPrice(double price);
    }
}
