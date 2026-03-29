using System;

namespace TradeAssistant.Domain
{
    /// <summary>
    /// Converts account risk settings into a dollar risk amount.
    /// </summary>
    public static class RiskCalculator
    {
        /// <summary>
        /// Returns the dollar amount to risk on a single trade.
        /// </summary>
        /// <param name="accountBalance">Current account cash value.</param>
        /// <param name="mode">Whether to use percentage or fixed amount.</param>
        /// <param name="riskValue">The risk value (percent or dollars depending on mode).</param>
        public static double Calculate(double accountBalance, RiskMode mode, double riskValue)
        {
            if (riskValue <= 0)
                throw new ArgumentException("Risk value must be positive.", nameof(riskValue));

            if (mode == RiskMode.FixedAmount)
                return riskValue;

            // Percentage mode
            if (accountBalance <= 0)
                throw new ArgumentException("Account balance must be positive.", nameof(accountBalance));

            return accountBalance * (riskValue / 100.0);
        }
    }
}
