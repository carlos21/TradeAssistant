using System;
using TradeAssistant.Domain;
using TradeAssistant.Infrastructure;

namespace TradeAssistant.Application
{
    /// <summary>
    /// Reacts to any input change, rebuilds TradeConfiguration, runs domain
    /// calculations, and emits a new TradePlan via the PlanUpdated event.
    /// 
    /// Now integrated with ATM strategy support for native ChartTrader visualization.
    /// </summary>
    public sealed class TradePlannerController
    {
        private readonly IInstrumentInfoProvider _instrument;
        private readonly IAccountDataProvider    _account;
        private AtmStrategyService               _atmService;

        // Current mutable inputs
        private RiskMode        _riskMode         = RiskMode.FixedAmount;
        private double          _riskValue        = 240.0;  // % or $ depending on mode
        private double          _rrRatio          = 4.0;    // Changed to 4.0 for ATM approach
        private TradeDirection  _direction        = TradeDirection.Long;
        private double          _entryPrice       = 0;
        private double          _stopPrice        = 0;

        // SL tier management - when true, stop price is derived from current tier
        private bool            _useSlTiers = true;

        public event Action<TradePlan> PlanUpdated;
        public event Action<SlTierInfo> TierChanged;

        public TradePlannerController(
            IInstrumentInfoProvider instrument,
            IAccountDataProvider    account)
        {
            _instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
            _account    = account    ?? throw new ArgumentNullException(nameof(account));
            
            InitializeAtmService();
        }

        private void InitializeAtmService()
        {
            // Initialize ATM service with current instrument
            try
            {
                _atmService = new AtmStrategyService(
                    _instrument.InstrumentName,
                    _instrument.PointValue,
                    _riskValue,
                    _rrRatio);
            }
            catch
            {
                // Fallback if instrument not available yet
                _atmService = null;
            }
        }

        /// <summary>
        /// Gets the current ATM strategy service
        /// </summary>
        public AtmStrategyService AtmService => _atmService;

        /// <summary>
        /// Gets the ATM strategy name for current tier
        /// </summary>
        public string CurrentAtmStrategyName => _atmService?.GetCurrentAtmStrategyName();

        /// <summary>
        /// Gets current tier info
        /// </summary>
        public SlTierInfo CurrentTierInfo => _atmService?.GetCurrentTierInfo();

        // ── Input setters — each triggers a recalculate ─────────────────────

        public void SetRiskMode(RiskMode mode)
        {
            _riskMode = mode;
            Recalculate();
        }

        public void SetRiskValue(double value)
        {
            _riskValue = value;
            // Reinitialize ATM service with new risk value
            if (_atmService != null)
            {
                _atmService = new AtmStrategyService(
                    _instrument.InstrumentName,
                    _instrument.PointValue,
                    _riskValue,
                    _rrRatio);
            }
            Recalculate();
        }

        public void SetRrRatio(double value)      { _rrRatio     = value; Recalculate(); }
        
        public void SetDirection(TradeDirection d) 
        { 
            _direction = d;
            // Recalculate stop price based on new direction and current tier
            if (_useSlTiers && _atmService != null && _entryPrice > 0)
            {
                _stopPrice = _atmService.CalculateStopPrice(_entryPrice, _direction == TradeDirection.Long);
            }
            Recalculate(); 
        }
        
        public void SetEntryPrice(double price)   
        { 
            _entryPrice = price;
            // Update stop price based on current tier
            if (_useSlTiers && _atmService != null && _entryPrice > 0)
            {
                _stopPrice = _atmService.CalculateStopPrice(_entryPrice, _direction == TradeDirection.Long);
            }
            Recalculate(); 
        }

        public void SetStopPrice(double price)
        {
            // When user manually sets stop, snap to nearest tier
            if (_useSlTiers && _atmService != null && _entryPrice > 0)
            {
                double slDistance = Math.Abs(price - _entryPrice);
                _atmService.SetTierBySlPoints(slDistance);
                _stopPrice = _atmService.CalculateStopPrice(_entryPrice, _direction == TradeDirection.Long);
                TierChanged?.Invoke(_atmService.GetCurrentTierInfo());
            }
            else
            {
                _stopPrice = price;
            }
            Recalculate();
        }

        /// <summary>
        /// Cycle to next SL tier (UP key)
        /// </summary>
        public void CycleTierUp()
        {
            if (_atmService == null) return;
            if (_atmService.CycleUp())
            {
                // Update stop price for new tier
                if (_entryPrice > 0)
                {
                    _stopPrice = _atmService.CalculateStopPrice(_entryPrice, _direction == TradeDirection.Long);
                }
                TierChanged?.Invoke(_atmService.GetCurrentTierInfo());
                Recalculate();
            }
        }

        /// <summary>
        /// Cycle to previous SL tier (DOWN key)
        /// </summary>
        public void CycleTierDown()
        {
            if (_atmService == null) return;
            if (_atmService.CycleDown())
            {
                // Update stop price for new tier
                if (_entryPrice > 0)
                {
                    _stopPrice = _atmService.CalculateStopPrice(_entryPrice, _direction == TradeDirection.Long);
                }
                TierChanged?.Invoke(_atmService.GetCurrentTierInfo());
                Recalculate();
            }
        }

        /// <summary>
        /// Set tier by index directly
        /// </summary>
        public void SetTierByIndex(int index)
        {
            if (_atmService == null) return;
            var tiers = _atmService.GetAllTiers();
            if (index >= 0 && index < tiers.Count)
            {
                _atmService.SetTierBySlPoints(tiers[index].SlPoints);
                if (_entryPrice > 0)
                {
                    _stopPrice = _atmService.CalculateStopPrice(_entryPrice, _direction == TradeDirection.Long);
                }
                TierChanged?.Invoke(_atmService.GetCurrentTierInfo());
                Recalculate();
            }
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
                return TradePlan.Invalid("Stop price not set.");

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

            // Compute TP price
            double tpPrice = strategy.CalcTp(_entryPrice, _stopPrice, _rrRatio);

            // When using ATM service, get contracts from it (overrides standard calculation)
            int contracts;
            double actualRisk;
            double profitDollars;
            double stopTicks = Math.Abs(_entryPrice - _stopPrice) / tickSize;

            if (_useSlTiers && _atmService != null)
            {
                var tierInfo = _atmService.GetCurrentTierInfo();
                contracts = tierInfo.Contracts;
                actualRisk = tierInfo.RiskAmount;
                profitDollars = tierInfo.ProfitPotential;
            }
            else
            {
                // Standard calculation
                double riskAmount;
                try
                {
                    riskAmount = RiskCalculator.Calculate(balance, _riskMode, _riskValue);
                }
                catch (ArgumentException ex)
                {
                    return TradePlan.Invalid(ex.Message, tpPrice, 0);
                }

                var (calcContracts, _, riskPerContract, error) =
                    FuturesPositionSizer.Calculate(_entryPrice, _stopPrice, tickSize, tickValue, riskAmount);

                if (error != null)
                    return TradePlan.Invalid(error, tpPrice, 0);

                if (calcContracts < 1)
                    return TradePlan.Invalid(
                        $"Insufficient risk budget. Need ${riskPerContract:F0}/contract, budget ${riskAmount:F0}.",
                        tpPrice, 0);

                contracts = calcContracts;
                actualRisk = contracts * riskPerContract;
                profitDollars = contracts * stopTicks * _rrRatio * tickValue;
            }

            return TradePlan.Valid(
                contracts, actualRisk, profitDollars,
                _entryPrice, _stopPrice, tpPrice, 0, stopTicks);
        }
    }
}
