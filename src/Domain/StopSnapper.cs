using System;

namespace TradeAssistant.Domain
{
    /// <summary>
    /// Central stop-loss snapping rules. SL distances are always a positive
    /// multiple of the configured step (default 5 points), and derived prices
    /// are aligned to the instrument tick size.
    /// </summary>
    public static class StopSnapper
    {
        public const double DefaultStepPoints = 5.0;

        /// <summary>
        /// Snaps a stop distance (in points) to the nearest multiple of
        /// <paramref name="step"/>, clamped to a minimum of one step.
        /// Negative or NaN input is treated as zero and yields one step.
        /// </summary>
        public static double SnapDistance(double distancePoints, double step = DefaultStepPoints)
        {
            if (step <= 0)
                throw new ArgumentException("Step must be positive.", nameof(step));

            if (double.IsNaN(distancePoints) || distancePoints < 0)
                distancePoints = 0;

            double snapped = Math.Round(distancePoints / step, MidpointRounding.AwayFromZero) * step;
            return Math.Max(step, snapped);
        }

        /// <summary>
        /// Aligns a price to the instrument tick size. Returns the input
        /// unchanged when tick size is not positive or the price is NaN.
        /// </summary>
        public static double SnapToTick(double price, double tickSize)
        {
            if (tickSize <= 0 || double.IsNaN(price)) return price;
            return Math.Round(price / tickSize, MidpointRounding.AwayFromZero) * tickSize;
        }

        /// <summary>Snapped SL distance between two prices.</summary>
        public static double DistanceBetween(double entryPrice, double stopPrice, double step = DefaultStepPoints)
        {
            return SnapDistance(Math.Abs(entryPrice - stopPrice), step);
        }

        /// <summary>
        /// Computes the stop price for an entry given a (snapped) distance and
        /// direction, aligned to the tick size.
        /// </summary>
        public static double StopPriceFor(double entryPrice, double distancePoints,
            TradeDirection direction, double tickSize, double step = DefaultStepPoints)
        {
            double snapped = SnapDistance(distancePoints, step);
            double raw = direction == TradeDirection.Long
                ? entryPrice - snapped
                : entryPrice + snapped;
            return SnapToTick(raw, tickSize);
        }
    }
}
