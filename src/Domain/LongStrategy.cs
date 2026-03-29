namespace TradeAssistant.Domain
{
    /// <summary>
    /// Direction strategy for long trades.
    /// TP is above entry; SL must be below entry.
    /// </summary>
    public sealed class LongStrategy : IDirectionStrategy
    {
        public static readonly LongStrategy Instance = new LongStrategy();

        public TradeDirection Direction => TradeDirection.Long;

        public double CalcTp(double entry, double stop, double rrRatio)
        {
            double risk = entry - stop;
            return entry + risk * rrRatio;
        }

        public double CalcBreakEven(double entry, double stop, double beRr)
        {
            double risk = entry - stop;
            return entry + risk * beRr;
        }

        public bool IsStopValid(double entry, double stop) => stop < entry;

        public bool IsPriceAtOrBeyondBreakEven(double currentPrice, double breakEvenPrice) =>
            currentPrice >= breakEvenPrice;
    }
}
