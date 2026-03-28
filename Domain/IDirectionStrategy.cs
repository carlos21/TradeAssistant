namespace TradeAssistant.Domain
{
    /// <summary>
    /// Encapsulates direction-specific logic so callers never branch on Long/Short themselves.
    /// </summary>
    public interface IDirectionStrategy
    {
        TradeDirection Direction { get; }

        /// <summary>Calculate TP price from entry, stop, and RR ratio.</summary>
        double CalcTp(double entry, double stop, double rrRatio);

        /// <summary>Calculate break-even price from entry, stop, and BE RR ratio.</summary>
        double CalcBreakEven(double entry, double stop, double beRr);

        /// <summary>True if stop placement is valid for this direction (e.g. stop below entry for Long).</summary>
        bool IsStopValid(double entry, double stop);

        /// <summary>True if price has reached or crossed the break-even level.</summary>
        bool IsPriceAtOrBeyondBreakEven(double currentPrice, double breakEvenPrice);
    }
}
