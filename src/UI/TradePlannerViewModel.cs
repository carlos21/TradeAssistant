using System;
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

        // ── Input properties ─────────────────────────────────────────────────

        private RiskMode _riskMode = RiskMode.Percentage;
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

        private double _riskValue = 1.0;
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

        private double _rrRatio = 2.0;
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

        private string _statusMessage = "Drag SL/TP to adjust";
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

        // ── Constructor ──────────────────────────────────────────────────────

        public TradePlannerViewModel(TradePlannerController controller, TradeStateMachine stateMachine)
        {
            _controller   = controller   ?? throw new ArgumentNullException(nameof(controller));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _dispatcher   = Dispatcher.CurrentDispatcher;

            _executeCommand = new RelayCommand(
                () => ExecuteRequested?.Invoke(),
                () => CanExecuteTrade && _stateMachine.Current == TradeState.Planning);

            _toggleDirectionCommand = new RelayCommand(() =>
            {
                Direction = Direction == TradeDirection.Long
                    ? TradeDirection.Short
                    : TradeDirection.Long;
                ToggleDirectionAction?.Invoke();
            });

            _controller.PlanUpdated += OnPlanUpdated;
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
                    StatusMessage = $"{plan.Contracts} contract(s) | Risk ${plan.RiskDollars:F0} | Profit ${plan.ProfitDollars:F0}";
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

        private void OnStateChanged(TradeState previous, TradeState next)
        {
            RunOnUI(() =>
            {
                IsArmed = next == TradeState.Armed;
                (_executeCommand as RelayCommand)?.RaiseCanExecuteChanged();

                switch (next)
                {
                    case TradeState.Idle:
                        StatusMessage   = "Drag SL/TP to adjust";
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
                        StatusMessage = "Active — monitoring for break-even";
                        break;
                    case TradeState.BreakEvenTriggered:
                        StatusMessage = "Break-even triggered — stop at entry";
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
