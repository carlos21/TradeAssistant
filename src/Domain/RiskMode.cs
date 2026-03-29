namespace TradeAssistant.Domain
{
    /// <summary>
    /// Determines how the risk amount is calculated.
    /// </summary>
    public enum RiskMode
    {
        /// <summary>Risk is calculated as a percentage of account balance.</summary>
        Percentage,

        /// <summary>Risk is a fixed dollar amount.</summary>
        FixedAmount
    }
}
