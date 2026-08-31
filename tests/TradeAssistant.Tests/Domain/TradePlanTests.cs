using TradeAssistant.Domain;
using Xunit;

namespace TradeAssistant.Tests.Domain
{
    public class TradePlanTests
    {
        [Fact]
        public void Valid_factory_sets_every_property()
        {
            var plan = TradePlan.Valid(
                contracts: 2, riskDollars: 800, profitDollars: 3200,
                entryPrice: 20000, stopPrice: 19980, tpPrice: 20080,
                slDistancePoints: 20, rrRatio: 4.0,
                direction: TradeDirection.Long, stopDistanceTicks: 80);

            Assert.True(plan.IsValid);
            Assert.Null(plan.ValidationError);
            Assert.Equal(2, plan.Contracts);
            Assert.Equal(800, plan.RiskDollars);
            Assert.Equal(3200, plan.ProfitDollars);
            Assert.Equal(20000, plan.EntryPrice);
            Assert.Equal(19980, plan.StopPrice);
            Assert.Equal(20080, plan.TpPrice);
            Assert.Equal(20, plan.SlDistancePoints);
            Assert.Equal(4.0, plan.RrRatio);
            Assert.Equal(TradeDirection.Long, plan.Direction);
            Assert.Equal(80, plan.StopDistanceTicks);
        }

        [Fact]
        public void Invalid_factory_carries_error_and_zeroes()
        {
            var plan = TradePlan.Invalid("boom");

            Assert.False(plan.IsValid);
            Assert.Equal("boom", plan.ValidationError);
            Assert.Equal(0, plan.Contracts);
            Assert.Equal(0, plan.EntryPrice);
        }

        [Fact]
        public void Empty_is_invalid_with_default_message()
        {
            var plan = TradePlan.Empty;

            Assert.False(plan.IsValid);
            Assert.Equal("No plan calculated.", plan.ValidationError);
            Assert.Equal(0, plan.Contracts);
        }
    }
}
