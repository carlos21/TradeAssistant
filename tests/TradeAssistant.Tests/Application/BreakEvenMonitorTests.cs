using System;
using System.Collections.Generic;
using TradeAssistant.Application;
using TradeAssistant.Domain;
using TradeAssistant.Tests.Fakes;
using Xunit;

namespace TradeAssistant.Tests.Application
{
    public class BreakEvenMonitorTests
    {
        private readonly FakePriceFeed _feed = new();
        private readonly FakeOrderGateway _gateway = new();
        private readonly TradeStateMachine _sm = new();
        private readonly BreakEvenMonitor _sut;

        private readonly List<string> _status = new();
        private readonly List<string> _errors = new();

        public BreakEvenMonitorTests()
        {
            _sut = new BreakEvenMonitor(_feed, _gateway, _sm);
            _sut.StatusMessage += s => _status.Add(s);
            _sut.Error += e => _errors.Add(e);
        }

        private void ToActive()
        {
            _sm.TransitionTo(TradeState.Planning);
            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            _sm.TransitionTo(TradeState.Active);
        }

        [Fact]
        public void Activate_announces_armed_trigger_price()
        {
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);

            Assert.Contains(_status, s => s.Contains("Auto BE armed") && s.Contains("20020.00"));
        }

        [Fact]
        public void EvaluatePrice_drives_trigger_without_price_feed()
        {
            ToActive();
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);

            _sut.EvaluatePrice(20020.00);

            Assert.Equal(20000, Assert.Single(_gateway.StopMoves));
            Assert.True(_sut.IsTriggered);
        }

        [Fact]
        public void Failed_trigger_reports_error_stays_armed_and_retries_next_tick()
        {
            ToActive();
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);
            _gateway.ThrowOnMoveStopTo = true;

            _feed.SetPrice(20020.00); // trigger condition met, move fails
            Assert.NotEmpty(_errors);
            Assert.Empty(_gateway.StopMoves);
            Assert.True(_sut.IsArmed);
            Assert.False(_sut.IsTriggered);

            _gateway.ThrowOnMoveStopTo = false;
            _feed.SetPrice(20021.00); // retry on a later tick succeeds
            Assert.Equal(20000, Assert.Single(_gateway.StopMoves));
            Assert.True(_sut.IsTriggered);
        }

        [Fact]
        public void Ctor_guards_against_nulls()
        {
            Assert.Throws<ArgumentNullException>(() => new BreakEvenMonitor(null, _gateway, _sm));
            Assert.Throws<ArgumentNullException>(() => new BreakEvenMonitor(_feed, null, _sm));
            Assert.Throws<ArgumentNullException>(() => new BreakEvenMonitor(_feed, _gateway, null));
        }

        [Fact]
        public void Activate_arms_with_trigger_price()
        {
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);

            Assert.True(_sut.IsArmed);
            Assert.False(_sut.IsTriggered);
            Assert.Equal(20020, _sut.TriggerPrice);
        }

        [Fact]
        public void Activate_with_zero_be_rr_disarms_but_keeps_manual()
        {
            _sut.Activate(20000, 19980, 0.0, TradeDirection.Long);

            Assert.False(_sut.IsArmed);
            Assert.Equal(0, _sut.TriggerPrice);

            ToActive();
            Assert.Equal(BreakEvenResult.Moved, _sut.MoveToBreakEven()); // manual still works
            Assert.Equal(20000, Assert.Single(_gateway.StopMoves));
        }

        [Fact]
        public void Auto_trigger_fires_exactly_once_when_price_crosses_long()
        {
            ToActive();
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);

            _feed.SetPrice(20019.75); // before trigger
            Assert.Empty(_gateway.StopMoves);

            _feed.SetPrice(20020.00); // at trigger
            Assert.Equal(20000, Assert.Single(_gateway.StopMoves));
            Assert.True(_sut.IsTriggered);
            Assert.False(_sut.IsArmed);
            Assert.Equal(TradeState.BreakEvenTriggered, _sm.Current);
            Assert.Contains(_status, s => s.Contains("break-even"));

            _feed.SetPrice(20030); // beyond, already triggered
            Assert.Single(_gateway.StopMoves);
        }

        [Fact]
        public void Auto_trigger_works_for_short()
        {
            ToActive();
            _sut.Activate(20000, 20020, 1.0, TradeDirection.Short);
            Assert.Equal(19980, _sut.TriggerPrice);

            _feed.SetPrice(19981);
            Assert.Empty(_gateway.StopMoves);

            _feed.SetPrice(19979.75);
            Assert.Equal(20000, Assert.Single(_gateway.StopMoves));
        }

        [Fact]
        public void Auto_trigger_does_not_fire_before_active_state()
        {
            _sm.TransitionTo(TradeState.Planning); // not Active
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);

            _feed.SetPrice(20025);

            Assert.Empty(_gateway.StopMoves);
            Assert.False(_sut.IsTriggered);
        }

        [Fact]
        public void Manual_move_without_activation_returns_NoWorkingStop()
        {
            ToActive();
            Assert.Equal(BreakEvenResult.NoWorkingStop, _sut.MoveToBreakEven()); // never activated
            Assert.Empty(_gateway.StopMoves);
        }

        [Fact]
        public void Manual_move_when_not_active_returns_NoActiveTrade()
        {
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);
            Assert.Equal(TradeState.Idle, _sm.Current);

            Assert.Equal(BreakEvenResult.NoActiveTrade, _sut.MoveToBreakEven());
        }

        [Fact]
        public void Manual_move_when_already_triggered_returns_AlreadyAtBreakEven()
        {
            ToActive();
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);

            Assert.Equal(BreakEvenResult.Moved, _sut.MoveToBreakEven());
            Assert.Equal(BreakEvenResult.AlreadyAtBreakEven, _sut.MoveToBreakEven());
            Assert.Single(_gateway.StopMoves);
        }

        [Fact]
        public void Gateway_throw_reports_error_and_returns_NoWorkingStop()
        {
            ToActive();
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);
            _gateway.ThrowOnMoveStopTo = true;

            Assert.Equal(BreakEvenResult.NoWorkingStop, _sut.MoveToBreakEven());

            Assert.Equal("Break-even failed: move boom", Assert.Single(_errors));
            Assert.False(_sut.IsTriggered);
            Assert.Equal(TradeState.Active, _sm.Current); // state untouched
        }

        [Fact]
        public void Deactivate_stops_auto_triggering()
        {
            ToActive();
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);
            _sut.Deactivate();

            Assert.False(_sut.IsArmed);
            _feed.SetPrice(20030);
            Assert.Empty(_gateway.StopMoves);
        }

        [Fact]
        public void ReplaceOrderGateway_throws_on_null_and_swaps_target()
        {
            Assert.Throws<ArgumentNullException>(() => _sut.ReplaceOrderGateway(null));

            var fresh = new FakeOrderGateway();
            _sut.ReplaceOrderGateway(fresh);

            ToActive();
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);
            Assert.Equal(BreakEvenResult.Moved, _sut.MoveToBreakEven());

            Assert.Empty(_gateway.StopMoves);
            Assert.Single(fresh.StopMoves);
        }

        [Fact]
        public void Dispose_unsubscribes_from_price_feed()
        {
            ToActive();
            _sut.Activate(20000, 19980, 1.0, TradeDirection.Long);
            _sut.Dispose();

            _feed.SetPrice(20030);
            Assert.Empty(_gateway.StopMoves);
        }
    }
}
