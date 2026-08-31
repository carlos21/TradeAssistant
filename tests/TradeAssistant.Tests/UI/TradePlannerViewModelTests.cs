using System;
using System.Collections.Generic;
using TradeAssistant.Application;
using TradeAssistant.Domain;
using TradeAssistant.Tests.Fakes;
using TradeAssistant.UI;
using Xunit;

namespace TradeAssistant.Tests.UI
{
    public class TradePlannerViewModelTests
    {
        private readonly FakeInstrumentInfoProvider _instrument = new();
        private readonly FakeAccountDataProvider _account = new();
        private readonly TradePlannerController _controller;
        private readonly TradeStateMachine _sm = new();
        private readonly TradePlannerViewModel _vm;

        public TradePlannerViewModelTests()
        {
            _controller = new TradePlannerController(_instrument, _account,
                new TradeConfiguration(RiskMode.FixedAmount, 800, 4.0, 1.0));
            _vm = new TradePlannerViewModel(_controller, _sm);
        }

        private void SetupValidPlan()
        {
            _sm.TransitionTo(TradeState.Planning);
            _controller.SetEntryPrice(20000);
            _controller.PlaceDefaultStop();
        }

        // ── Construction / inputs ───────────────────────────────────────────

        [Fact]
        public void Ctor_guards_against_nulls()
        {
            Assert.Throws<ArgumentNullException>(() => new TradePlannerViewModel(null, _sm));
            Assert.Throws<ArgumentNullException>(() => new TradePlannerViewModel(_controller, null));
        }

        [Fact]
        public void Input_properties_forward_to_controller()
        {
            _vm.RiskValue = 500;
            Assert.Equal(500, _vm.RiskValue);
            Assert.Equal(500, _controller.Config.RiskValue);

            _vm.RrRatio = 2.5;
            Assert.Equal(2.5, _vm.RrRatio);
            Assert.Equal(2.5, _controller.Config.RrRatio);

            _vm.BreakEvenRr = 0.75;
            Assert.Equal(0.75, _vm.BreakEvenRr);
            Assert.Equal(0.75, _controller.Config.BreakEvenRr);

            Assert.Equal(RiskMode.FixedAmount, _vm.RiskMode);
            Assert.True(_vm.ShowTradeBoxes);
        }

        [Fact]
        public void RiskValueLabel_switches_with_mode()
        {
            Assert.Equal("Risk $", _vm.RiskValueLabel);

            _vm.RiskMode = RiskMode.Percentage;
            Assert.Equal("Risk %", _vm.RiskValueLabel);
            Assert.Equal(RiskMode.Percentage, _controller.Config.RiskMode);

            _vm.RiskMode = RiskMode.FixedAmount;
            Assert.Equal("Risk $", _vm.RiskValueLabel);
        }

        [Fact]
        public void RiskMode_change_raises_label_property_changed()
        {
            var raised = new List<string>();
            _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            _vm.RiskMode = RiskMode.Percentage;

            Assert.Contains("RiskMode", raised);
            Assert.Contains("RiskValueLabel", raised);
        }

        [Fact]
        public void Direction_property_updates_label_and_controller()
        {
            _vm.Direction = TradeDirection.Short;

            Assert.Equal("SHORT", _vm.DirectionLabel);
            Assert.Equal(TradeDirection.Short, _controller.Direction);
        }

        [Fact]
        public void ShowTradeBoxes_fires_event_only_on_change()
        {
            var seen = new List<bool>();
            _vm.ShowTradeBoxesChanged += v => seen.Add(v);

            _vm.ShowTradeBoxes = true;  // already true
            _vm.ShowTradeBoxes = false;
            _vm.ShowTradeBoxes = false; // no change
            _vm.ShowTradeBoxes = true;

            Assert.Equal(new[] { false, true }, seen);
        }

        // ── Plan updates ────────────────────────────────────────────────────

        [Fact]
        public void Valid_plan_updates_display_properties_and_status()
        {
            SetupValidPlan();

            Assert.Equal(2, _vm.Contracts);
            Assert.Equal(20, _vm.SlPoints);
            Assert.Equal(800, _vm.RiskDollars);
            Assert.Equal(3200, _vm.ProfitDollars);
            Assert.Equal(19980, _vm.StopPrice);
            Assert.Equal(20080, _vm.TpPrice);
            Assert.True(_vm.CanExecuteTrade);
            Assert.Equal("2 ctr | SL 20pt | Risk $800 | TP $3200", _vm.StatusMessage);
        }

        [Fact]
        public void Invalid_plan_zeroes_display_and_shows_error()
        {
            SetupValidPlan();
            _controller.ClearStop();

            Assert.Equal(0, _vm.Contracts);
            Assert.Equal(0, _vm.RiskDollars);
            Assert.Equal(0, _vm.ProfitDollars);
            Assert.False(_vm.CanExecuteTrade);
            Assert.Equal("Stop not placed.", _vm.StatusMessage);
        }

        // ── State changes ───────────────────────────────────────────────────

        [Fact]
        public void State_changes_drive_armed_flag_break_even_and_status()
        {
            SetupValidPlan();

            _sm.TransitionTo(TradeState.Armed);
            Assert.True(_vm.IsArmed);
            Assert.False(_vm.CanBreakEven);
            Assert.Equal("ARMED — submitting entry…", _vm.StatusMessage);

            _sm.TransitionTo(TradeState.Submitted);
            Assert.False(_vm.IsArmed);
            Assert.Equal("Submitted — awaiting fill…", _vm.StatusMessage);

            _sm.TransitionTo(TradeState.Active);
            Assert.True(_vm.CanBreakEven);
            Assert.Equal("Active — bracket managing SL/TP", _vm.StatusMessage);

            _sm.TransitionTo(TradeState.BreakEvenTriggered);
            Assert.False(_vm.CanBreakEven);
            Assert.Equal("Break-even — stop at entry", _vm.StatusMessage);

            _sm.TransitionTo(TradeState.Closed);
            Assert.Equal("Trade closed.", _vm.StatusMessage);

            _sm.TransitionTo(TradeState.Idle);
            Assert.Equal("Drag SL line or use ▲▼ buttons to adjust (steps of 5 pts)", _vm.StatusMessage);
            Assert.Equal(0, _vm.Contracts);
            Assert.False(_vm.CanExecuteTrade);
        }

        [Fact]
        public void Cancelled_state_shows_cancelled_status()
        {
            SetupValidPlan();
            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            _sm.TransitionTo(TradeState.Cancelled);

            Assert.Equal("Trade cancelled.", _vm.StatusMessage);

            _sm.TransitionTo(TradeState.Idle);
            _sm.TransitionTo(TradeState.Planning); // back to Planning: flags cleared
            Assert.False(_vm.IsArmed);
            Assert.False(_vm.CanBreakEven);
        }

        // ── Commands ────────────────────────────────────────────────────────

        [Fact]
        public void ExecuteCommand_gated_on_valid_plan_and_planning_state()
        {
            _vm.ExecuteRequested = () => { };
            Assert.False(_vm.ExecuteCommand.CanExecute(null)); // Idle, no plan

            SetupValidPlan();
            Assert.True(_vm.ExecuteCommand.CanExecute(null));

            _sm.TransitionTo(TradeState.Armed);
            Assert.False(_vm.ExecuteCommand.CanExecute(null)); // not Planning
        }

        [Fact]
        public void ExecuteCommand_invokes_ExecuteRequested()
        {
            int calls = 0;
            _vm.ExecuteRequested = () => calls++;
            SetupValidPlan();

            _vm.ExecuteCommand.Execute(null);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void ExecuteCommand_catches_handler_exceptions_into_status()
        {
            _vm.ExecuteRequested = () => throw new InvalidOperationException("kaput");
            SetupValidPlan();

            _vm.ExecuteCommand.Execute(null);
            Assert.Equal("Execute error: kaput", _vm.StatusMessage);
        }

        [Fact]
        public void ToggleDirectionCommand_flips_direction()
        {
            Assert.Equal(TradeDirection.Long, _vm.Direction);
            _vm.ToggleDirectionCommand.Execute(null);
            Assert.Equal(TradeDirection.Short, _vm.Direction);
            _vm.ToggleDirectionCommand.Execute(null);
            Assert.Equal(TradeDirection.Long, _vm.Direction);
        }

        [Fact]
        public void Nudge_commands_forward_to_controller_only_in_planning()
        {
            _controller.SetEntryPrice(20000);
            Assert.False(_vm.NudgeUpCommand.CanExecute(null)); // still Idle

            _sm.TransitionTo(TradeState.Planning);
            _controller.PlaceDefaultStop();
            Assert.True(_vm.NudgeUpCommand.CanExecute(null));
            Assert.True(_vm.NudgeDownCommand.CanExecute(null));

            _vm.NudgeUpCommand.Execute(null);
            Assert.Equal(25, _controller.SlDistancePoints);

            _vm.NudgeDownCommand.Execute(null);
            Assert.Equal(20, _controller.SlDistancePoints);

            _sm.TransitionTo(TradeState.Armed);
            Assert.False(_vm.NudgeUpCommand.CanExecute(null));
            Assert.False(_vm.NudgeDownCommand.CanExecute(null));
        }

        [Fact]
        public void NudgeStop_method_forwards_arbitrary_steps()
        {
            _controller.SetEntryPrice(20000);
            _vm.NudgeStop(2);
            Assert.Equal(10, _controller.SlDistancePoints);
        }

        [Fact]
        public void BreakEvenCommand_gated_on_CanBreakEven_and_invokes_action()
        {
            int calls = 0;
            _vm.ManualBreakEvenRequested = () => calls++;

            Assert.False(_vm.BreakEvenCommand.CanExecute(null));

            SetupValidPlan();
            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            _sm.TransitionTo(TradeState.Active);

            Assert.True(_vm.BreakEvenCommand.CanExecute(null));
            _vm.BreakEvenCommand.Execute(null);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void BreakEvenCommand_catches_handler_exceptions_into_status()
        {
            _vm.ManualBreakEvenRequested = () => throw new InvalidOperationException("nope");
            SetupValidPlan();
            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            _sm.TransitionTo(TradeState.Active);

            _vm.BreakEvenCommand.Execute(null);
            Assert.Equal("Break-even error: nope", _vm.StatusMessage);
        }

        [Fact]
        public void Dispose_stops_reacting_to_events()
        {
            SetupValidPlan();
            _vm.Dispose();

            _controller.ClearStop();            // would set "Stop not placed."
            _sm.TransitionTo(TradeState.Armed); // would set armed status

            Assert.Equal("2 ctr | SL 20pt | Risk $800 | TP $3200", _vm.StatusMessage);
            Assert.False(_vm.IsArmed);
            Assert.Equal(2, _vm.Contracts);
        }
    }
}
