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
                new TradeConfiguration(RiskMode.FixedAmount, 800, 4.0, 3.0));
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
        public void SlStepPoints_defaults_to_five_and_flows_to_controller()
        {
            Assert.Equal(5.0, _vm.SlStepPoints);
            Assert.Contains("steps of 5 pts", _vm.AdjustHint);

            _vm.SlStepPoints = 2;

            Assert.Equal(2.0, _vm.SlStepPoints);
            Assert.Equal(2.0, _controller.Config.SlStepPoints);
            Assert.Contains("steps of 2 pts", _vm.AdjustHint);
        }

        [Fact]
        public void Invalid_sl_step_pops_error_and_reverts()
        {
            var errors = new List<(string title, string message)>();
            _vm.ErrorDisplay = (t, m) => errors.Add((t, m));

            _vm.SlStepPoints = 0;

            Assert.Single(errors);
            Assert.Equal("Invalid SL Step", errors[0].title);
            Assert.Equal(5.0, _vm.SlStepPoints);              // reverted to config value
            Assert.Equal(5.0, _controller.Config.SlStepPoints); // controller untouched
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
        public void RiskValue_below_minimum_is_clamped_and_pops_up()
        {
            var errors = new List<(string title, string message)>();
            _vm.ErrorDisplay = (t, m) => errors.Add((t, m));

            _vm.RiskValue = 50;

            Assert.Equal(100.0, _vm.RiskValue);
            Assert.Equal(100.0, _controller.Config.RiskValue);
            Assert.Single(errors);
        }

        [Fact]
        public void RiskValue_at_or_above_minimum_is_untouched()
        {
            var errors = new List<(string title, string message)>();
            _vm.ErrorDisplay = (t, m) => errors.Add((t, m));

            _vm.RiskValue = 150;

            Assert.Equal(150.0, _vm.RiskValue);
            Assert.Empty(errors);
        }

        [Fact]
        public void Switching_risk_mode_applies_mode_defaults()
        {
            _vm.RiskMode = RiskMode.Percentage;
            Assert.Equal(1.0, _vm.RiskValue);
            Assert.Equal(1.0, _controller.Config.RiskValue);
            Assert.Equal(RiskMode.Percentage, _controller.Config.RiskMode);

            _vm.RiskValue = 2.5; // custom value is replaced by the default on switch
            _vm.RiskMode = RiskMode.FixedAmount;
            Assert.Equal(120.0, _vm.RiskValue);
            Assert.Equal(120.0, _controller.Config.RiskValue);
            Assert.Equal(RiskMode.FixedAmount, _controller.Config.RiskMode);
        }

        [Fact]
        public void Reapplying_same_risk_mode_keeps_custom_value()
        {
            _vm.RiskValue = 250.0;

            _vm.RiskMode = RiskMode.FixedAmount; // unchanged — no reset

            Assert.Equal(250.0, _vm.RiskValue);
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
        public void ExecuteCommand_catches_handler_exceptions_into_status_and_popup()
        {
            var errors = new List<(string Title, string Message)>();
            _vm.ErrorDisplay = (t, m) => errors.Add((t, m));
            _vm.ExecuteRequested = () => throw new InvalidOperationException("kaput");
            SetupValidPlan();

            _vm.ExecuteCommand.Execute(null);
            Assert.Equal("Execute error: kaput", _vm.StatusMessage);
            Assert.Equal(("Execute Error", "kaput"), Assert.Single(errors));
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
            _vm.ManualBreakEvenRequested = () => { calls++; return BreakEvenResult.Moved; };

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
        public void BreakEvenCommand_catches_handler_exceptions_into_status_and_popup()
        {
            var errors = new List<(string Title, string Message)>();
            _vm.ErrorDisplay = (t, m) => errors.Add((t, m));
            _vm.ManualBreakEvenRequested = () => throw new InvalidOperationException("nope");
            SetupValidPlan();
            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            _sm.TransitionTo(TradeState.Active);

            _vm.BreakEvenCommand.Execute(null);
            Assert.Equal("Break-even error: nope", _vm.StatusMessage);
            Assert.Equal(("Break-even Error", "nope"), Assert.Single(errors));
        }

        [Theory]
        [InlineData(BreakEvenResult.AlreadyAtBreakEven, "Break-even already applied")]
        [InlineData(BreakEvenResult.NoActiveTrade, "No active trade to move")]
        [InlineData(BreakEvenResult.NoWorkingStop, "Stop order not yet working — retry in a moment")]
        public void BreakEvenCommand_surfaces_rejection_reasons_in_status_and_popup(
            BreakEvenResult result, string expectedMessage)
        {
            var errors = new List<(string Title, string Message)>();
            _vm.ErrorDisplay = (t, m) => errors.Add((t, m));
            _vm.ManualBreakEvenRequested = () => result;
            SetupValidPlan();
            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            _sm.TransitionTo(TradeState.Active);

            _vm.BreakEvenCommand.Execute(null);
            Assert.Equal(expectedMessage, _vm.StatusMessage);
            Assert.Equal(expectedMessage, TradePlannerViewModel.DescribeBreakEvenResult(result));
            Assert.Equal(("Break-even Unavailable", expectedMessage), Assert.Single(errors));
        }

        [Fact]
        public void BreakEvenCommand_Moved_leaves_status_to_the_monitor_and_state_flow()
        {
            var errors = new List<(string Title, string Message)>();
            _vm.ErrorDisplay = (t, m) => errors.Add((t, m));
            _vm.ManualBreakEvenRequested = () => BreakEvenResult.Moved;
            SetupValidPlan();
            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            _sm.TransitionTo(TradeState.Active);
            string statusBefore = _vm.StatusMessage;

            _vm.BreakEvenCommand.Execute(null);
            Assert.Equal(statusBefore, _vm.StatusMessage); // no rejection message injected
            Assert.Empty(errors);                          // no popup on success
        }

        // ── Update RR command ───────────────────────────────────────────────

        [Fact]
        public void UpdateRrCommand_gated_on_CanUpdateRr_and_forwards_current_rr()
        {
            double? received = null;
            _vm.UpdateRrRequested = rr => { received = rr; return RrUpdateResult.Updated; };

            Assert.False(_vm.UpdateRrCommand.CanExecute(null)); // Idle

            SetupValidPlan();
            Assert.False(_vm.UpdateRrCommand.CanExecute(null)); // Planning

            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            Assert.False(_vm.UpdateRrCommand.CanExecute(null));

            _sm.TransitionTo(TradeState.Active);
            Assert.True(_vm.UpdateRrCommand.CanExecute(null));

            _vm.RrRatio = 5.5;
            _vm.UpdateRrCommand.Execute(null);
            Assert.Equal(5.5, received);

            _sm.TransitionTo(TradeState.BreakEvenTriggered);
            Assert.True(_vm.UpdateRrCommand.CanExecute(null));

            _sm.TransitionTo(TradeState.Closed);
            Assert.False(_vm.UpdateRrCommand.CanExecute(null));
        }

        [Theory]
        [InlineData(RrUpdateResult.InvalidRatio,    "RR ratio must be positive")]
        [InlineData(RrUpdateResult.NoActiveTrade,   "No active trade to update")]
        [InlineData(RrUpdateResult.NoWorkingTarget, "Target order not working — retry in a moment")]
        public void UpdateRrCommand_surfaces_rejection_reasons_in_status_and_popup(
            RrUpdateResult result, string expectedMessage)
        {
            var errors = new List<(string Title, string Message)>();
            _vm.ErrorDisplay = (t, m) => errors.Add((t, m));
            _vm.UpdateRrRequested = _ => result;
            SetupValidPlan();
            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            _sm.TransitionTo(TradeState.Active);

            _vm.UpdateRrCommand.Execute(null);
            Assert.Equal(expectedMessage, _vm.StatusMessage);
            Assert.Equal(expectedMessage, TradePlannerViewModel.DescribeRrUpdateResult(result));
            Assert.Equal(("RR Update Unavailable", expectedMessage), Assert.Single(errors));
        }

        [Fact]
        public void UpdateRrCommand_Updated_leaves_status_to_the_coordinator()
        {
            var errors = new List<(string Title, string Message)>();
            _vm.ErrorDisplay = (t, m) => errors.Add((t, m));
            _vm.UpdateRrRequested = _ => RrUpdateResult.Updated;
            SetupValidPlan();
            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            _sm.TransitionTo(TradeState.Active);
            string statusBefore = _vm.StatusMessage;

            _vm.UpdateRrCommand.Execute(null);
            Assert.Equal(statusBefore, _vm.StatusMessage); // no rejection message injected
            Assert.Empty(errors);                          // no popup on success
        }

        [Fact]
        public void UpdateRrCommand_catches_handler_exceptions_into_status_and_popup()
        {
            var errors = new List<(string Title, string Message)>();
            _vm.ErrorDisplay = (t, m) => errors.Add((t, m));
            _vm.UpdateRrRequested = _ => throw new InvalidOperationException("nope");
            SetupValidPlan();
            _sm.TransitionTo(TradeState.Armed);
            _sm.TransitionTo(TradeState.Submitted);
            _sm.TransitionTo(TradeState.Active);

            _vm.UpdateRrCommand.Execute(null);
            Assert.Equal("RR update error: nope", _vm.StatusMessage);
            Assert.Equal(("RR Update Error", "nope"), Assert.Single(errors));
        }

        // ── Auto BE checkbox ────────────────────────────────────────────────

        [Fact]
        public void AutoBreakEven_defaults_true_matching_default_be_rr()
        {
            Assert.True(_vm.AutoBreakEven);
            Assert.Equal(3.0, _vm.BreakEvenRr);
            Assert.Equal(3.0, _controller.Config.BreakEvenRr);
        }

        [Fact]
        public void Unchecking_AutoBreakEven_zeroes_be_rr_in_controller()
        {
            _vm.AutoBreakEven = false;

            Assert.False(_vm.AutoBreakEven);
            Assert.Equal(0, _vm.BreakEvenRr);
            Assert.Equal(0, _controller.Config.BreakEvenRr);
        }

        [Fact]
        public void Rechecking_AutoBreakEven_restores_default_rr_only_when_zero()
        {
            _vm.AutoBreakEven = false;
            _vm.AutoBreakEven = true;
            Assert.Equal(3.0, _vm.BreakEvenRr);

            _vm.BreakEvenRr = 2.5;
            _vm.AutoBreakEven = false;
            _vm.AutoBreakEven = true;
            Assert.Equal(3.0, _vm.BreakEvenRr); // restored from 0, custom value was already cleared

            _vm.BreakEvenRr = 2.5;
            _vm.AutoBreakEven = true; // already checked with a custom RR — untouched
            Assert.Equal(2.5, _vm.BreakEvenRr);
        }

        [Fact]
        public void Typing_zero_into_be_rr_unchecks_AutoBreakEven_and_positive_checks_it()
        {
            Assert.True(_vm.AutoBreakEven);

            _vm.BreakEvenRr = 0;
            Assert.False(_vm.AutoBreakEven);

            _vm.BreakEvenRr = 1.5;
            Assert.True(_vm.AutoBreakEven);
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
