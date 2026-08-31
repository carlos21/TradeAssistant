using System;
using System.Collections.Generic;

namespace TradeAssistant.Domain
{
    /// <summary>
    /// Enforces explicit, validated state transitions.
    /// Raises StateChanged whenever a transition succeeds.
    /// </summary>
    public sealed class TradeStateMachine
    {
        private static readonly Dictionary<TradeState, HashSet<TradeState>> _allowed =
            new Dictionary<TradeState, HashSet<TradeState>>
            {
                { TradeState.Idle,               new HashSet<TradeState> { TradeState.Planning } },
                { TradeState.Planning,           new HashSet<TradeState> { TradeState.Armed, TradeState.Idle } },
                { TradeState.Armed,              new HashSet<TradeState> { TradeState.Submitted, TradeState.Planning, TradeState.Cancelled } },
                { TradeState.Submitted,          new HashSet<TradeState> { TradeState.Active, TradeState.Cancelled } },
                { TradeState.Active,             new HashSet<TradeState> { TradeState.BreakEvenTriggered, TradeState.Closed, TradeState.Cancelled } },
                { TradeState.BreakEvenTriggered, new HashSet<TradeState> { TradeState.Closed, TradeState.Cancelled } },
                { TradeState.Closed,             new HashSet<TradeState> { TradeState.Idle } },
                { TradeState.Cancelled,          new HashSet<TradeState> { TradeState.Idle } },
            };

        public TradeState Current { get; private set; } = TradeState.Idle;

        public event Action<TradeState, TradeState> StateChanged;

        /// <summary>
        /// Attempts the transition to <paramref name="next"/>.
        /// Throws InvalidOperationException if the transition is not allowed.
        /// </summary>
        public void TransitionTo(TradeState next)
        {
            if (!_allowed.TryGetValue(Current, out var permitted) || !permitted.Contains(next))
                throw new InvalidOperationException(
                    $"Invalid state transition: {Current} → {next}");

            TradeState previous = Current;
            Current = next;
            StateChanged?.Invoke(previous, next);
        }

        /// <summary>
        /// Returns true and performs the transition if allowed; otherwise returns false without throwing.
        /// </summary>
        public bool TryTransitionTo(TradeState next)
        {
            if (!_allowed.TryGetValue(Current, out var permitted) || !permitted.Contains(next))
                return false;

            TransitionTo(next);
            return true;
        }

        /// <summary>Resets the machine to Idle from any terminal or error state.</summary>
        public void Reset()
        {
            TradeState previous = Current;
            Current = TradeState.Idle;
            if (previous != TradeState.Idle)
                StateChanged?.Invoke(previous, TradeState.Idle);
        }
    }
}
