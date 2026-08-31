using System;
using TradeAssistant.Domain;
using Xunit;

namespace TradeAssistant.Tests.Domain
{
    public class RiskCalculatorTests
    {
        [Fact]
        public void FixedAmount_returns_risk_value_ignoring_balance()
        {
            Assert.Equal(240.0, RiskCalculator.Calculate(0, RiskMode.FixedAmount, 240.0));
            Assert.Equal(240.0, RiskCalculator.Calculate(100_000, RiskMode.FixedAmount, 240.0));
        }

        [Fact]
        public void Percentage_returns_balance_fraction()
        {
            Assert.Equal(1000.0, RiskCalculator.Calculate(100_000, RiskMode.Percentage, 1.0));
            Assert.Equal(250.0, RiskCalculator.Calculate(50_000, RiskMode.Percentage, 0.5));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        public void Throws_when_risk_value_not_positive(double riskValue)
        {
            Assert.Throws<ArgumentException>(
                () => RiskCalculator.Calculate(100_000, RiskMode.FixedAmount, riskValue));
            Assert.Throws<ArgumentException>(
                () => RiskCalculator.Calculate(100_000, RiskMode.Percentage, riskValue));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void Percentage_throws_when_balance_not_positive(double balance)
        {
            Assert.Throws<ArgumentException>(
                () => RiskCalculator.Calculate(balance, RiskMode.Percentage, 1.0));
        }
    }
}
