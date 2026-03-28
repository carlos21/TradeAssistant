using System;

namespace TradeAssistant.Domain
{
    /// <summary>
    /// Converts account risk settings into a dollar risk amount.
    /// If fixedRiskDollars > 0, it takes precedence over percent-based risk.
    /// </summary>
    public static class RiskCalculator
    {
        /// <summary>
        /// Returns the dollar amount to risk on a single trade.
        /// </summary>
        /// <param name="accountBalance">Current account cash value.</param>
        /// <param name="riskPercent">Risk as a percentage (e.g. 1.0 = 1%).</param>
        /// <param name="fixedRiskDollars">Fixed dollar risk override; 0 means use percent.</param>
        public static double Calculate(double accountBalance, double riskPercent, double fixedRiskDollars)
        {
            if (fixedRiskDollars > 0)
                return fixedRiskDollars;

            if (accountBalance <= 0)
                throw new ArgumentException("Account balance must be positive.", nameof(accountBalance));

            if (riskPercent <= 0)
                throw new ArgumentException("Risk percent must be positive.", nameof(riskPercent));

            return accountBalance * (riskPercent / 100.0);
        }
    }
}
