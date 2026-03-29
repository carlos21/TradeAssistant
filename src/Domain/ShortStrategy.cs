namespace TradeAssistant.Domain
{
    /// <summary>
    /// Direction strategy for short trades.
    /// TP is below entry; SL must be above entry.
    /// </summary>
    public sealed class ShortStrategy : IDirectionStrategy
    {
        public static readonly ShortStrategy Instance = new ShortStrategy();

        public TradeDirection Direction => TradeDirection.Short;

        public double CalcTp(double entry, double stop, double rrRatio)
        {
            double risk = stop - entry;
            return entry - risk * rrRatio;
        }

        public double CalcBreakEven(double entry, double stop, double beRr)
        {
            double risk = stop - entry;
            return entry - risk * beRr;
        }

        public bool IsStopValid(double entry, double stop) => stop > entry;

        public bool IsPriceAtOrBeyondBreakEven(double currentPrice, double breakEvenPrice) =>
            currentPrice <= breakEvenPrice;
    }
}
