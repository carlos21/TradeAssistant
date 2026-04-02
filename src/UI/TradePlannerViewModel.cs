using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TradeAssistant.Application;
using TradeAssistant.Domain;

namespace TradeAssistant.UI
{
    /// <summary>
    /// ViewModel for the trade planner panel embedded in the chart.
    /// All bindable properties live here; no NinjaTrader references.
    /// </summary>
    public sealed class TradePlannerViewModel : ViewModelBase
    {
        private readonly TradePlannerController _controller;
        private readonly TradeStateMachine      _stateMachine;
        private readonly Dispatcher             _dispatcher;

        // Callbacks wired by the indicator for execution and direction toggle
        public Action ExecuteRequested      { get; set; }
        public Action ToggleDirectionAction { get; set; }
        public Action<int> TierChangedAction { get; set; } // Arg = new tier index

        // ── Input properties ─────────────────────────────────────────────────

        private RiskMode _riskMode = RiskMode.FixedAmount;
        public RiskMode RiskMode
        {
            get => _riskMode;
            set
            {
                SetProperty(ref _riskMode, value);
                OnPropertyChanged(nameof(RiskValueLabel));
                _controller.SetRiskMode(value);
            }
        }

        private double _riskValue = 240.0;
        public double RiskValue
        {
            get => _riskValue;
            set
            {
                SetProperty(ref _riskValue, value);
                _controller.SetRiskValue(value);
            }
        }

        public string RiskValueLabel => _riskMode == RiskMode.Percentage ? "Risk %" : "Risk $";

        private double _rrRatio = 4.0;
        public double RrRatio
        {
            get => _rrRatio;
            set
            {
                SetProperty(ref _rrRatio, value);
                _controller.SetRrRatio(value);
            }
        }

        private TradeDirection _direction = TradeDirection.Long;
        public TradeDirection Direction
        {
            get => _direction;
            set
            {
                SetProperty(ref _direction, value);
                OnPropertyChanged(nameof(DirectionLabel));
                _controller.SetDirection(value);
            }
        }

        public string DirectionLabel => Direction == TradeDirection.Long ? "LONG" : "SHORT";

        // ── Display properties ───────────────────────────────────────────────

        private bool _showTradeBoxes = true;
        public bool ShowTradeBoxes
        {
            get => _showTradeBoxes;
            set
            {
                if (_showTradeBoxes != value)
                {
                    SetProperty(ref _showTradeBoxes, value);
                    ShowTradeBoxesChanged?.Invoke(value);
                }
            }
        }

        public event Action<bool> ShowTradeBoxesChanged;

        // ── ATM Tier properties ──────────────────────────────────────────────

        private int _currentTierIndex = 1;
        public int CurrentTierIndex
        {
            get => _currentTierIndex;
            private set
            {
                if (_currentTierIndex != value)
                {
                    SetProperty(ref _currentTierIndex, value);
                    OnPropertyChanged(nameof(CurrentTierDisplay));
                    OnPropertyChanged(nameof(SlPoints));
                }
            }
        }

        private int _totalTiers = 4;
        public int TotalTiers
        {
            get => _totalTiers;
            private set => SetProperty(ref _totalTiers, value);
        }

        public string CurrentTierDisplay => $"Tier {_currentTierIndex + 1}/{_totalTiers}";

        private double _slPoints = 20;
        public double SlPoints
        {
            get => _slPoints;
            private set => SetProperty(ref _slPoints, value);
        }

        private string _atmStrategyName = "";
        public string AtmStrategyName
        {
            get => _atmStrategyName;
            private set => SetProperty(ref _atmStrategyName, value);
        }

        // ── Output / display properties ──────────────────────────────────────

        private int _contracts;
        public int Contracts
        {
            get => _contracts;
            private set => SetProperty(ref _contracts, value);
        }

        private double _riskDollars;
        public double RiskDollars
        {
            get => _riskDollars;
            private set => SetProperty(ref _riskDollars, value);
        }

        private double _profitDollars;
        public double ProfitDollars
        {
            get => _profitDollars;
            private set => SetProperty(ref _profitDollars, value);
        }

        private double _stopPrice;
        public double StopPrice
        {
            get => _stopPrice;
            private set => SetProperty(ref _stopPrice, value);
        }

        private double _tpPrice;
        public double TpPrice
        {
            get => _tpPrice;
            private set => SetProperty(ref _tpPrice, value);
        }

        private string _statusMessage = "Click ▲▼ to change SL tier, drag SL line to snap";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isArmed;
        public bool IsArmed
        {
            get => _isArmed;
            set => SetProperty(ref _isArmed, value);
        }

        private bool _canExecute;
        public bool CanExecuteTrade
        {
            get => _canExecute;
            set
            {
                SetProperty(ref _canExecute, value);
                (_executeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        // ── Commands ─────────────────────────────────────────────────────────

        private readonly ICommand _executeCommand;
        public ICommand ExecuteCommand => _executeCommand;

        private readonly ICommand _toggleDirectionCommand;
        public ICommand ToggleDirectionCommand => _toggleDirectionCommand;

        private readonly ICommand _cycleTierUpCommand;
        public ICommand CycleTierUpCommand => _cycleTierUpCommand;

        private readonly ICommand _cycleTierDownCommand;
        public ICommand CycleTierDownCommand => _cycleTierDownCommand;

        // ── Constructor ──────────────────────────────────────────────────────

        public TradePlannerViewModel(TradePlannerController controller, TradeStateMachine stateMachine)
        {
            _controller   = controller   ?? throw new ArgumentNullException(nameof(controller));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _dispatcher   = Dispatcher.CurrentDispatcher;

            _executeCommand = new RelayCommand(
                () => 
                {
                    try { ExecuteRequested?.Invoke(); }
                    catch (Exception ex) { StatusMessage = $"Execute error: {ex.Message}"; }
                },
                () => CanExecuteTrade && _stateMachine.Current == TradeState.Planning);

            _toggleDirectionCommand = new RelayCommand(() =>
            {
                try
                {
                    Direction = Direction == TradeDirection.Long
                        ? TradeDirection.Short
                        : TradeDirection.Long;
                    ToggleDirectionAction?.Invoke();
                }
                catch (Exception ex) { StatusMessage = $"Direction error: {ex.Message}"; }
            });

            _cycleTierUpCommand = new RelayCommand(
                () => 
                {
                    try { _controller.CycleTierUp(); }
                    catch (Exception ex) { StatusMessage = $"Tier up error: {ex.Message}"; }
                },
                () => _stateMachine.Current == TradeState.Planning);

            _cycleTierDownCommand = new RelayCommand(
                () => 
                {
                    try { _controller.CycleTierDown(); }
                    catch (Exception ex) { StatusMessage = $"Tier down error: {ex.Message}"; }
                },
                () => _stateMachine.Current == TradeState.Planning);

            _controller.PlanUpdated += OnPlanUpdated;
            _controller.TierChanged += OnTierChanged;
            _stateMachine.StateChanged += OnStateChanged;
        }

        private void RunOnUI(Action action)
        {
            if (_dispatcher.CheckAccess())
                action();
            else
                _dispatcher.BeginInvoke(action);
        }

        private void OnPlanUpdated(TradePlan plan)
        {
            RunOnUI(() =>
            {
                if (plan.IsValid)
                {
                    Contracts     = plan.Contracts;
                    RiskDollars   = plan.RiskDollars;
                    ProfitDollars = plan.ProfitDollars;
                    StopPrice     = plan.StopPrice;
                    TpPrice       = plan.TpPrice;
                    CanExecuteTrade = true;
                    
                    // Update tier info display
                    var tierInfo = _controller.CurrentTierInfo;
                    if (tierInfo != null)
                    {
                        SlPoints = tierInfo.SlPoints;
                        AtmStrategyName = tierInfo.AtmStrategyName;
                    }
                    
                    StatusMessage = $"{Contracts} ctr | SL {SlPoints:F0}pt | Risk ${RiskDollars:F0} | TP ${ProfitDollars:F0}";
                }
                else
                {
                    Contracts     = 0;
                    RiskDollars   = 0;
                    ProfitDollars = 0;
                    CanExecuteTrade = false;
                    StatusMessage = plan.ValidationError;
                }
            });
        }

        private void OnTierChanged(SlTierInfo tierInfo)
        {
            RunOnUI(() =>
            {
                if (tierInfo != null)
                {
                    CurrentTierIndex = tierInfo.Index;
                    SlPoints = tierInfo.SlPoints;
                    AtmStrategyName = tierInfo.AtmStrategyName;
                    TierChangedAction?.Invoke(tierInfo.Index);
                }
            });
        }

        private void OnStateChanged(TradeState previous, TradeState next)
        {
            RunOnUI(() =>
            {
                IsArmed = next == TradeState.Armed;
                (_executeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (_cycleTierUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (_cycleTierDownCommand as RelayCommand)?.RaiseCanExecuteChanged();

                switch (next)
                {
                    case TradeState.Idle:
                        StatusMessage   = "Click ▲▼ to change SL tier, drag SL line to snap";
                        Contracts       = 0;
                        RiskDollars     = 0;
                        ProfitDollars   = 0;
                        CanExecuteTrade = false;
                        break;
                    case TradeState.Armed:
                        StatusMessage = "ARMED — press Space to execute";
                        break;
                    case TradeState.Submitted:
                        StatusMessage = "Submitted — awaiting fill…";
                        break;
                    case TradeState.Active:
                        StatusMessage = "Active — ATM managing SL/TP";
                        break;
                    case TradeState.BreakEvenTriggered:
                        StatusMessage = "Break-even triggered";
                        break;
                    case TradeState.Closed:
                        StatusMessage = "Trade closed.";
                        break;
                    case TradeState.Cancelled:
                        StatusMessage = "Trade cancelled.";
                        break;
                }
            });
        }

        /// <summary>
        /// Cycles to next tier (UP key)
        /// </summary>
        public void CycleTierUp()
        {
            try { _controller.CycleTierUp(); }
            catch (Exception ex) { StatusMessage = $"Tier error: {ex.Message}"; }
        }

        /// <summary>
        /// Cycles to previous tier (DOWN key)
        /// </summary>
        public void CycleTierDown()
        {
            try { _controller.CycleTierDown(); }
            catch (Exception ex) { StatusMessage = $"Tier error: {ex.Message}"; }
        }

        public void Dispose()
        {
            _controller.PlanUpdated    -= OnPlanUpdated;
            _controller.TierChanged    -= OnTierChanged;
            _stateMachine.StateChanged -= OnStateChanged;
        }
    }
}
