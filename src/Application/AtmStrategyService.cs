using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeAssistant.Application
{
    /// <summary>
    /// Manages ATM strategy discovery and SL tier configuration.
    /// Maps SL distances to ATM strategy templates.
    /// </summary>
    public sealed class AtmStrategyService
    {
        private readonly string _instrumentName;
        private readonly double _pointValue;
        private readonly double _targetRisk;
        private readonly double _rrRatio;

        // Predefined SL tiers in points (configurable)
        private readonly double[] _slTiers = { 15, 20, 30, 40 };
        
        private int _currentTierIndex = 1; // Default to 20pt tier
        private List<string> _availableAtmStrategies = new List<string>();

        public AtmStrategyService(string instrumentName, double pointValue, double targetRisk = 240.0, double rrRatio = 4.0)
        {
            _instrumentName = instrumentName ?? throw new ArgumentNullException(nameof(instrumentName));
            _pointValue = pointValue;
            _targetRisk = targetRisk;
            _rrRatio = rrRatio;
        }

        /// <summary>
        /// Current SL tier distance in points
        /// </summary>
        public double CurrentSlPoints => _slTiers[_currentTierIndex];

        /// <summary>
        /// Current tier index (0-based)
        /// </summary>
        public int CurrentTierIndex => _currentTierIndex;

        /// <summary>
        /// Total number of available tiers
        /// </summary>
        public int TierCount => _slTiers.Length;

        /// <summary>
        /// Get the ATM strategy name for the current tier
        /// </summary>
        public string GetCurrentAtmStrategyName()
        {
            return $"TA_{_instrumentName}_{(int)CurrentSlPoints}pt";
        }

        /// <summary>
        /// Calculate contracts for current tier based on target risk
        /// </summary>
        public int CalculateContracts()
        {
            double slPoints = CurrentSlPoints;
            double riskPerContract = slPoints * _pointValue;
            
            if (riskPerContract <= 0) return 0;
            
            int contracts = (int)Math.Floor(_targetRisk / riskPerContract);
            return Math.Max(1, contracts); // At least 1 contract
        }

        /// <summary>
        /// Calculate actual risk amount for current tier
        /// </summary>
        public double CalculateActualRisk()
        {
            int contracts = CalculateContracts();
            double riskPerContract = CurrentSlPoints * _pointValue;
            return contracts * riskPerContract;
        }

        /// <summary>
        /// Calculate TP distance for current tier (based on RR ratio)
        /// </summary>
        public double CalculateTpPoints()
        {
            return CurrentSlPoints * _rrRatio;
        }

        /// <summary>
        /// Calculate potential profit for current tier
        /// </summary>
        public double CalculatePotentialProfit()
        {
            int contracts = CalculateContracts();
            double tpPoints = CalculateTpPoints();
            return contracts * tpPoints * _pointValue;
        }

        /// <summary>
        /// Move to next tier (UP key)
        /// </summary>
        public bool CycleUp()
        {
            if (_currentTierIndex >= _slTiers.Length - 1) return false;
            _currentTierIndex++;
            return true;
        }

        /// <summary>
        /// Move to previous tier (DOWN key)
        /// </summary>
        public bool CycleDown()
        {
            if (_currentTierIndex <= 0) return false;
            _currentTierIndex--;
            return true;
        }

        /// <summary>
        /// Set tier by SL points value (snaps to nearest tier)
        /// </summary>
        public void SetTierBySlPoints(double slPoints)
        {
            // Find nearest tier
            int nearestIndex = 0;
            double minDiff = Math.Abs(slPoints - _slTiers[0]);
            
            for (int i = 1; i < _slTiers.Length; i++)
            {
                double diff = Math.Abs(slPoints - _slTiers[i]);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    nearestIndex = i;
                }
            }
            
            _currentTierIndex = nearestIndex;
        }

        /// <summary>
        /// Get all available tier information
        /// </summary>
        public IReadOnlyList<SlTierInfo> GetAllTiers()
        {
            var tiers = new List<SlTierInfo>();
            int savedIndex = _currentTierIndex;
            
            for (int i = 0; i < _slTiers.Length; i++)
            {
                _currentTierIndex = i;
                tiers.Add(new SlTierInfo
                {
                    Index = i,
                    SlPoints = _slTiers[i],
                    Contracts = CalculateContracts(),
                    RiskAmount = CalculateActualRisk(),
                    TpPoints = CalculateTpPoints(),
                    ProfitPotential = CalculatePotentialProfit(),
                    AtmStrategyName = GetCurrentAtmStrategyName()
                });
            }
            
            _currentTierIndex = savedIndex;
            return tiers;
        }

        /// <summary>
        /// Get current tier information
        /// </summary>
        public SlTierInfo GetCurrentTierInfo()
        {
            return new SlTierInfo
            {
                Index = _currentTierIndex,
                SlPoints = CurrentSlPoints,
                Contracts = CalculateContracts(),
                RiskAmount = CalculateActualRisk(),
                TpPoints = CalculateTpPoints(),
                ProfitPotential = CalculatePotentialProfit(),
                AtmStrategyName = GetCurrentAtmStrategyName()
            };
        }

        /// <summary>
        /// Calculate stop price based on entry and current tier
        /// </summary>
        public double CalculateStopPrice(double entryPrice, bool isLong)
        {
            double slDistance = CurrentSlPoints;
            return isLong ? entryPrice - slDistance : entryPrice + slDistance;
        }

        /// <summary>
        /// Calculate TP price based on entry and current tier
        /// </summary>
        public double CalculateTpPrice(double entryPrice, bool isLong)
        {
            double tpDistance = CalculateTpPoints();
            return isLong ? entryPrice + tpDistance : entryPrice - tpDistance;
        }
    }

    /// <summary>
    /// Information about an SL tier
    /// </summary>
    public sealed class SlTierInfo
    {
        public int Index { get; set; }
        public double SlPoints { get; set; }
        public int Contracts { get; set; }
        public double RiskAmount { get; set; }
        public double TpPoints { get; set; }
        public double ProfitPotential { get; set; }
        public string AtmStrategyName { get; set; }
    }
}
