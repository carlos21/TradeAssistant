using System;

namespace TradeAssistant.Domain
{
    /// <summary>
    /// Immutable value object holding all user-configured inputs for a trade plan.
    /// </summary>
    public sealed class TradeConfiguration
    {
        /// <summary>Minimum risk in account currency when RiskMode is FixedAmount.</summary>
        public const double MinRiskDollars = 100.0;

        public RiskMode RiskMode        { get; }
        public double   RiskValue       { get; }
        public double   RrRatio         { get; }

        /// <summary>RR multiple at which the stop auto-moves to break-even. 0 disables auto BE.</summary>
        public double   BreakEvenRr     { get; }

        /// <summary>SL distance snapping step in points (default 5).</summary>
        public double   SlStepPoints    { get; }

        /// <summary>Initial SL distance in points when planning starts.</summary>
        public double   DefaultSlPoints { get; }

        public TradeConfiguration(
            RiskMode riskMode,
            double   riskValue,
            double   rrRatio,
            double   breakEvenRr,
            double   slStepPoints    = StopSnapper.DefaultStepPoints,
            double   defaultSlPoints = 20.0)
        {
            if (riskValue <= 0)
                throw new ArgumentException("Risk value must be positive.", nameof(riskValue));
            if (riskMode == RiskMode.FixedAmount && riskValue < MinRiskDollars)
                throw new ArgumentException(
                    $"Fixed-amount risk cannot be less than {MinRiskDollars:F0}.", nameof(riskValue));
            if (rrRatio <= 0)
                throw new ArgumentException("RR ratio must be positive.", nameof(rrRatio));
            if (breakEvenRr < 0)
                throw new ArgumentException("Break-even RR cannot be negative.", nameof(breakEvenRr));
            if (slStepPoints <= 0)
                throw new ArgumentException("SL step must be positive.", nameof(slStepPoints));
            if (defaultSlPoints < slStepPoints)
                throw new ArgumentException("Default SL must be at least one step.", nameof(defaultSlPoints));

            RiskMode        = riskMode;
            RiskValue       = riskValue;
            RrRatio         = rrRatio;
            BreakEvenRr     = breakEvenRr;
            SlStepPoints    = slStepPoints;
            DefaultSlPoints = StopSnapper.SnapDistance(defaultSlPoints, slStepPoints);
        }

        /// <summary>Returns a copy with the given fields replaced.</summary>
        public TradeConfiguration With(
            RiskMode? riskMode    = null,
            double?   riskValue   = null,
            double?   rrRatio     = null,
            double?   breakEvenRr = null)
        {
            return new TradeConfiguration(
                riskMode    ?? RiskMode,
                riskValue   ?? RiskValue,
                rrRatio     ?? RrRatio,
                breakEvenRr ?? BreakEvenRr,
                SlStepPoints,
                DefaultSlPoints);
        }

        public static TradeConfiguration Default =>
            new TradeConfiguration(RiskMode.FixedAmount, 120.0, 4.0, 1.0);
    }
}
