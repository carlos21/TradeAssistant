using System;
using TradeAssistant.Domain;
using Xunit;

namespace TradeAssistant.Tests.Domain
{
    public class StopSnapperTests
    {
        [Theory]
        [InlineData(20.0, 20.0)]
        [InlineData(22.4, 20.0)]   // rounds down
        [InlineData(22.5, 25.0)]   // midpoint rounds away from zero
        [InlineData(22.6, 25.0)]
        [InlineData(2.0, 5.0)]     // below one step → one step
        [InlineData(0.0, 5.0)]     // zero → one step
        public void SnapDistance_snaps_to_nearest_multiple_with_min_one_step(double input, double expected)
        {
            Assert.Equal(expected, StopSnapper.SnapDistance(input));
        }

        [Fact]
        public void SnapDistance_negative_yields_one_step()
        {
            Assert.Equal(5.0, StopSnapper.SnapDistance(-10));
        }

        [Fact]
        public void SnapDistance_NaN_yields_one_step()
        {
            Assert.Equal(5.0, StopSnapper.SnapDistance(double.NaN));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void SnapDistance_throws_when_step_not_positive(double step)
        {
            Assert.Throws<ArgumentException>(() => StopSnapper.SnapDistance(10, step));
        }

        [Fact]
        public void SnapDistance_honours_custom_step()
        {
            Assert.Equal(10.0, StopSnapper.SnapDistance(9.0, 10.0));
            Assert.Equal(10.0, StopSnapper.SnapDistance(4.0, 10.0)); // min one step
        }

        [Fact]
        public void SnapToTick_rounds_to_tick()
        {
            Assert.Equal(20000.25, StopSnapper.SnapToTick(20000.30, 0.25));
            Assert.Equal(20000.00, StopSnapper.SnapToTick(20000.10, 0.25));
            Assert.Equal(20000.50, StopSnapper.SnapToTick(20000.375, 0.25)); // midpoint away from zero
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-0.25)]
        public void SnapToTick_passes_through_when_tick_not_positive(double tick)
        {
            Assert.Equal(20000.13, StopSnapper.SnapToTick(20000.13, tick));
        }

        [Fact]
        public void SnapToTick_passes_through_NaN()
        {
            Assert.True(double.IsNaN(StopSnapper.SnapToTick(double.NaN, 0.25)));
        }

        [Fact]
        public void DistanceBetween_is_absolute_and_snapped()
        {
            Assert.Equal(20.0, StopSnapper.DistanceBetween(20000, 19980));
            Assert.Equal(20.0, StopSnapper.DistanceBetween(19980, 20000));
            Assert.Equal(25.0, StopSnapper.DistanceBetween(20000, 19977)); // 23 → 25
        }

        [Fact]
        public void StopPriceFor_long_is_below_entry()
        {
            Assert.Equal(19980.0, StopSnapper.StopPriceFor(20000, 20, TradeDirection.Long, 0.25));
        }

        [Fact]
        public void StopPriceFor_short_is_above_entry()
        {
            Assert.Equal(20020.0, StopSnapper.StopPriceFor(20000, 20, TradeDirection.Short, 0.25));
        }

        [Fact]
        public void StopPriceFor_snaps_distance_and_aligns_to_tick()
        {
            // distance 18 → 20, entry not tick aligned → result tick aligned
            Assert.Equal(19980.25, StopSnapper.StopPriceFor(20000.30, 18, TradeDirection.Long, 0.25));
        }
    }
}
