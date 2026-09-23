using System;

namespace TradeAssistant.Domain
{
    /// <summary>
    /// Applies the canonical futures position sizing formula.
    ///
    /// stopDistanceTicks = |entry - stop| / tickSize
    /// riskPerContract   = stopDistanceTicks × tickValue
    /// contracts         = floor(riskAmount / riskPerContract)  [int, >= 1 or 0 if insufficient]
    ///
    /// Min-risk rule (when minRiskAmount > 0): if the floored result realizes less
    /// than minRiskAmount, one contract is added; a 0-contract result is rescued to
    /// 1 contract when a single contract already realizes at least minRiskAmount.
    /// </summary>
    public static class FuturesPositionSizer
    {
        /// <summary>
        /// Calculates the number of contracts and derived values.
        /// Returns (0, error) when the plan is not executable.
        /// </summary>
        public static (int contracts, double stopDistanceTicks, double riskPerContract, string error)
            Calculate(double entry, double stop, double tickSize, double tickValue, double riskAmount,
                      double minRiskAmount = 0)
        {
            if (tickSize <= 0)
                return (0, 0, 0, "Tick size must be positive.");

            if (tickValue <= 0)
                return (0, 0, 0, "Tick value must be positive.");

            double rawDistance = Math.Abs(entry - stop);
            if (rawDistance < tickSize * 0.5)
                return (0, 0, 0, "Stop price is too close to entry (< 1 tick).");

            double stopDistanceTicks = rawDistance / tickSize;
            double riskPerContract   = stopDistanceTicks * tickValue;

            if (riskPerContract <= 0)
                return (0, 0, 0, "Risk per contract is zero — check tick size/value.");

            int contracts = (int)Math.Floor(riskAmount / riskPerContract);
            if (contracts == 0)
            {
                if (minRiskAmount > 0 && riskPerContract >= minRiskAmount)
                    contracts = 1;
            }
            else if (contracts * riskPerContract < minRiskAmount)
            {
                contracts += 1;
            }
            return (contracts, stopDistanceTicks, riskPerContract, null);
        }
    }
}
