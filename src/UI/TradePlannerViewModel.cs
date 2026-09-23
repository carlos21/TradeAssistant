using System;
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

        // Wired by the indicator (account refresh + execution / break-even)
        public Action ExecuteRequested { get; set; }
        public Func<BreakEvenResult> ManualBreakEvenRequested { get; set; }

        /// <summary>Wired by the indicator to a MessageBox popup; receives (title, message).</summary>
        public Action<string, string> ErrorDisplay { get; set; }

        // ── Input properties ─────────────────────────────────────────────────

        private RiskMode _riskMode = RiskMode.FixedAmount;
        public RiskMode RiskMode
        {
            get => _riskMode;
            set
            {
                SetProperty(ref _riskMode, value);
                OnPropertyChanged(nameof(RiskValueLabel));
                // Re-assigning the current (sub-minimum) value routes the bump through the
                // RiskValue setter's clamp + popup before the mode switch validates.
                if (value == RiskMode.FixedAmount && _riskValue < TradeConfiguration.MinRiskDollars)
                    RiskValue = _riskValue;
                _controller.SetRiskMode(value);
            }
        }

        private double _riskValue = 120.0;
        public double RiskValue
        {
            get => _riskValue;
            set
            {
                if (_riskMode == RiskMode.FixedAmount && value < TradeConfiguration.MinRiskDollars)
                {
                    value = TradeConfiguration.MinRiskDollars;
                    ErrorDisplay?.Invoke("Invalid Risk",
                        $"Risk $ cannot be less than {TradeConfiguration.MinRiskDollars:F0} USD — set to {TradeConfiguration.MinRiskDollars:F0}.");
                }
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

        private double _breakEvenRr = 1.0;
        public double BreakEvenRr
        {
            get => _breakEvenRr;
            set
            {
                SetProperty(ref _breakEvenRr, value);
                _controller.SetBreakEvenRr(value);
                // Typing 0 into "BE at R:R" unchecks Auto BE; any positive value checks it.
                SetProperty(ref _autoBreakEven, value > 0, nameof(AutoBreakEven));
            }
        }

        private bool _autoBreakEven = true;
        public bool AutoBreakEven
        {
            get => _autoBreakEven;
            set
            {
                if (_autoBreakEven == value) return;
                SetProperty(ref _autoBreakEven, value);
                if (!value)
                {
                    BreakEvenRr = 0;
                }
                else if (BreakEvenRr == 0)
                {
                    BreakEvenRr = 1.0;
                }
            }
        }

        /// <summary>Human-readable feedback for a rejected manual break-even attempt.</summary>
        public static string DescribeBreakEvenResult(BreakEvenResult result)
        {
            switch (result)
            {
                case BreakEvenResult.AlreadyAtBreakEven: return "Break-even already applied";
                case BreakEvenResult.NoActiveTrade:      return "No active trade to move";
                case BreakEvenResult.NoWorkingStop:      return "Stop order not yet working — retry in a moment";
                default:                                 return null; // Moved — the monitor reports success itself
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

        private double _slPoints;
        public double SlPoints
        {
            get => _slPoints;
            private set => SetProperty(ref _slPoints, value);
        }

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

        private string _statusMessage = "Drag SL line or use ▲▼ buttons to adjust (steps of 5 pts)";
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

        private bool _canBreakEven;
        public bool CanBreakEven
        {
            get => _canBreakEven;
            set
            {
                SetProperty(ref _canBreakEven, value);
                (_breakEvenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        // ── Commands ─────────────────────────────────────────────────────────

        private readonly ICommand _executeCommand;
        public ICommand ExecuteCommand => _executeCommand;

        private readonly ICommand _toggleDirectionCommand;
        public ICommand ToggleDirectionCommand => _toggleDirectionCommand;

        private readonly ICommand _nudgeUpCommand;
        public ICommand NudgeUpCommand => _nudgeUpCommand;

        private readonly ICommand _nudgeDownCommand;
        public ICommand NudgeDownCommand => _nudgeDownCommand;

        private readonly ICommand _breakEvenCommand;
        public ICommand BreakEvenCommand => _breakEvenCommand;

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
                    catch (Exception ex)
                    {
                        StatusMessage = $"Execute error: {ex.Message}";
                        ErrorDisplay?.Invoke("Execute Error", ex.Message);
                    }
                },
                () => CanExecuteTrade && _stateMachine.Current == TradeState.Planning);

            _toggleDirectionCommand = new RelayCommand(() =>
            {
                try
                {
                    Direction = Direction == TradeDirection.Long
                        ? TradeDirection.Short
                        : TradeDirection.Long;
                }
                catch (Exception ex) { StatusMessage = $"Direction error: {ex.Message}"; }
            });

            _nudgeUpCommand = new RelayCommand(
                () => SafeNudge(+1),
                () => _stateMachine.Current == TradeState.Planning);

            _nudgeDownCommand = new RelayCommand(
                () => SafeNudge(-1),
                () => _stateMachine.Current == TradeState.Planning);

            _breakEvenCommand = new RelayCommand(
                () =>
                {
                    try
                    {
                        BreakEvenResult result = ManualBreakEvenRequested?.Invoke() ?? BreakEvenResult.NoWorkingStop;
                        string feedback = DescribeBreakEvenResult(result);
                        if (feedback != null)
                        {
                            StatusMessage = feedback;
                            ErrorDisplay?.Invoke("Break-even Unavailable", feedback);
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Break-even error: {ex.Message}";
                        ErrorDisplay?.Invoke("Break-even Error", ex.Message);
                    }
                },
                () => CanBreakEven);

            _controller.PlanUpdated    += OnPlanUpdated;
            _stateMachine.StateChanged += OnStateChanged;
        }

        /// <summary>Nudges the SL distance by ±1 step (5 pts). Used by the ▲▼ panel buttons.</summary>
        public void NudgeStop(int steps) => SafeNudge(steps);

        private void SafeNudge(int steps)
        {
            try { _controller.NudgeStop(steps); }
            catch (Exception ex)
            {
                StatusMessage = $"Nudge error: {ex.Message}";
                ErrorDisplay?.Invoke("Nudge Error", ex.Message);
            }
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
                    Contracts       = plan.Contracts;
                    RiskDollars     = plan.RiskDollars;
                    ProfitDollars   = plan.ProfitDollars;
                    StopPrice       = plan.StopPrice;
                    TpPrice         = plan.TpPrice;
                    SlPoints        = plan.SlDistancePoints;
                    CanExecuteTrade = true;
                    StatusMessage   = $"{Contracts} ctr | SL {SlPoints:F0}pt | Risk ${RiskDollars:F0} | TP ${ProfitDollars:F0}";
                }
                else
                {
                    Contracts       = 0;
                    RiskDollars     = 0;
                    ProfitDollars   = 0;
                    CanExecuteTrade = false;
                    StatusMessage   = plan.ValidationError;
                }
            });
        }

        private void OnStateChanged(TradeState previous, TradeState next)
        {
            RunOnUI(() =>
            {
                IsArmed      = next == TradeState.Armed;
                CanBreakEven = next == TradeState.Active;
                (_executeCommand   as RelayCommand)?.RaiseCanExecuteChanged();
                (_nudgeUpCommand   as RelayCommand)?.RaiseCanExecuteChanged();
                (_nudgeDownCommand as RelayCommand)?.RaiseCanExecuteChanged();

                switch (next)
                {
                    case TradeState.Idle:
                        StatusMessage   = "Drag SL line or use ▲▼ buttons to adjust (steps of 5 pts)";
                        Contracts       = 0;
                        RiskDollars     = 0;
                        ProfitDollars   = 0;
                        CanExecuteTrade = false;
                        break;
                    case TradeState.Planning:
                        break;
                    case TradeState.Armed:
                        StatusMessage = "ARMED — submitting entry…";
                        break;
                    case TradeState.Submitted:
                        StatusMessage = "Submitted — awaiting fill…";
                        break;
                    case TradeState.Active:
                        StatusMessage = "Active — bracket managing SL/TP";
                        break;
                    case TradeState.BreakEvenTriggered:
                        StatusMessage = "Break-even — stop at entry";
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

        public void Dispose()
        {
            _controller.PlanUpdated    -= OnPlanUpdated;
            _stateMachine.StateChanged -= OnStateChanged;
        }
    }
}
