using System;
using System.Collections.Generic;
using TradeAssistant.Application;
using TradeAssistant.Domain;
using TradeAssistant.Tests.Fakes;
using Xunit;

namespace TradeAssistant.Tests.Application
{
    public class BracketExecutionCoordinatorTests
    {
        private readonly FakeInstrumentInfoProvider _instrument = new();
        private readonly FakeOrderGateway _gateway = new();
        private readonly TradeStateMachine _sm = new();
        private readonly BracketExecutionCoordinator _sut;

        private readonly List<string> _errors = new();
        private readonly List<string> _status = new();
        private readonly List<BracketInfo> _brackets = new();

        public BracketExecutionCoordinatorTests()
        {
            _sut = new BracketExecutionCoordinator(_gateway, _sm, _instrument);
            _sut.ExecutionError += e => _errors.Add(e);
            _sut.StatusMessage += s => _status.Add(s);
            _sut.BracketPlaced += b => _brackets.Add(b);
        }

        private static TradePlan LongPlan(int contracts = 2, double rr = 4.0, double slDist = 20) =>
            TradePlan.Valid(contracts, 800, 3200, 20000, 19980, 20080,
                slDist, rr, TradeDirection.Long, slDist / 0.25);

        private void ToPlanning() => _sm.TransitionTo(TradeState.Planning);

        // ── Guards ──────────────────────────────────────────────────────────

        [Fact]
        public void Ctor_guards_against_nulls()
        {
            Assert.Throws<ArgumentNullException>(() => new BracketExecutionCoordinator(null, _sm, _instrument));
            Assert.Throws<ArgumentNullException>(() => new BracketExecutionCoordinator(_gateway, null, _instrument));
            Assert.Throws<ArgumentNullException>(() => new BracketExecutionCoordinator(_gateway, _sm, null));
        }

        [Fact]
        public void Execute_null_plan_reports_error()
        {
            _sut.Execute(null);
            Assert.Equal("No valid trade plan.", Assert.Single(_errors));
        }

        [Fact]
        public void Execute_invalid_plan_reports_its_validation_error()
        {
            _sut.Execute(TradePlan.Invalid("bad geometry"));
            Assert.Equal("bad geometry", Assert.Single(_errors));
        }

        [Fact]
        public void Execute_zero_contracts_reports_error()
        {
            _sut.Execute(LongPlan(contracts: 0));
            Assert.Equal("Cannot execute: 0 contracts calculated.", Assert.Single(_errors));
        }

        [Fact]
        public void Execute_from_wrong_state_reports_error()
        {
            Assert.Equal(TradeState.Idle, _sm.Current);
            _sut.Execute(LongPlan());
            Assert.Equal("Cannot execute from state Idle.", Assert.Single(_errors));
            Assert.Empty(_gateway.Entries);
        }

        // ── Happy path ──────────────────────────────────────────────────────

        [Fact]
        public void Happy_path_arms_submits_and_reports_status()
        {
            ToPlanning();
            _sut.Execute(LongPlan());

            Assert.Equal(TradeState.Submitted, _sm.Current);
            var entry = Assert.Single(_gateway.Entries);
            Assert.Equal(TradeDirection.Long, entry.Direction);
            Assert.Equal(2, entry.Quantity);
            Assert.Contains(_status, s => s.Contains("awaiting fill"));
            Assert.Empty(_errors);
        }

        [Fact]
        public void Entry_fill_with_slippage_anchors_bracket_to_fill_preserving_rr()
        {
            ToPlanning();
            _sut.Execute(LongPlan());
            _gateway.FireEntryFilled(20001.25);

            var b = Assert.Single(_gateway.Brackets);
            Assert.Equal(19981.25, b.StopPrice);            // fill − 20 (tick aligned)
            Assert.Equal(20081.25, b.TpPrice);              // fill + 20 × 4
            Assert.Equal(2, b.Quantity);
            Assert.Equal(4.0, (b.TpPrice - 20001.25) / (20001.25 - b.StopPrice), 10);

            Assert.Equal(TradeState.Active, _sm.Current);
            var info = Assert.Single(_brackets);
            Assert.Equal(20001.25, info.FillPrice);
            Assert.Equal(19981.25, info.StopPrice);
            Assert.Equal(20081.25, info.TpPrice);
            Assert.Equal(2, info.Contracts);
            Assert.Equal(TradeDirection.Long, info.Direction);
            Assert.Contains(_status, s => s.Contains("Bracket live"));
        }

        [Fact]
        public void Short_plan_bracket_is_above_and_below_fill()
        {
            ToPlanning();
            _sut.Execute(TradePlan.Valid(1, 400, 1600, 20000, 20020, 19920,
                20, 4.0, TradeDirection.Short, 80));
            _gateway.FireEntryFilled(19998.75);

            var b = Assert.Single(_gateway.Brackets);
            Assert.Equal(20018.75, b.StopPrice); // fill + 20
            Assert.Equal(19918.75, b.TpPrice);   // fill − 80
            Assert.Equal(TradeDirection.Short, Assert.Single(_brackets).Direction);
        }

        // ── Failure paths ───────────────────────────────────────────────────

        [Fact]
        public void Entry_submission_throw_reports_error_and_returns_to_idle_via_cancelled()
        {
            ToPlanning();
            _gateway.ThrowOnSubmitMarketEntry = true;

            var states = new List<TradeState>();
            _sm.StateChanged += (_, next) => states.Add(next);

            _sut.Execute(LongPlan());

            Assert.Equal("Entry submission failed: entry boom", Assert.Single(_errors));
            // Armed → Cancelled → Idle rollback.
            Assert.Equal(TradeState.Idle, _sm.Current);
            Assert.Equal(
                new[] { TradeState.Armed, TradeState.Cancelled, TradeState.Idle },
                states.ToArray()); // subscribed after reaching Planning

            // Recovered: a new attempt works.
            _gateway.ThrowOnSubmitMarketEntry = false;
            ToPlanning();
            _sut.Execute(LongPlan());
            Assert.Single(_gateway.Entries);
        }

        [Fact]
        public void Bracket_submission_throw_reports_flatten_warning_and_moves_to_active()
        {
            ToPlanning();
            _sut.Execute(LongPlan());
            _gateway.ThrowOnSubmitBracket = true;

            _gateway.FireEntryFilled(20000);

            var err = Assert.Single(_errors);
            Assert.Contains("Bracket submission failed: bracket boom", err);
            Assert.Contains("flatten it manually", err);
            // The entry filled, so the position is genuinely open: the machine
            // now transitions to Active even though the bracket failed.
            Assert.Equal(TradeState.Active, _sm.Current);
            Assert.Empty(_brackets);
        }

        [Fact]
        public void Duplicate_entry_fill_is_ignored()
        {
            ToPlanning();
            _sut.Execute(LongPlan());
            _gateway.FireEntryFilled(20000);
            _gateway.FireEntryFilled(20000);

            Assert.Single(_gateway.Brackets);
            Assert.Single(_brackets);
        }

        [Fact]
        public void Entry_fill_without_pending_plan_is_ignored()
        {
            _gateway.FireEntryFilled(20000); // never executed a plan
            Assert.Empty(_gateway.Brackets);
        }

        [Fact]
        public void Order_rejected_reports_error_and_resets_to_idle()
        {
            ToPlanning();
            _sut.Execute(LongPlan());

            _gateway.FireOrderRejected("margin");

            Assert.Equal("Order rejected: margin", Assert.Single(_errors));
            Assert.Equal(TradeState.Idle, _sm.Current);
        }

        [Fact]
        public void Order_cancelled_in_submitted_aborts_trade()
        {
            ToPlanning();
            _sut.Execute(LongPlan());

            _gateway.FireOrderCancelled("user cancel");

            Assert.Equal(TradeState.Idle, _sm.Current);
            Assert.Contains(_status, s => s == "Entry cancelled: user cancel");
        }

        [Fact]
        public void Order_cancelled_after_fill_is_only_a_status_message()
        {
            ToPlanning();
            _sut.Execute(LongPlan());
            _gateway.FireEntryFilled(20000);
            Assert.Equal(TradeState.Active, _sm.Current);

            _gateway.FireOrderCancelled("oco sibling");

            Assert.Equal(TradeState.Active, _sm.Current);
            Assert.Contains(_status, s => s == "Order cancelled: oco sibling");
            Assert.Empty(_errors);
        }

        [Fact]
        public void Position_closed_transitions_to_idle_with_status()
        {
            ToPlanning();
            _sut.Execute(LongPlan());
            _gateway.FireEntryFilled(20000);

            _gateway.FirePositionClosed();

            Assert.Equal(TradeState.Idle, _sm.Current);
            Assert.Contains(_status, s => s == "Position closed. Ready for next trade.");
        }

        [Fact]
        public void Position_closed_in_idle_only_reports_status()
        {
            _gateway.FirePositionClosed(); // no Closed transition possible from Idle

            Assert.Equal(TradeState.Idle, _sm.Current);
            Assert.Contains(_status, s => s == "Position closed. Ready for next trade.");
        }

        // ── Gateway replacement / disposal ──────────────────────────────────

        [Fact]
        public void ReplaceOrderGateway_throws_on_null()
        {
            Assert.Throws<ArgumentNullException>(() => _sut.ReplaceOrderGateway(null));
        }

        [Fact]
        public void ReplaceOrderGateway_resubscribes_events()
        {
            var fresh = new FakeOrderGateway();
            _sut.ReplaceOrderGateway(fresh);

            ToPlanning();
            // Execute uses the NEW gateway.
            _sut.Execute(LongPlan());
            Assert.Empty(_gateway.Entries);
            Assert.Single(fresh.Entries);

            // Old gateway events no longer reach the coordinator.
            _gateway.FireEntryFilled(20000);
            Assert.Empty(fresh.Brackets);

            // New gateway events do.
            fresh.FireEntryFilled(20000);
            Assert.Single(fresh.Brackets);
        }

        [Fact]
        public void Dispose_unsubscribes_from_gateway()
        {
            _sut.Dispose();

            ToPlanning();
            _gateway.FireEntryFilled(20000);
            _gateway.FireOrderRejected("x");
            _gateway.FireOrderCancelled("x");
            _gateway.FirePositionClosed();

            Assert.Empty(_gateway.Brackets);
            Assert.Empty(_errors);
            Assert.Equal(TradeState.Planning, _sm.Current);
        }
    }
}
