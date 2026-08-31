using System;
using System.Collections.Generic;
using TradeAssistant.Domain;
using Xunit;

namespace TradeAssistant.Tests.Domain
{
    public class TradeStateMachineTests
    {
        [Fact]
        public void Starts_idle()
        {
            Assert.Equal(TradeState.Idle, new TradeStateMachine().Current);
        }

        [Fact]
        public void Walks_the_full_happy_path()
        {
            var sm = new TradeStateMachine();
            var seen = new List<(TradeState, TradeState)>();
            sm.StateChanged += (p, n) => seen.Add((p, n));

            var path = new[]
            {
                TradeState.Planning, TradeState.Armed, TradeState.Submitted,
                TradeState.Active, TradeState.BreakEvenTriggered,
                TradeState.Closed, TradeState.Idle
            };

            foreach (var next in path)
            {
                sm.TransitionTo(next);
                Assert.Equal(next, sm.Current);
            }

            Assert.Equal(path.Length, seen.Count);
            Assert.Equal((TradeState.Idle, TradeState.Planning), seen[0]);
            Assert.Equal((TradeState.Closed, TradeState.Idle), seen[^1]);
        }

        public static IEnumerable<object[]> AllAllowedTransitions()
        {
            var map = new Dictionary<TradeState, TradeState[]>
            {
                { TradeState.Idle,               new[] { TradeState.Planning } },
                { TradeState.Planning,           new[] { TradeState.Armed, TradeState.Idle } },
                { TradeState.Armed,              new[] { TradeState.Submitted, TradeState.Planning, TradeState.Cancelled } },
                { TradeState.Submitted,          new[] { TradeState.Active, TradeState.Cancelled } },
                { TradeState.Active,             new[] { TradeState.BreakEvenTriggered, TradeState.Closed, TradeState.Cancelled } },
                { TradeState.BreakEvenTriggered, new[] { TradeState.Closed, TradeState.Cancelled } },
                { TradeState.Closed,             new[] { TradeState.Idle } },
                { TradeState.Cancelled,          new[] { TradeState.Idle } },
            };
            foreach (var kv in map)
                foreach (var to in kv.Value)
                    yield return new object[] { kv.Key, to };
        }

        private static void DriveTo(TradeStateMachine sm, TradeState target)
        {
            var route = new Dictionary<TradeState, TradeState[]>
            {
                { TradeState.Planning,           new[] { TradeState.Planning } },
                { TradeState.Armed,              new[] { TradeState.Planning, TradeState.Armed } },
                { TradeState.Submitted,          new[] { TradeState.Planning, TradeState.Armed, TradeState.Submitted } },
                { TradeState.Active,             new[] { TradeState.Planning, TradeState.Armed, TradeState.Submitted, TradeState.Active } },
                { TradeState.BreakEvenTriggered, new[] { TradeState.Planning, TradeState.Armed, TradeState.Submitted, TradeState.Active, TradeState.BreakEvenTriggered } },
                { TradeState.Closed,             new[] { TradeState.Planning, TradeState.Armed, TradeState.Submitted, TradeState.Active, TradeState.Closed } },
                { TradeState.Cancelled,          new[] { TradeState.Planning, TradeState.Armed, TradeState.Submitted, TradeState.Cancelled } },
            };
            if (target == TradeState.Idle) return;
            foreach (var step in route[target])
                sm.TransitionTo(step);
        }

        [Theory]
        [MemberData(nameof(AllAllowedTransitions))]
        public void Every_allowed_transition_succeeds(TradeState from, TradeState to)
        {
            var sm = new TradeStateMachine();
            DriveTo(sm, from);
            Assert.Equal(from, sm.Current);
            sm.TransitionTo(to);
            Assert.Equal(to, sm.Current);
        }

        [Theory]
        [InlineData(TradeState.Idle, TradeState.Active)]
        [InlineData(TradeState.Idle, TradeState.Idle)]
        [InlineData(TradeState.Planning, TradeState.Active)]
        [InlineData(TradeState.Armed, TradeState.Idle)]
        [InlineData(TradeState.Active, TradeState.Planning)]
        [InlineData(TradeState.Closed, TradeState.Planning)]
        [InlineData(TradeState.Cancelled, TradeState.Active)]
        public void Disallowed_transitions_throw_and_keep_state(TradeState from, TradeState to)
        {
            var sm = new TradeStateMachine();
            DriveTo(sm, from);

            var ex = Assert.Throws<InvalidOperationException>(() => sm.TransitionTo(to));
            Assert.Equal($"Invalid state transition: {from} → {to}", ex.Message);
            Assert.Equal(from, sm.Current);
        }

        [Fact]
        public void TryTransitionTo_returns_false_without_throwing()
        {
            var sm = new TradeStateMachine();
            Assert.False(sm.TryTransitionTo(TradeState.Active));
            Assert.Equal(TradeState.Idle, sm.Current);

            Assert.True(sm.TryTransitionTo(TradeState.Planning));
            Assert.Equal(TradeState.Planning, sm.Current);
        }

        [Fact]
        public void Reset_returns_to_idle_and_raises_event_when_not_idle()
        {
            var sm = new TradeStateMachine();
            var seen = new List<(TradeState, TradeState)>();
            sm.StateChanged += (p, n) => seen.Add((p, n));

            sm.TransitionTo(TradeState.Planning);
            seen.Clear();

            sm.Reset();
            Assert.Equal(TradeState.Idle, sm.Current);
            Assert.Single(seen);
            Assert.Equal((TradeState.Planning, TradeState.Idle), seen[0]);
        }

        [Fact]
        public void Reset_from_idle_raises_no_event()
        {
            var sm = new TradeStateMachine();
            int events = 0;
            sm.StateChanged += (_, _) => events++;

            sm.Reset();
            Assert.Equal(TradeState.Idle, sm.Current);
            Assert.Equal(0, events);
        }
    }
}
