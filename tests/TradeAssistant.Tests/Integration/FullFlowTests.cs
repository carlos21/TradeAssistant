using System;
using System.Linq;
using TradeAssistant.Application;
using TradeAssistant.Domain;
using TradeAssistant.Tests.Fakes;
using Xunit;

namespace TradeAssistant.Tests.Integration
{
    /// <summary>
    /// End-to-end flows wired exactly like the indicator wires them:
    /// controller → coordinator (gateway events) → break-even monitor (price feed).
    /// </summary>
    public class FullFlowTests
    {
        private readonly FakeInstrumentInfoProvider _instrument = new();
        private readonly FakeAccountDataProvider _account = new();
        private readonly FakeOrderGateway _gateway = new();
        private readonly FakePriceFeed _feed = new();
        private readonly TradeStateMachine _sm = new();

        private readonly TradePlannerController _controller;
        private readonly BracketExecutionCoordinator _coordinator;
        private readonly BreakEvenMonitor _monitor;

        public FullFlowTests()
        {
            var config = new TradeConfiguration(RiskMode.FixedAmount, 800, 4.0, 1.0);
            _controller = new TradePlannerController(_instrument, _account, config);
            _coordinator = new BracketExecutionCoordinator(_gateway, _sm, _instrument);
            _monitor = new BreakEvenMonitor(_feed, _gateway, _sm);

            // The indicator wires the bracket into the monitor via this event.
            _coordinator.BracketPlaced += b =>
                _monitor.Activate(b.FillPrice, b.StopPrice, config.BreakEvenRr, b.Direction);
        }

        [Fact]
        public void Full_long_flow_plan_execute_fill_bracket_break_even_close()
        {
            // 1. Planning: entry + default stop.
            _sm.TransitionTo(TradeState.Planning);
            _controller.SetEntryPrice(20000);
            _controller.PlaceDefaultStop();

            TradePlan plan = null;
            _controller.PlanUpdated += p => plan = p;
            _controller.Recalculate();
            Assert.True(plan.IsValid);
            Assert.Equal(20, plan.SlDistancePoints);

            // 2. Execute → entry submitted.
            _coordinator.Execute(plan);
            Assert.Equal(TradeState.Submitted, _sm.Current);
            Assert.Equal(2, Assert.Single(_gateway.Entries).Quantity);

            // 3. Fill with slippage → bracket anchored to the actual fill.
            _gateway.FireEntryFilled(20001.25);
            Assert.Equal(TradeState.Active, _sm.Current);

            var bracket = Assert.Single(_gateway.Brackets);
            Assert.Equal(19981.25, bracket.StopPrice); // fill − 20
            Assert.Equal(0, (20001.25 - bracket.StopPrice) % 5, 10); // SL distance is a 5-pt multiple
            Assert.Equal(20081.25, bracket.TpPrice);
            // RR preserved against the fill.
            Assert.Equal(4.0, (bracket.TpPrice - 20001.25) / (20001.25 - bracket.StopPrice), 10);

            // 4. Monitor armed by the BracketPlaced handler; price crosses BE.
            Assert.True(_monitor.IsArmed);
            Assert.Equal(20021.25, _monitor.TriggerPrice);
            _feed.SetPrice(20021.25);

            Assert.Equal(20001.25, Assert.Single(_gateway.StopMoves)); // stop → fill
            Assert.Equal(TradeState.BreakEvenTriggered, _sm.Current);

            // 5. Position closes → back to Idle.
            _gateway.FirePositionClosed();
            Assert.Equal(TradeState.Idle, _sm.Current);
        }

        [Fact]
        public void Full_short_flow_mirrors_the_long_one()
        {
            _sm.TransitionTo(TradeState.Planning);
            _controller.SetDirection(TradeDirection.Short);
            _controller.SetEntryPrice(20000);
            _controller.PlaceDefaultStop();

            TradePlan plan = null;
            _controller.PlanUpdated += p => plan = p;
            _controller.Recalculate();
            Assert.True(plan.IsValid);
            Assert.Equal(TradeDirection.Short, plan.Direction);

            _coordinator.Execute(plan);
            _gateway.FireEntryFilled(19998.75);

            var bracket = Assert.Single(_gateway.Brackets);
            Assert.Equal(20018.75, bracket.StopPrice); // fill + 20
            Assert.Equal(19918.75, bracket.TpPrice);   // fill − 80
            Assert.Equal(4.0, (19998.75 - bracket.TpPrice) / (bracket.StopPrice - 19998.75), 10);

            // BE trigger: fill − 1R = 19978.75.
            Assert.True(_monitor.IsArmed);
            _feed.SetPrice(19979.00);
            Assert.Empty(_gateway.StopMoves);
            _feed.SetPrice(19978.75);
            Assert.Equal(19998.75, Assert.Single(_gateway.StopMoves));
            Assert.Equal(TradeState.BreakEvenTriggered, _sm.Current);

            _gateway.FirePositionClosed();
            Assert.Equal(TradeState.Idle, _sm.Current);
        }

        [Fact]
        public void Bracket_submit_failure_still_activates_break_even_monitor_so_manual_be_works()
        {
            _sm.TransitionTo(TradeState.Planning);
            _controller.SetEntryPrice(20000);
            _controller.PlaceDefaultStop();

            TradePlan plan = null;
            _controller.PlanUpdated += p => plan = p;
            _controller.Recalculate();
            Assert.True(plan.IsValid);

            _coordinator.Execute(plan);
            _gateway.ThrowOnSubmitBracket = true; // bracket submission blows up after the fill
            _gateway.FireEntryFilled(20001.25);

            Assert.Equal(TradeState.Active, _sm.Current);
            Assert.Empty(_gateway.Brackets);

            // The monitor was activated via BracketPlaced despite the failure:
            // armed for auto BE AND manual BE works for the open position.
            Assert.True(_monitor.IsArmed);
            Assert.Equal(20021.25, _monitor.TriggerPrice);
            Assert.Equal(BreakEvenResult.Moved, _monitor.MoveToBreakEven());
            Assert.Equal(20001.25, Assert.Single(_gateway.StopMoves));
            Assert.Equal(TradeState.BreakEvenTriggered, _sm.Current);
        }

        [Fact]
        public void Auto_be_disabled_by_zero_rr_skips_auto_trigger_but_manual_be_still_works()
        {
            // Local wiring with a zero BreakEvenRr config (what the VM produces
            // when Auto BE is unchecked) — the shared _monitor would otherwise
            // also react to the price feed.
            var gateway = new FakeOrderGateway();
            var feed = new FakePriceFeed();
            var noAutoConfig = new TradeConfiguration(RiskMode.FixedAmount, 800, 4.0, 0.0);
            var coordinator = new BracketExecutionCoordinator(gateway, _sm, _instrument);
            var monitor = new BreakEvenMonitor(feed, gateway, _sm);
            coordinator.BracketPlaced += b =>
                monitor.Activate(b.FillPrice, b.StopPrice, noAutoConfig.BreakEvenRr, b.Direction);

            _sm.TransitionTo(TradeState.Planning);
            _controller.SetEntryPrice(20000);
            _controller.PlaceDefaultStop();

            TradePlan plan = null;
            _controller.PlanUpdated += p => plan = p;
            _controller.Recalculate();

            coordinator.Execute(plan);
            gateway.FireEntryFilled(20000);

            Assert.Equal(TradeState.Active, _sm.Current);
            Assert.False(monitor.IsArmed); // auto BE disabled by BreakEvenRr = 0

            feed.SetPrice(20100); // would have crossed any trigger — nothing happens
            Assert.Empty(gateway.StopMoves);

            Assert.Equal(BreakEvenResult.Moved, monitor.MoveToBreakEven()); // manual still works
            Assert.Equal(20000, Assert.Single(gateway.StopMoves));
            monitor.Dispose();
            coordinator.Dispose();
        }

        [Fact]
        public void Keyboard_flow_nudges_sl_by_exactly_one_step_and_tp_tracks_rr()
        {
            _sm.TransitionTo(TradeState.Planning);
            _controller.SetEntryPrice(20000);
            _controller.PlaceDefaultStop();

            TradePlan plan = null;
            _controller.PlanUpdated += p => plan = p;

            _controller.NudgeStop(+1);
            Assert.Equal(25, plan.SlDistancePoints);
            Assert.Equal(19975, plan.StopPrice);
            Assert.Equal(20100, plan.TpPrice); // 25 × 4 = 100 pts
            Assert.Equal(4.0, (plan.TpPrice - plan.EntryPrice) / (plan.EntryPrice - plan.StopPrice), 10);

            _controller.NudgeStop(-1);
            Assert.Equal(20, plan.SlDistancePoints);
            Assert.Equal(19980, plan.StopPrice);
            Assert.Equal(20080, plan.TpPrice);
        }

        [Fact]
        public void Drag_flow_sets_snapped_stop_distance_and_tp_is_always_derived()
        {
            _sm.TransitionTo(TradeState.Planning);
            _controller.SetEntryPrice(20000);
            _controller.PlaceDefaultStop();

            var axis = new FakePriceAxisConverter();
            var chart = new ChartInteractionController(axis);
            chart.UpdateSnapshot(_controller.EntryPrice, _controller.StopPrice, 100, 300);

            // Grab the SL line (19980 → y=1960) and drag to y=1980 → price 19990.
            Assert.True(chart.TryBeginDrag(200, axis.YFromPrice(19980)));
            Assert.True(chart.TryDragTo(1980, out double rawPrice));
            Assert.Equal(19990, rawPrice);

            _controller.SetStopFromPrice(rawPrice);
            chart.EndDrag();

            TradePlan plan = null;
            _controller.PlanUpdated += p => plan = p;
            _controller.Recalculate();

            Assert.True(plan.IsValid);
            Assert.Equal(10, plan.SlDistancePoints); // snapped multiple of 5
            Assert.Equal(19990, plan.StopPrice);
            // TP still derived from RR — the drag can only ever move the stop.
            Assert.Equal(20040, plan.TpPrice);
            Assert.Equal(4.0, (plan.TpPrice - plan.EntryPrice) / (plan.EntryPrice - plan.StopPrice), 10);
            Assert.False(chart.IsDragging);

            // There is deliberately no TP mutation API on the controller.
            var publicMethods = typeof(TradePlannerController)
                .GetMethods()
                .Select(m => m.Name)
                .ToArray();
            Assert.Contains("SetStopFromPrice", publicMethods);
            Assert.DoesNotContain(publicMethods,
                m => m.Contains("Tp", StringComparison.OrdinalIgnoreCase) ||
                     m.Contains("TakeProfit", StringComparison.OrdinalIgnoreCase));
        }
    }
}
