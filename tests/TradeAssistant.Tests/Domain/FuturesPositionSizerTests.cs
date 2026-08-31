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
