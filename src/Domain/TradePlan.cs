namespace TradeAssistant.Domain
{
    /// <summary>
    /// Computed result produced by the planner from a TradeConfiguration.
    /// Immutable. IsValid == false if the plan cannot be executed.
    /// </summary>
    public sealed class TradePlan
    {
        public int            Contracts         { get; }
        public double         RiskDollars       { get; }
        public double         ProfitDollars     { get; }
        public double         EntryPrice        { get; }
        public double         StopPrice         { get; }
        public double         TpPrice           { get; }
        public double         SlDistancePoints  { get; }
        public double         RrRatio           { get; }
        public TradeDirection Direction         { get; }
        public double         StopDistanceTicks { get; }
        public bool           IsValid           { get; }
        public string         ValidationError   { get; }

        private TradePlan(
            int            contracts,
            double         riskDollars,
            double         profitDollars,
            double         entryPrice,
            double         stopPrice,
            double         tpPrice,
            double         slDistancePoints,
            double         rrRatio,
            TradeDirection direction,
            double         stopDistanceTicks,
            bool           isValid,
            string         validationError)
        {
            Contracts         = contracts;
            RiskDollars       = riskDollars;
            ProfitDollars     = profitDollars;
            EntryPrice        = entryPrice;
            StopPrice         = stopPrice;
            TpPrice           = tpPrice;
            SlDistancePoints  = slDistancePoints;
            RrRatio           = rrRatio;
            Direction         = direction;
            StopDistanceTicks = stopDistanceTicks;
            IsValid           = isValid;
            ValidationError   = validationError;
        }

        public static TradePlan Valid(
            int            contracts,
            double         riskDollars,
            double         profitDollars,
            double         entryPrice,
            double         stopPrice,
            double         tpPrice,
            double         slDistancePoints,
            double         rrRatio,
            TradeDirection direction,
            double         stopDistanceTicks) =>
            new TradePlan(contracts, riskDollars, profitDollars,
                entryPrice, stopPrice, tpPrice, slDistancePoints, rrRatio,
                direction, stopDistanceTicks, true, null);

        public static TradePlan Invalid(string error) =>
            new TradePlan(0, 0, 0, 0, 0, 0, 0, 0, TradeDirection.Long, 0, false, error);

        public static TradePlan Empty =>
            new TradePlan(0, 0, 0, 0, 0, 0, 0, 0, TradeDirection.Long, 0, false, "No plan calculated.");
    }
}
