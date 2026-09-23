using TradeAssistant.Domain;
using Xunit;

namespace TradeAssistant.Tests.Domain
{
    public class FuturesPositionSizerTests
    {
        // NQ: tick 0.25, tick value $5.
        [Fact]
        public void Computes_exact_tick_math_and_contracts()
        {
            var r = FuturesPositionSizer.Calculate(20000, 19980, 0.25, 5.0, 800);

            Assert.Null(r.error);
            Assert.Equal(80.0, r.stopDistanceTicks);   // 20 pts / 0.25
            Assert.Equal(400.0, r.riskPerContract);    // 80 ticks × $5
            Assert.Equal(2, r.contracts);              // floor(800 / 400)
        }

        [Fact]
        public void Floors_contracts_when_risk_does_not_divide_evenly()
        {
            var r = FuturesPositionSizer.Calculate(20000, 19980, 0.25, 5.0, 999.99);
            Assert.Equal(2, r.contracts);
        }

        [Fact]
        public void Zero_contracts_when_risk_too_small()
        {
            var r = FuturesPositionSizer.Calculate(20000, 19980, 0.25, 5.0, 399.99);
            Assert.Equal(0, r.contracts);
            Assert.Null(r.error);
        }

        // ── Min-risk floor (minRiskAmount) ────────────────────────────────────
        // NQ numbers below: tick 0.25, tick value $5.

        [Fact]
        public void Adds_one_contract_when_floored_risk_is_below_minimum()
        {
            // riskPerContract = $70 (14 ticks): floor(120/70) = 1 → $70 < $100 → bump to 2.
            var r = FuturesPositionSizer.Calculate(20000, 19996.5, 0.25, 5.0, 120, 100);
            Assert.Null(r.error);
            Assert.Equal(70.0, r.riskPerContract);
            Assert.Equal(2, r.contracts);
        }

        [Fact]
        public void No_bump_when_floored_risk_meets_minimum_exactly()
        {
            // riskPerContract = $50 (10 ticks): floor(120/50) = 2 → $100 exactly.
            var r = FuturesPositionSizer.Calculate(20000, 19997.5, 0.25, 5.0, 120, 100);
            Assert.Equal(2, r.contracts);
        }

        [Fact]
        public void No_bump_when_floored_risk_exceeds_minimum()
        {
            // riskPerContract = $60 (12 ticks): floor(120/60) = 2 → $120.
            var r = FuturesPositionSizer.Calculate(20000, 19997.0, 0.25, 5.0, 120, 100);
            Assert.Equal(2, r.contracts);
        }

        [Fact]
        public void Zero_contract_result_is_rescued_when_one_contract_meets_minimum()
        {
            // riskPerContract = $150 (30 ticks): floor(120/150) = 0, but $150 >= $100 → 1.
            var r = FuturesPositionSizer.Calculate(20000, 19992.5, 0.25, 5.0, 120, 100);
            Assert.Null(r.error);
            Assert.Equal(150.0, r.riskPerContract);
            Assert.Equal(1, r.contracts);
        }

        [Fact]
        public void Zero_contract_result_without_minimum_stays_zero()
        {
            var r = FuturesPositionSizer.Calculate(20000, 19992.5, 0.25, 5.0, 120, 0);
            Assert.Equal(0, r.contracts);
        }

        [Fact]
        public void Works_for_short_direction_distances()
        {
            var r = FuturesPositionSizer.Calculate(20000, 20010, 0.25, 5.0, 200);
            Assert.Equal(40.0, r.stopDistanceTicks);
            Assert.Equal(200.0, r.riskPerContract);
            Assert.Equal(1, r.contracts);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-0.25)]
        public void Error_when_tick_size_not_positive(double tickSize)
        {
            var r = FuturesPositionSizer.Calculate(20000, 19980, tickSize, 5.0, 800);
            Assert.Equal(0, r.contracts);
            Assert.Equal("Tick size must be positive.", r.error);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        public void Error_when_tick_value_not_positive(double tickValue)
        {
            var r = FuturesPositionSizer.Calculate(20000, 19980, 0.25, tickValue, 800);
            Assert.Equal(0, r.contracts);
            Assert.Equal("Tick value must be positive.", r.error);
        }

        [Fact]
        public void Error_when_stop_closer_than_half_tick()
        {
            var r = FuturesPositionSizer.Calculate(20000, 19999.90, 0.25, 5.0, 800);
            Assert.Equal(0, r.contracts);
            Assert.Equal("Stop price is too close to entry (< 1 tick).", r.error);
        }

        [Fact]
        public void Half_tick_distance_is_accepted()
        {
            var r = FuturesPositionSizer.Calculate(20000, 19999.875, 0.25, 5.0, 800);
            Assert.Null(r.error);
            Assert.Equal(0.5, r.stopDistanceTicks);
        }
    }
}
