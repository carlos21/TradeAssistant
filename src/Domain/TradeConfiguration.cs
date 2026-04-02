namespace TradeAssistant.Domain
{
    /// <summary>
    /// Immutable value object holding all user inputs required to compute a TradePlan.
    /// </summary>
    public sealed class TradeConfiguration
    {
        public double AccountBalance   { get; }
        public double RiskPercent      { get; }   // e.g. 1.0 = 1%
        public double FixedRiskDollars { get; }   // 0 means "use percent"
        public double RrRatio          { get; }   // e.g. 4.0
        public TradeDirection Direction { get; }
        public double EntryPrice       { get; }
        public double StopPrice        { get; }
        public double TickSize         { get; }
        public double TickValue        { get; }

        public TradeConfiguration(
            double accountBalance,
            double riskPercent,
            double fixedRiskDollars,
            double rrRatio,
            TradeDirection direction,
            double entryPrice,
            double stopPrice,
            double tickSize,
            double tickValue)
        {
            AccountBalance   = accountBalance;
            RiskPercent      = riskPercent;
            FixedRiskDollars = fixedRiskDollars;
            RrRatio          = rrRatio;
            Direction        = direction;
            EntryPrice       = entryPrice;
            StopPrice        = stopPrice;
            TickSize         = tickSize;
            TickValue        = tickValue;
        }

        /// <summary>Returns a copy with a new stop price.</summary>
        public TradeConfiguration WithStop(double stopPrice) =>
            new TradeConfiguration(AccountBalance, RiskPercent, FixedRiskDollars,
                RrRatio, Direction, EntryPrice, stopPrice, TickSize, TickValue);

        /// <summary>Returns a copy with a new entry price.</summary>
        public TradeConfiguration WithEntry(double entryPrice) =>
            new TradeConfiguration(AccountBalance, RiskPercent, FixedRiskDollars,
                RrRatio, Direction, entryPrice, StopPrice, TickSize, TickValue);

        /// <summary>Returns a copy with a new direction.</summary>
        public TradeConfiguration WithDirection(TradeDirection direction) =>
            new TradeConfiguration(AccountBalance, RiskPercent, FixedRiskDollars,
                RrRatio, direction, EntryPrice, StopPrice, TickSize, TickValue);
    }
}
