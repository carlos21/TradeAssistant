using System;
using TradeAssistant.Application.Ports;
using TradeAssistant.Domain;

namespace TradeAssistant.Application
{
    /// <summary>
    /// Owns the planner inputs, derives SL/TP from the configured risk/RR rules,
    /// and emits a new TradePlan on every change. No NinjaTrader references.
    ///
    /// Invariants enforced here:
    ///  - SL distance is always a positive multiple of TradeConfiguration.SlStepPoints.
    ///  - TP is always derived as SL distance × RR — the configured RR never mutates.
    /// </summary>
    public sealed class TradePlannerController
    {
        private readonly IInstrumentInfoProvider _instrument;
        private IAccountDataProvider _account;
        private TradeConfiguration _config;
        private TradeDirection     _direction = TradeDirection.Long;
        private double             _entryPrice;
        private double             _slDistancePoints;
        private bool               _hasStop;

        public event Action<TradePlan> PlanUpdated;

        public TradePlannerController(
            IInstrumentInfoProvider instrument,
            IAccountDataProvider    account,
            TradeConfiguration      config = null)
        {
            _instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
            _account    = account    ?? throw new ArgumentNullException(nameof(account));
            _config     = config     ?? TradeConfiguration.Default;
        }

        public TradeConfiguration Config          => _config;
        public TradeDirection     Direction       => _direction;
        public double             EntryPrice      => _entryPrice;
        public bool               HasStop         => _hasStop;
        public double             SlDistancePoints => _hasStop ? _slDistancePoints : 0;

        /// <summary>Derived stop price (0 when no stop placed or no entry yet).</summary>
        public double StopPrice => !_hasStop || _entryPrice <= 0
            ? 0
            : StopSnapper.StopPriceFor(_entryPrice, _slDistancePoints, _direction,
                _instrument.TickSize, _config.SlStepPoints);

        // ── Configuration ───────────────────────────────────────────────────

        public void UpdateConfiguration(TradeConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (_hasStop)
                _slDistancePoints = StopSnapper.SnapDistance(_slDistancePoints, _config.SlStepPoints);
            Recalculate();
        }

        public void SetRiskMode(RiskMode mode)   => UpdateConfiguration(_config.With(riskMode: mode));
        public void SetRiskValue(double value)   => UpdateConfiguration(_config.With(riskValue: value));

        /// <summary>Atomic mode+value change — avoids invalid intermediate states
        /// (e.g. FixedAmount with a sub-minimum value during the transition).</summary>
        public void SetRiskModeAndValue(RiskMode mode, double value) =>
            UpdateConfiguration(_config.With(riskMode: mode, riskValue: value));

        /// <summary>
        /// Sets the SL step in points. The step is snapped to the instrument
        /// tick (minimum one tick) so every derived stop price stays tick-aligned.
        /// An already-placed stop is re-snapped to the new step by
        /// <see cref="UpdateConfiguration"/>.
        /// </summary>
        public void SetSlStepPoints(double value)
        {
            if (value <= 0)
                throw new ArgumentException("SL step must be positive.", nameof(value));

            double snapped = Math.Max(
                _instrument.TickSize, StopSnapper.SnapToTick(value, _instrument.TickSize));

            if (snapped > _config.DefaultSlPoints)
                throw new ArgumentException(
                    $"SL step cannot exceed the default SL distance ({_config.DefaultSlPoints:0.##} pts).",
                    nameof(value));

            UpdateConfiguration(_config.With(slStepPoints: snapped));
        }
        public void SetRrRatio(double value)     => UpdateConfiguration(_config.With(rrRatio: value));
        public void SetBreakEvenRr(double value) => UpdateConfiguration(_config.With(breakEvenRr: value));

        /// <summary>Replaces the account data provider (e.g. ChartTrader account change).</summary>
        public void ReplaceAccountDataProvider(IAccountDataProvider account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        // ── Trade geometry ──────────────────────────────────────────────────

        /// <summary>Flips direction, keeping the same SL distance mirrored to the other side.</summary>
        public void SetDirection(TradeDirection direction)
        {
            _direction = direction;
            Recalculate();
        }

        /// <summary>Live entry price (last traded price). Stop follows at the same distance.</summary>
        public void SetEntryPrice(double price)
        {
            _entryPrice = price;
            Recalculate();
        }

        /// <summary>Places the default stop distance from the configuration.</summary>
        public void PlaceDefaultStop()
        {
            _slDistancePoints = StopSnapper.SnapDistance(_config.DefaultSlPoints, _config.SlStepPoints);
            _hasStop = true;
            Recalculate();
        }

        /// <summary>
        /// Drag input: the SL distance derives from how far the dragged price is
        /// from the entry, snapped to the step. Direction decides which side the
        /// stop sits on, so validity is preserved by construction.
        /// </summary>
        public void SetStopFromPrice(double price)
        {
            if (_entryPrice <= 0) return;
            _slDistancePoints = StopSnapper.DistanceBetween(_entryPrice, price, _config.SlStepPoints);
            _hasStop = true;
            Recalculate();
        }

        /// <summary>
        /// Keyboard input: nudges the SL distance by <paramref name="steps"/>
        /// steps of SlStepPoints (+1 = wider, -1 = tighter). Clamped at one step.
        /// </summary>
        public void NudgeStop(int steps)
        {
            double next = (_hasStop ? _slDistancePoints : 0) + steps * _config.SlStepPoints;
            _slDistancePoints = StopSnapper.SnapDistance(next, _config.SlStepPoints);
            _hasStop = true;
            Recalculate();
        }

        /// <summary>Clears the stop so planning starts over (e.g. after reset).</summary>
        public void ClearStop()
        {
            _hasStop = false;
            _slDistancePoints = 0;
            Recalculate();
        }

        // ── Recalculation ───────────────────────────────────────────────────

        public void Recalculate()
        {
            try
            {
                PlanUpdated?.Invoke(ComputePlan());
            }
            catch (Exception ex)
            {
                PlanUpdated?.Invoke(TradePlan.Invalid($"Calculation error: {ex.Message}"));
            }
        }

        private TradePlan ComputePlan()
        {
            if (_entryPrice <= 0)
                return TradePlan.Invalid("Entry price not set.");

            if (!_hasStop)
                return TradePlan.Invalid("Stop not placed.");

            double stopPrice = StopPrice;

            IDirectionStrategy strategy = _direction == TradeDirection.Long
                ? (IDirectionStrategy)LongStrategy.Instance
                : ShortStrategy.Instance;

            if (!strategy.IsStopValid(_entryPrice, stopPrice))
                return TradePlan.Invalid(_direction == TradeDirection.Long
                    ? "Stop must be below entry for Long trades."
                    : "Stop must be above entry for Short trades.");

            double balance = _account.AccountBalance;
            if (balance <= 0)
                return TradePlan.Invalid("Account balance is zero or unavailable.");

            double riskAmount = RiskCalculator.Calculate(balance, _config.RiskMode, _config.RiskValue);

            var sizing = FuturesPositionSizer.Calculate(
                _entryPrice, stopPrice, _instrument.TickSize, _instrument.TickValue, riskAmount,
                TradeConfiguration.MinRiskDollars);

            if (sizing.error != null)
                return TradePlan.Invalid(sizing.error);

            if (sizing.contracts < 1)
                return TradePlan.Invalid("0 contracts — risk amount too small for this stop distance.");

            double tpPrice = StopSnapper.SnapToTick(
                strategy.CalcTp(_entryPrice, stopPrice, _config.RrRatio), _instrument.TickSize);

            double riskDollars   = sizing.contracts * sizing.riskPerContract;
            double tpTicks       = Math.Abs(tpPrice - _entryPrice) / _instrument.TickSize;
            double profitDollars = sizing.contracts * tpTicks * _instrument.TickValue;

            return TradePlan.Valid(
                sizing.contracts, riskDollars, profitDollars,
                _entryPrice, stopPrice, tpPrice,
                _slDistancePoints, _config.RrRatio, _direction, sizing.stopDistanceTicks);
        }
    }
}
