namespace TradeAssistant.Domain
{
    /// <summary>
    /// Computed result produced by domain calculators from a TradeConfiguration.
    /// Immutable. IsValid == false if the plan cannot be executed.
    /// </summary>
    public sealed class TradePlan
    {
        public int    Contracts         { get; }
        public double RiskDollars       { get; }
        public double ProfitDollars     { get; }
        public double EntryPrice        { get; }
        public double StopPrice         { get; }
        public double TpPrice           { get; }
        public double BreakEvenPrice    { get; }
        public double StopDistanceTicks { get; }
        public bool   IsValid           { get; }
        public string ValidationError   { get; }

        private TradePlan(
            int    contracts,
            double riskDollars,
            double profitDollars,
            double entryPrice,
            double stopPrice,
            double tpPrice,
            double breakEvenPrice,
            double stopDistanceTicks,
            bool   isValid,
            string validationError)
        {
            Contracts         = contracts;
            RiskDollars       = riskDollars;
            ProfitDollars     = profitDollars;
            EntryPrice        = entryPrice;
            StopPrice         = stopPrice;
            TpPrice           = tpPrice;
            BreakEvenPrice    = breakEvenPrice;
            StopDistanceTicks = stopDistanceTicks;
            IsValid           = isValid;
            ValidationError   = validationError;
        }

        public static TradePlan Valid(
            int    contracts,
            double riskDollars,
            double profitDollars,
            double entryPrice,
            double stopPrice,
            double tpPrice,
            double breakEvenPrice,
            double stopDistanceTicks) =>
            new TradePlan(contracts, riskDollars, profitDollars,
                entryPrice, stopPrice, tpPrice, breakEvenPrice,
                stopDistanceTicks, true, null);

        public static TradePlan Invalid(string error) =>
            new TradePlan(0, 0, 0, 0, 0, 0, 0, 0, false, error);

        public static TradePlan Empty =>
            new TradePlan(0, 0, 0, 0, 0, 0, 0, 0, false, "No plan calculated.");
    }
}
