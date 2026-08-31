using TradeAssistant.Domain;
using Xunit;

namespace TradeAssistant.Tests.Domain
{
    public class DirectionStrategyTests
    {
        [Fact]
        public void Long_direction_and_stop_validity()
        {
            var s = LongStrategy.Instance;
            Assert.Equal(TradeDirection.Long, s.Direction);
            Assert.True(s.IsStopValid(20000, 19980));
            Assert.False(s.IsStopValid(20000, 20020));
            Assert.False(s.IsStopValid(20000, 20000));
        }

        [Fact]
        public void Short_direction_and_stop_validity()
        {
            var s = ShortStrategy.Instance;
            Assert.Equal(TradeDirection.Short, s.Direction);
            Assert.True(s.IsStopValid(20000, 20020));
            Assert.False(s.IsStopValid(20000, 19980));
            Assert.False(s.IsStopValid(20000, 20000));
        }

        [Fact]
        public void Long_CalcTp_is_entry_plus_risk_times_rr()
        {
            Assert.Equal(20080.0, LongStrategy.Instance.CalcTp(20000, 19980, 4.0));
        }

        [Fact]
        public void Short_CalcTp_is_entry_minus_risk_times_rr()
        {
            Assert.Equal(19920.0, ShortStrategy.Instance.CalcTp(20000, 20020, 4.0));
        }

        [Fact]
        public void Long_CalcBreakEven()
        {
            Assert.Equal(20020.0, LongStrategy.Instance.CalcBreakEven(20000, 19980, 1.0));
        }

        [Fact]
        public void Short_CalcBreakEven()
        {
            Assert.Equal(19980.0, ShortStrategy.Instance.CalcBreakEven(20000, 20020, 1.0));
        }

        [Theory]
        [InlineData(20020.0, true)]   // at
        [InlineData(20025.0, true)]   // beyond
        [InlineData(20019.0, false)]  // before
        public void Long_break_even_detection(double price, bool expected)
        {
            Assert.Equal(expected,
                LongStrategy.Instance.IsPriceAtOrBeyondBreakEven(price, 20020));
        }

        [Theory]
        [InlineData(19980.0, true)]
        [InlineData(19975.0, true)]
        [InlineData(19981.0, false)]
        public void Short_break_even_detection(double price, bool expected)
        {
            Assert.Equal(expected,
                ShortStrategy.Instance.IsPriceAtOrBeyondBreakEven(price, 19980));
        }
    }
}
