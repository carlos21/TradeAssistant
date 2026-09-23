using System;
using System.Collections.Generic;
using TradeAssistant.Application;
using TradeAssistant.Domain;
using TradeAssistant.Tests.Fakes;
using Xunit;

namespace TradeAssistant.Tests.Application
{
    public class TradePlannerControllerTests
    {
        private readonly FakeInstrumentInfoProvider _instrument = new();
        private readonly FakeAccountDataProvider _account = new();
        private readonly List<TradePlan> _plans = new();
        private readonly TradePlannerController _sut;

        public TradePlannerControllerTests()
        {
            _sut = new TradePlannerController(_instrument, _account,
                new TradeConfiguration(RiskMode.FixedAmount, 800, 4.0, 1.0));
            _sut.PlanUpdated += p => _plans.Add(p);
        }

        private TradePlan LastPlan => _plans[^1];

        private void SetupValidPlan()
        {
            _sut.SetEntryPrice(20000);
            _sut.PlaceDefaultStop();
        }

        // ── Construction ────────────────────────────────────────────────────

        [Fact]
        public void Ctor_guards_against_null_dependencies()
        {
            Assert.Throws<ArgumentNullException>(() => new TradePlannerController(null, _account));
            Assert.Throws<ArgumentNullException>(() => new TradePlannerController(_instrument, null));
        }

        [Fact]
        public void Ctor_uses_default_config_when_none_given()
        {
            var c = new TradePlannerController(_instrument, _account);
            Assert.Equal(120.0, c.Config.RiskValue);
        }

        [Fact]
        public void Initial_state_has_no_entry_and_no_stop()
        {
            Assert.Equal(0, _sut.EntryPrice);
            Assert.False(_sut.HasStop);
            Assert.Equal(0, _sut.SlDistancePoints);
            Assert.Equal(0, _sut.StopPrice);
            Assert.Equal(TradeDirection.Long, _sut.Direction);
        }

        // ── Plan emission ───────────────────────────────────────────────────

        [Fact]
        public void Every_setter_emits_a_plan()
        {
            _sut.SetEntryPrice(20000);
            _sut.PlaceDefaultStop();
            _sut.SetDirection(TradeDirection.Short);
            _sut.SetRiskMode(RiskMode.Percentage);
            _sut.SetRiskValue(2.0);
            _sut.SetRrRatio(3.0);
            _sut.SetBreakEvenRr(0.5);
            _sut.SetStopFromPrice(20010);
            _sut.NudgeStop(1);
            _sut.ClearStop();
            _sut.Recalculate();

            Assert.Equal(11, _plans.Count);
        }

        [Fact]
        public void UpdateConfiguration_throws_on_null()
        {
            Assert.Throws<ArgumentNullException>(() => _sut.UpdateConfiguration(null));
        }

        [Fact]
        public void Plan_adds_a_contract_when_rounding_would_realize_less_than_minimum_risk()
        {
            // 0.25-pt snapping so a 3.5-pt stop is reachable: 14 ticks → $70/contract.
            // floor(120/70) = 1 → $70 realized < $100 minimum → bumped to 2 contracts.
            var sut = new TradePlannerController(_instrument, _account,
                new TradeConfiguration(RiskMode.FixedAmount, 120, 4.0, 1.0, slStepPoints: 0.25));
            var plans = new List<TradePlan>();
            sut.PlanUpdated += p => plans.Add(p);

            sut.SetEntryPrice(20000);
            sut.SetStopFromPrice(19996.5);

            var plan = plans[^1];
            Assert.True(plan.IsValid);
            Assert.Equal(2, plan.Contracts);
            Assert.True(plan.RiskDollars >= TradeConfiguration.MinRiskDollars);
        }

        [Fact]
        public void Plan_takes_one_contract_when_only_one_exceeds_target_but_meets_minimum()
        {
            // 10-pt stop = 40 ticks → $200/contract. floor(120/200) = 0, but $200 >= $100.
            var sut = new TradePlannerController(_instrument, _account,
                new TradeConfiguration(RiskMode.FixedAmount, 120, 4.0, 1.0));
            var plans = new List<TradePlan>();
            sut.PlanUpdated += p => plans.Add(p);

            sut.SetEntryPrice(20000);
            sut.SetStopFromPrice(19990);

            var plan = plans[^1];
            Assert.True(plan.IsValid);
            Assert.Equal(1, plan.Contracts);
            Assert.True(plan.RiskDollars >= TradeConfiguration.MinRiskDollars);
        }

        [Fact]
        public void ReplaceAccountDataProvider_throws_on_null_and_swaps_source()
        {
            Assert.Throws<ArgumentNullException>(() => _sut.ReplaceAccountDataProvider(null));

            SetupValidPlan();
            _sut.ReplaceAccountDataProvider(new FakeAccountDataProvider { AccountBalance = 0 });
            _sut.Recalculate();

            Assert.False(LastPlan.IsValid);
            Assert.Equal("Account balance is zero or unavailable.", LastPlan.ValidationError);
        }

        // ── Valid plan geometry ─────────────────────────────────────────────

        [Fact]
        public void Valid_long_plan_computes_all_fields()
        {
            SetupValidPlan();

            var p = LastPlan;
            Assert.True(p.IsValid);
            Assert.Equal(20000, p.EntryPrice);
            Assert.Equal(19980, p.StopPrice);
            Assert.Equal(20080, p.TpPrice);          // 20pt risk × RR 4
            Assert.Equal(20, p.SlDistancePoints);
            Assert.Equal(80, p.StopDistanceTicks);   // 20 / 0.25
            Assert.Equal(2, p.Contracts);            // 800 / (80×5)
            Assert.Equal(800, p.RiskDollars);
            Assert.Equal(3200, p.ProfitDollars);     // 2 × 320 ticks × $5
            Assert.Equal(TradeDirection.Long, p.Direction);
            Assert.Equal(4.0, p.RrRatio);
        }

        [Fact]
        public void Rr_invariant_holds_for_long_and_short()
        {
            SetupValidPlan();
            Assert.Equal(4.0, (LastPlan.TpPrice - 20000) / (20000 - LastPlan.StopPrice), 10);

            _sut.SetDirection(TradeDirection.Short);
            Assert.Equal(4.0, (20000 - LastPlan.TpPrice) / (LastPlan.StopPrice - 20000), 10);
            Assert.Equal(20020, LastPlan.StopPrice);
            Assert.Equal(19920, LastPlan.TpPrice);
        }

        [Fact]
        public void SetDirection_mirrors_stop_keeping_distance()
        {
            SetupValidPlan();
            double before = _sut.StopPrice;
            Assert.Equal(19980, before);

            _sut.SetDirection(TradeDirection.Short);
            Assert.Equal(20020, _sut.StopPrice);
            Assert.Equal(20, _sut.SlDistancePoints);
        }

        [Fact]
        public void SetStopFromPrice_derives_snapped_distance()
        {
            _sut.SetEntryPrice(20000);
            _sut.SetStopFromPrice(19977.60); // 22.4 pts → snaps to 20

            Assert.True(_sut.HasStop);
            Assert.Equal(20, _sut.SlDistancePoints);
            Assert.Equal(19980, LastPlan.StopPrice);
        }

        [Fact]
        public void SetStopFromPrice_is_noop_without_entry()
        {
            _sut.SetStopFromPrice(19980);

            Assert.False(_sut.HasStop);
            Assert.Empty(_plans);
        }

        [Fact]
        public void NudgeStop_moves_by_step_and_clamps_at_one_step()
        {
            SetupValidPlan();

            _sut.NudgeStop(1);
            Assert.Equal(25, _sut.SlDistancePoints);
            Assert.Equal(19975, LastPlan.StopPrice);

            _sut.NudgeStop(-1);
            Assert.Equal(20, _sut.SlDistancePoints);

            _sut.NudgeStop(-10); // would go negative → clamped at one step
            Assert.Equal(5, _sut.SlDistancePoints);
            Assert.Equal(19995, LastPlan.StopPrice);
        }

        [Fact]
        public void NudgeStop_arms_stop_when_none_placed()
        {
            _sut.SetEntryPrice(20000);
            Assert.False(_sut.HasStop);

            _sut.NudgeStop(1);
            Assert.True(_sut.HasStop);
            Assert.Equal(5, _sut.SlDistancePoints);
        }

        [Fact]
        public void PlaceDefaultStop_uses_configured_default_distance()
        {
            var c = new TradePlannerController(_instrument, _account,
                new TradeConfiguration(RiskMode.FixedAmount, 800, 4.0, 1.0, defaultSlPoints: 30));
            TradePlan plan = null;
            c.PlanUpdated += p => plan = p;

            c.SetEntryPrice(20000);
            c.PlaceDefaultStop();

            Assert.Equal(30, c.SlDistancePoints);
            Assert.Equal(19970, plan.StopPrice);
        }

        [Fact]
        public void UpdateConfiguration_resnaps_existing_stop_to_new_step()
        {
            _sut.SetEntryPrice(20000);
            _sut.SetStopFromPrice(19985); // 15 pts with step 5
            Assert.Equal(15, _sut.SlDistancePoints);

            _sut.UpdateConfiguration(new TradeConfiguration(
                RiskMode.FixedAmount, 800, 4.0, 1.0, slStepPoints: 10, defaultSlPoints: 20));

            Assert.Equal(20, _sut.SlDistancePoints); // 15 re-snapped to step 10
        }

        // ── Invalid plans ───────────────────────────────────────────────────

        [Fact]
        public void No_entry_yields_invalid_plan()
        {
            _sut.PlaceDefaultStop();
            Assert.False(LastPlan.IsValid);
            Assert.Equal("Entry price not set.", LastPlan.ValidationError);
        }

        [Fact]
        public void No_stop_yields_invalid_plan()
        {
            _sut.SetEntryPrice(20000);
            Assert.False(LastPlan.IsValid);
            Assert.Equal("Stop not placed.", LastPlan.ValidationError);
        }

        [Fact]
        public void ClearStop_invalidates_plan()
        {
            SetupValidPlan();
            _sut.ClearStop();

            Assert.False(_sut.HasStop);
            Assert.Equal(0, _sut.SlDistancePoints);
            Assert.Equal(0, _sut.StopPrice);
            Assert.Equal("Stop not placed.", LastPlan.ValidationError);
        }

        [Fact]
        public void Zero_balance_yields_invalid_plan()
        {
            _account.AccountBalance = 0;
            SetupValidPlan();

            Assert.False(LastPlan.IsValid);
            Assert.Equal("Account balance is zero or unavailable.", LastPlan.ValidationError);
        }

        [Fact]
        public void Sizing_error_is_surfaced()
        {
            _instrument.TickValue = 0;
            SetupValidPlan();

            Assert.False(LastPlan.IsValid);
            Assert.Equal("Tick value must be positive.", LastPlan.ValidationError);
        }

        [Fact]
        public void Zero_contracts_yields_invalid_plan_when_even_one_contract_is_below_minimum()
        {
            // Percentage mode on a small balance: 0.8% of $5,000 = $40 target.
            // 2.5-pt stop = 10 ticks → $50/contract: floor(40/50) = 0, and $50 < $100
            // minimum → no rescue, so the plan stays invalid.
            _account.AccountBalance = 5_000;
            var c = new TradePlannerController(_instrument, _account,
                new TradeConfiguration(RiskMode.Percentage, 0.8, 4.0, 1.0, slStepPoints: 0.25));
            TradePlan plan = null;
            c.PlanUpdated += p => plan = p;

            c.SetEntryPrice(20000);
            c.SetStopFromPrice(19997.5);

            Assert.False(plan.IsValid);
            Assert.Equal("0 contracts — risk amount too small for this stop distance.", plan.ValidationError);
        }

        [Fact]
        public void Percentage_mode_uses_balance()
        {
            _sut.SetRiskMode(RiskMode.Percentage);
            _sut.SetRiskValue(0.8); // 0.8% of 100k = $800
            SetupValidPlan();

            Assert.True(LastPlan.IsValid);
            Assert.Equal(2, LastPlan.Contracts);
            Assert.Equal(800, LastPlan.RiskDollars);
        }

        [Fact]
        public void Calculation_exception_becomes_invalid_plan()
        {
            _account.ThrowOnBalance = true;
            SetupValidPlan();

            Assert.False(LastPlan.IsValid);
            Assert.StartsWith("Calculation error:", LastPlan.ValidationError);
        }

        [Fact]
        public void Stop_follows_entry_at_same_distance()
        {
            SetupValidPlan();
            _sut.SetEntryPrice(20010);

            Assert.Equal(19990, LastPlan.StopPrice);
            Assert.Equal(20090, LastPlan.TpPrice);
        }
    }
}
