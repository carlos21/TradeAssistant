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
        private double          _riskPercent      = 1.0;
        private double          _fixedRiskDollars = 0;
        private double          _rrRatio          = 2.0;
        private double          _breakEvenRr      = 1.0;
        private TradeDirection  _direction        = TradeDirection.Long;
        private double          _entryPrice       = 0;
        private double          _stopPrice        = 0;

        public event Action<TradePlan> PlanUpdated;

        public TradePlannerController(
            IInstrumentInfoProvider instrument,
            IAccountDataProvider    account)
        {
            _instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
            _account    = account    ?? throw new ArgumentNullException(nameof(account));
        }

        // ── Input setters — each triggers a recalculate ─────────────────────

        public void SetRiskPercent(double value)
        {
            _riskPercent = value;
            _fixedRiskDollars = 0;
            Recalculate();
        }

        public void SetFixedRiskDollars(double value)
        {
            _fixedRiskDollars = value;
            _riskPercent = 0;
            Recalculate();
        }

        public void SetRrRatio(double value)      { _rrRatio     = value; Recalculate(); }
        public void SetBreakEvenRr(double value)  { _breakEvenRr = value; Recalculate(); }
        public void SetDirection(TradeDirection d) { _direction   = d;     Recalculate(); }
        public void SetEntryPrice(double price)   { _entryPrice  = price; Recalculate(); }
        public void SetStopPrice(double price)    { _stopPrice   = price; Recalculate(); }

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

            if (!strategy.IsStopValid(_entryPrice, _stopPrice))
                return TradePlan.Invalid(
                    _direction == TradeDirection.Long
                        ? "Stop must be below entry for Long trades."
                        : "Stop must be above entry for Short trades.");

            // Calculate risk amount
            double riskAmount;
            try
            {
                riskAmount = RiskCalculator.Calculate(balance, _riskPercent, _fixedRiskDollars);
            }
            catch (ArgumentException ex)
            {
                return TradePlan.Invalid(ex.Message);
            }

            // Position sizing
            var (contracts, stopTicks, riskPerContract, error) =
                FuturesPositionSizer.Calculate(_entryPrice, _stopPrice, tickSize, tickValue, riskAmount);

            if (error != null)
                return TradePlan.Invalid(error);

            if (contracts < 1)
                return TradePlan.Invalid(
                    $"Insufficient risk budget. Need ${riskPerContract:F0}/contract, budget ${riskAmount:F0}.");

            // Compute TP and break-even prices
            double tpPrice      = strategy.CalcTp(_entryPrice, _stopPrice, _rrRatio);
            double bePrice      = strategy.CalcBreakEven(_entryPrice, _stopPrice, _breakEvenRr);
            double profitDollars = contracts * stopTicks * _rrRatio * tickValue;
            double actualRisk    = contracts * riskPerContract;

            return TradePlan.Valid(
                contracts, actualRisk, profitDollars,
                _entryPrice, _stopPrice, tpPrice, bePrice, stopTicks);
        }
    }
}
