using System;
using TradeAssistant.Domain;
using TradeAssistant.Infrastructure;

namespace TradeAssistant.Application
{
    /// <summary>
    /// Reacts to any input change, rebuilds TradeConfiguration, runs domain
    /// calculations, and emits a new TradePlan via the PlanUpdated event.
    /// </summary>
    public sealed class TradePlannerController
    {
        private readonly IInstrumentInfoProvider _instrument;
        private readonly IAccountDataProvider    _account;

        // Current mutable inputs
        private RiskMode        _riskMode         = RiskMode.FixedAmount;
        private double          _riskValue        = 240.0;  // % or $ depending on mode
        private double          _rrRatio          = 2.0;
        private double          _breakEvenRr      = 0.0;   // 0 = disabled (optional)
        private TradeDirection  _direction        = TradeDirection.Long;
        private double          _entryPrice       = 0;
        private double          _stopPrice        = 0;

        // Fixed SL distance in ticks - set when user manually places SL, kept constant
        private double          _fixedSlDistanceTicks = 0;
        private bool            _slManuallySet = false;

        public event Action<TradePlan> PlanUpdated;

        public TradePlannerController(
            IInstrumentInfoProvider instrument,
            IAccountDataProvider    account)
        {
            _instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
            _account    = account    ?? throw new ArgumentNullException(nameof(account));
        }

        // ── Input setters — each triggers a recalculate ─────────────────────

        public void SetRiskMode(RiskMode mode)
        {
            _riskMode = mode;
            Recalculate();
        }

        public void SetRiskValue(double value)
        {
            _riskValue = value;
            Recalculate();
        }

        public void SetRrRatio(double value)      { _rrRatio     = value; Recalculate(); }
        public void SetBreakEvenRr(double value)  { _breakEvenRr = value; Recalculate(); }
        public void SetDirection(TradeDirection d) { _direction   = d;     Recalculate(); }
        public void SetEntryPrice(double price)   { _entryPrice  = price; Recalculate(); }
        public void SetStopPrice(double price)
        {
            _stopPrice = price;
            _slManuallySet = true;
            // Capture the SL distance in ticks when user sets it
            if (_entryPrice > 0 && _instrument != null)
            {
                double tickSize = _instrument.TickSize;
                if (tickSize > 0)
                    _fixedSlDistanceTicks = Math.Abs(_entryPrice - _stopPrice) / tickSize;
            }
            Recalculate();
        }

        // ── Core recalculation ───────────────────────────────────────────────

        public void Recalculate()
        {
            try
            {
                TradePlan plan = ComputePlan();
                PlanUpdated?.Invoke(plan);
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

            if (_stopPrice <= 0)
                return TradePlan.Invalid("Stop price not set. Click on chart to place SL.");

            double tickSize  = _instrument.TickSize;
            double tickValue = _instrument.TickValue;
            double balance   = _account.AccountBalance;

            if (balance <= 0)
                return TradePlan.Invalid("Account balance is zero or unavailable.");

            // Get direction strategy
            IDirectionStrategy strategy = _direction == TradeDirection.Long
                ? (IDirectionStrategy)LongStrategy.Instance
                : ShortStrategy.Instance;

            // Maintain constant SL distance: recalculate stop price based on current entry
            // to make SL float with price. _fixedSlDistanceTicks is only updated when
            // user manually drags the SL via SetStopPrice().
            if (_slManuallySet && _fixedSlDistanceTicks > 0 && _entryPrice > 0 && tickSize > 0)
            {
                double fixedDistance = _fixedSlDistanceTicks * tickSize;
                _stopPrice = _direction == TradeDirection.Long
                    ? _entryPrice - fixedDistance
                    : _entryPrice + fixedDistance;
            }

            if (!strategy.IsStopValid(_entryPrice, _stopPrice))
                return TradePlan.Invalid(
                    _direction == TradeDirection.Long
                        ? "Stop must be below entry for Long trades."
                        : "Stop must be above entry for Short trades.");

            // Compute TP and break-even prices (always calculate for visualization)
            double tpPrice = strategy.CalcTp(_entryPrice, _stopPrice, _rrRatio);
            double bePrice = _breakEvenRr > 0
                ? strategy.CalcBreakEven(_entryPrice, _stopPrice, _breakEvenRr)
                : 0;  // 0 = disabled

            // Calculate risk amount
            double riskAmount;
            try
            {
                riskAmount = RiskCalculator.Calculate(balance, _riskMode, _riskValue);
            }
            catch (ArgumentException ex)
            {
                return TradePlan.Invalid(ex.Message, tpPrice, bePrice);
            }

            // Position sizing
            var (contracts, stopTicks, riskPerContract, error) =
                FuturesPositionSizer.Calculate(_entryPrice, _stopPrice, tickSize, tickValue, riskAmount);

            if (error != null)
                return TradePlan.Invalid(error, tpPrice, bePrice);

            if (contracts < 1)
                return TradePlan.Invalid(
                    $"Insufficient risk budget. Need ${riskPerContract:F0}/contract, budget ${riskAmount:F0}.",
                    tpPrice, bePrice);

            double profitDollars = contracts * stopTicks * _rrRatio * tickValue;
            double actualRisk    = contracts * riskPerContract;

            return TradePlan.Valid(
                contracts, actualRisk, profitDollars,
                _entryPrice, _stopPrice, tpPrice, bePrice, stopTicks);
        }
    }
}
