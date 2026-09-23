using System;
using TradeAssistant.Domain;
using Xunit;

namespace TradeAssistant.Tests.Domain
{
    public class TradeConfigurationTests
    {
        [Fact]
        public void Default_matches_documented_values()
        {
            var c = TradeConfiguration.Default;

            Assert.Equal(RiskMode.FixedAmount, c.RiskMode);
            Assert.Equal(120.0, c.RiskValue);
            Assert.Equal(4.0, c.RrRatio);
            Assert.Equal(1.0, c.BreakEvenRr);
            Assert.Equal(5.0, c.SlStepPoints);
            Assert.Equal(20.0, c.DefaultSlPoints);
        }

        [Fact]
        public void DefaultSlPoints_is_snapped_to_step()
        {
            var c = new TradeConfiguration(RiskMode.FixedAmount, 100, 2.0, 0.5,
                slStepPoints: 10, defaultSlPoints: 23);
            Assert.Equal(20.0, c.DefaultSlPoints);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void Throws_when_risk_value_not_positive(double riskValue)
        {
            Assert.Throws<ArgumentException>(
                () => new TradeConfiguration(RiskMode.FixedAmount, riskValue, 4.0, 1.0));
        }

        [Theory]
        [InlineData(99.0)]
        [InlineData(50.0)]
        public void Throws_when_fixed_amount_risk_below_minimum(double riskValue)
        {
            Assert.Throws<ArgumentException>(
                () => new TradeConfiguration(RiskMode.FixedAmount, riskValue, 4.0, 1.0));
        }

        [Fact]
        public void Fixed_amount_risk_at_minimum_is_allowed()
        {
            var c = new TradeConfiguration(RiskMode.FixedAmount, 100.0, 4.0, 1.0);
            Assert.Equal(100.0, c.RiskValue);
        }

        [Fact]
        public void Percentage_risk_has_no_dollar_floor()
        {
            var c = new TradeConfiguration(RiskMode.Percentage, 1.0, 4.0, 1.0);
            Assert.Equal(1.0, c.RiskValue);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-2.0)]
        public void Throws_when_rr_not_positive(double rr)
        {
            Assert.Throws<ArgumentException>(
                () => new TradeConfiguration(RiskMode.FixedAmount, 240, rr, 1.0));
        }

        [Fact]
        public void Throws_when_break_even_rr_negative()
        {
            Assert.Throws<ArgumentException>(
                () => new TradeConfiguration(RiskMode.FixedAmount, 240, 4.0, -0.1));
        }

        [Fact]
        public void Break_even_rr_zero_is_allowed()
        {
            var c = new TradeConfiguration(RiskMode.FixedAmount, 240, 4.0, 0.0);
            Assert.Equal(0.0, c.BreakEvenRr);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        public void Throws_when_step_not_positive(double step)
        {
            Assert.Throws<ArgumentException>(
                () => new TradeConfiguration(RiskMode.FixedAmount, 240, 4.0, 1.0, slStepPoints: step));
        }

        [Fact]
        public void Throws_when_default_sl_smaller_than_one_step()
        {
            Assert.Throws<ArgumentException>(
                () => new TradeConfiguration(RiskMode.FixedAmount, 240, 4.0, 1.0,
                    slStepPoints: 10, defaultSlPoints: 9));
        }

        [Fact]
        public void With_replaces_only_given_fields_and_keeps_sl_settings()
        {
            var original = new TradeConfiguration(RiskMode.FixedAmount, 240, 4.0, 1.0,
                slStepPoints: 5, defaultSlPoints: 20);

            var copy = original.With(riskMode: RiskMode.Percentage, riskValue: 1.5);

            Assert.Equal(RiskMode.Percentage, copy.RiskMode);
            Assert.Equal(1.5, copy.RiskValue);
            Assert.Equal(4.0, copy.RrRatio);
            Assert.Equal(1.0, copy.BreakEvenRr);
            Assert.Equal(5.0, copy.SlStepPoints);
            Assert.Equal(20.0, copy.DefaultSlPoints);
        }

        [Fact]
        public void With_rr_and_break_even_rr()
        {
            var copy = TradeConfiguration.Default.With(rrRatio: 2.5, breakEvenRr: 0.75);
            Assert.Equal(2.5, copy.RrRatio);
            Assert.Equal(0.75, copy.BreakEvenRr);
        }

        [Fact]
        public void With_no_args_returns_equivalent_copy()
        {
            var original = TradeConfiguration.Default;
            var copy = original.With();

            Assert.NotSame(original, copy);
            Assert.Equal(original.RiskMode, copy.RiskMode);
            Assert.Equal(original.RiskValue, copy.RiskValue);
            Assert.Equal(original.RrRatio, copy.RrRatio);
            Assert.Equal(original.BreakEvenRr, copy.BreakEvenRr);
        }
    }
}
