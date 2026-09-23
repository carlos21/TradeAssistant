#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript.Indicators;
using SharpDX;
using TradeAssistant.Application;
using TradeAssistant.Application.Ports;
using TradeAssistant.Domain;
using TradeAssistant.Infrastructure;
using TradeAssistant.UI;
using RiskMode = TradeAssistant.Domain.RiskMode;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Thin composition root: wires the Domain/Application/Infrastructure/UI
    /// layers together and translates raw chart events into controller calls.
    /// All trading logic lives in the layers — none here.
    ///
    /// Hotkeys: B = break-even. SL distance adjusts via drag or the ▲/▼ panel
    /// buttons (±5 pts).
    /// </summary>
    public class TradeAssistantIndicator : Indicator
    {
        // ── Layer objects ─────────────────────────────────────────────────────
        private TradeStateMachine            _stateMachine;
        private NinjaInstrumentInfoProvider  _instrumentInfo;
        private IAccountDataProvider         _accountData;
        private NinjaOrderGateway            _orderGateway;
        private NinjaPriceFeed               _priceFeed;
        private TradePlannerController       _controller;
        private BracketExecutionCoordinator  _coordinator;
        private BreakEvenMonitor             _breakEvenMonitor;
        private ChartInteractionController   _interaction;
        private ChartScaleAxisConverter      _axis;
        private TradePlannerViewModel        _viewModel;
        private TradePlannerView             _panel;

        // Cached handlers for clean unsubscribe
        private Action<string> _statusHandler;
        private Action<string> _errorHandler;
        private Action<string> _beStatusHandler;
        private Action<string> _beErrorHandler;

        // ── Chart state (render cache only) ───────────────────────────────────
        private TradePlan _currentPlan = TradePlan.Empty;
        private double    _stopPrice;
        private double    _tpPrice;
        private float     _rectLeftX;
        private bool      _initialized;
        private bool      _accountResolved;
        private Account   _currentAccount;

        private const double HitTolerancePx = 12.0;
        private const float  RectWidth      = 120f;

        // ── SharpDX resources ─────────────────────────────────────────────────
        private SharpDX.Direct2D1.SolidColorBrush _slZoneBrush;
        private SharpDX.Direct2D1.SolidColorBrush _tpZoneBrush;
        private SharpDX.Direct2D1.SolidColorBrush _entryLineBrush;
        private SharpDX.Direct2D1.SolidColorBrush _labelBrush;
        private SharpDX.Direct2D1.SolidColorBrush _slLabelBgBrush;
        private SharpDX.Direct2D1.SolidColorBrush _tpLabelBgBrush;
        private SharpDX.DirectWrite.TextFormat    _labelFormat;

        // ─────────────────────────────────────────────────────────────────────
        // NT8 lifecycle
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description      = "TradeAssistant — manual futures trade planner with bracket execution";
                Name             = "TradeAssistant";
                Calculate        = Calculate.OnEachTick;
                IsOverlay        = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = false;

                RiskMode        = RiskMode.FixedAmount;
                RiskValue       = 120.0;
                RrRatio         = 4.0;
                BreakEvenRr     = 1.0;
                DefaultSlPoints = 20.0;
            }
            else if (State == State.DataLoaded)
            {
                _stateMachine   = new TradeStateMachine();
                _instrumentInfo = new NinjaInstrumentInfoProvider(this);
                _priceFeed      = new NinjaPriceFeed(this);
                _axis           = new ChartScaleAxisConverter();
                _interaction    = new ChartInteractionController(_axis, HitTolerancePx);

                SubscribeChartEvents();
                TryInitializeAccount();
            }
            else if (State == State.Terminated)
            {
                UnsubscribeChartEvents();
                Cleanup();
            }
        }

        // ── Parameters ──────────────────────────────────────────────────────

        [NinjaScriptProperty]
        [Display(Name = "Risk Mode", Order = 1, GroupName = "Risk")]
        public RiskMode RiskMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Risk Value", Order = 2, GroupName = "Risk")]
        public double RiskValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "R:R Ratio", Order = 3, GroupName = "Risk")]
        public double RrRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break-even at R:R (0 = off)", Order = 4, GroupName = "Risk")]
        public double BreakEvenRr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Default SL Points", Order = 5, GroupName = "Risk")]
        public double DefaultSlPoints { get; set; }

        // ─────────────────────────────────────────────────────────────────────
        // Layer wiring
        // ─────────────────────────────────────────────────────────────────────

        private void TryInitializeAccount()
        {
            try
            {
                if (_accountResolved) return;

                Account account = ResolveAccount();
                if (account == null) return; // retry on next tick

                BuildServices(account);
                _accountResolved = true;
                _currentAccount  = account;
                OpenPanel();
            }
            catch (Exception ex)
            {
                ShowError("Initialization Error", $"Failed to initialize Trade Assistant: {ex.Message}");
            }
        }

        private TradeConfiguration BuildConfig()
        {
            double riskValue = RiskMode == RiskMode.FixedAmount
                ? Math.Max(TradeConfiguration.MinRiskDollars, RiskValue)
                : RiskValue;
            return new TradeConfiguration(RiskMode, riskValue, RrRatio, BreakEvenRr,
                StopSnapper.DefaultStepPoints, DefaultSlPoints);
        }

        private void BuildServices(Account account)
        {
            _accountData  = new NinjaAccountDataProvider(account);
            _orderGateway = new NinjaOrderGateway(this, account);
            _controller   = new TradePlannerController(_instrumentInfo, _accountData, BuildConfig());
            _coordinator  = new BracketExecutionCoordinator(_orderGateway, _stateMachine, _instrumentInfo);
            _breakEvenMonitor = new BreakEvenMonitor(_priceFeed, _orderGateway, _stateMachine);

            _controller.PlanUpdated      += OnPlanUpdated;
            _stateMachine.StateChanged   += OnStateChanged;
            _coordinator.BracketPlaced   += OnBracketPlaced;

            _statusHandler = msg =>
                Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.StatusMessage = msg; });
            _errorHandler = msg =>
                Dispatcher.InvokeAsync(() =>
                {
                    if (_viewModel != null) _viewModel.StatusMessage = $"Error: {msg}";
                    ShowError("Execution Error", msg);
                });
            _coordinator.StatusMessage  += _statusHandler;
            _coordinator.ExecutionError += _errorHandler;

            _beStatusHandler = msg =>
                Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.StatusMessage = msg; });
            _beErrorHandler = msg =>
                Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.StatusMessage = $"Error: {msg}"; });
            _breakEvenMonitor.StatusMessage += _beStatusHandler;
            _breakEvenMonitor.Error         += _beErrorHandler;
        }

        private void OpenPanel()
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _viewModel = new TradePlannerViewModel(_controller, _stateMachine);
                    _viewModel.RiskMode    = RiskMode;
                    _viewModel.RiskValue   = RiskValue;
                    _viewModel.RrRatio     = RrRatio;
                    _viewModel.BreakEvenRr = BreakEvenRr;

                    _viewModel.ExecuteRequested         = OnExecuteRequested;
                    _viewModel.ManualBreakEvenRequested = () => _breakEvenMonitor.MoveToBreakEven();
                    _viewModel.ErrorDisplay             = ShowError;
                    _viewModel.ShowTradeBoxesChanged   += show => ForceRefresh();

                    _panel = new TradePlannerView(_viewModel);

                    if (ChartControl != null)
                    {
                        var chartGrid = ChartControl.Parent as System.Windows.Controls.Grid;
                        if (chartGrid != null)
                        {
                            System.Windows.Controls.Grid.SetRowSpan(_panel,
                                chartGrid.RowDefinitions.Count > 0 ? chartGrid.RowDefinitions.Count : 1);
                            System.Windows.Controls.Grid.SetColumnSpan(_panel,
                                chartGrid.ColumnDefinitions.Count > 0 ? chartGrid.ColumnDefinitions.Count : 1);
                            System.Windows.Controls.Panel.SetZIndex(_panel, 100);
                            chartGrid.Children.Add(_panel);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShowError("Panel Error", $"Failed to open Trade Assistant panel: {ex.Message}");
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Price tick
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnBarUpdate()
        {
            try
            {
                if (CurrentBar < 1) return;

                if (!_accountResolved)
                {
                    TryInitializeAccount();
                    if (!_accountResolved) return;
                }

                _controller.SetEntryPrice(Close[0]);

                if (!_initialized)
                {
                    if (State == State.Historical && CurrentBar < BarsArray[0].Count - 1) return;
                    _initialized = true;
                    _controller.PlaceDefaultStop();
                    _stateMachine.TryTransitionTo(TradeState.Planning);
                }

                UpdateInteractionSnapshot();
                ForceRefresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TradeAssistant] OnBarUpdate error: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SharpDX rendering
        // ─────────────────────────────────────────────────────────────────────

        public override void OnRenderTargetChanged()
        {
            DisposeBrushes();
            if (RenderTarget == null) return;

            _slZoneBrush     = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.90f, 0.15f, 0.15f, 0.50f));
            _tpZoneBrush     = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.10f, 0.75f, 0.25f, 0.50f));
            _entryLineBrush  = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(1.0f, 0.85f, 0.10f, 1.0f));
            _slLabelBgBrush  = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.85f, 0.15f, 0.15f, 0.90f));
            _tpLabelBgBrush  = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.10f, 0.65f, 0.20f, 0.90f));
            _labelBrush      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, Color4.White);
            _labelFormat     = new SharpDX.DirectWrite.TextFormat(
                Core.Globals.DirectWriteFactory, "Segoe UI Semibold", 12.0f);
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            _axis.Scale = chartScale;

            if (RenderTarget == null || _stateMachine == null) return;

            double entryPrice = _controller != null ? _controller.EntryPrice : 0;
            if (entryPrice <= 0) return;

            float lastBarX = chartControl.GetXByBarIndex(ChartBars, CurrentBar);
            _rectLeftX = lastBarX;
            float rectRight = lastBarX + RectWidth;

            UpdateInteractionSnapshot();

            if (_stateMachine.Current == TradeState.Idle) return;

            bool showBoxes = _viewModel?.ShowTradeBoxes ?? true;
            if (!showBoxes) return;

            float entryY = (float)chartScale.GetYByValue(entryPrice);
            float slY    = _stopPrice > 0 ? (float)chartScale.GetYByValue(_stopPrice) : 0;
            float tpY    = _tpPrice   > 0 ? (float)chartScale.GetYByValue(_tpPrice)   : 0;

            // ── SL zone (draggable) ──
            if (_stopPrice > 0 && _slZoneBrush != null)
            {
                float top    = Math.Min(entryY, slY);
                float height = Math.Max(Math.Max(entryY, slY) - top, 1f);
                RenderTarget.FillRectangle(new RectangleF(lastBarX, top, RectWidth, height), _slZoneBrush);

                double slPts = Math.Abs(entryPrice - _stopPrice);
                float slLabelY = slY > entryY ? slY + 3 : slY - 20;
                DrawLabelWithBg($"SL  -{slPts:F0} pts", lastBarX + 4, slLabelY, _slLabelBgBrush);
            }

            // ── TP zone (derived — not interactive) ──
            if (_tpPrice > 0 && _tpZoneBrush != null)
            {
                float top    = Math.Min(entryY, tpY);
                float height = Math.Max(Math.Max(entryY, tpY) - top, 1f);
                RenderTarget.FillRectangle(new RectangleF(lastBarX, top, RectWidth, height), _tpZoneBrush);

                double tpPts = Math.Abs(_tpPrice - entryPrice);
                float tpLabelY = tpY < entryY ? tpY - 20 : tpY + 3;
                DrawLabelWithBg($"TP  +{tpPts:F0} pts", lastBarX + 4, tpLabelY, _tpLabelBgBrush);
            }

            // ── Entry line ──
            if (_entryLineBrush != null)
            {
                RenderTarget.DrawLine(
                    new Vector2(lastBarX, entryY), new Vector2(rectRight, entryY), _entryLineBrush, 2.0f);
            }
        }

        private void DrawLabelWithBg(string text, float x, float y,
            SharpDX.Direct2D1.SolidColorBrush bgBrush)
        {
            if (_labelFormat == null || _labelBrush == null || RenderTarget == null) return;
            var layout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, text, _labelFormat, 300, 20);
            float w = layout.Metrics.Width;
            float h = layout.Metrics.Height;
            if (bgBrush != null)
                RenderTarget.FillRectangle(new RectangleF(x - 2, y - 1, w + 6, h + 2), bgBrush);
            RenderTarget.DrawTextLayout(new Vector2(x, y), layout, _labelBrush);
            layout.Dispose();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Mouse / keyboard → ChartInteractionController
        // ─────────────────────────────────────────────────────────────────────

        private void SubscribeChartEvents()
        {
            if (ChartControl == null) return;
            ChartControl.PreviewMouseDown  += OnChartMouseDown;
            ChartControl.PreviewMouseMove  += OnChartMouseMove;
            ChartControl.PreviewMouseUp    += OnChartMouseUp;
            ChartControl.LostMouseCapture  += OnChartLostMouseCapture;
            ChartControl.PreviewKeyDown    += OnChartKeyDown;
        }

        private void UnsubscribeChartEvents()
        {
            if (ChartControl == null) return;
            ChartControl.PreviewMouseDown  -= OnChartMouseDown;
            ChartControl.PreviewMouseMove  -= OnChartMouseMove;
            ChartControl.PreviewMouseUp    -= OnChartMouseUp;
            ChartControl.LostMouseCapture  -= OnChartLostMouseCapture;
            ChartControl.PreviewKeyDown    -= OnChartKeyDown;
        }

        private void UpdateInteractionSnapshot()
        {
            if (_interaction == null || _controller == null) return;
            _interaction.UpdateSnapshot(
                _controller.EntryPrice, _stopPrice, _rectLeftX, _rectLeftX + RectWidth);
        }

        private void OnChartMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                if (_stateMachine == null || _stateMachine.Current != TradeState.Planning) return;
                if (ChartControl == null) return;

                System.Windows.Point pos = e.GetPosition(ChartControl);
                if (_interaction.TryBeginDrag(pos.X, pos.Y))
                {
                    Mouse.Capture(ChartControl);
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                ShowError("Mouse Error", $"Error on mouse down: {ex.Message}");
            }
        }

        private void OnChartMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (ChartControl == null || _interaction == null) return;
                System.Windows.Point pos = e.GetPosition(ChartControl);

                if (_interaction.TryDragTo(pos.Y, out double rawStopPrice))
                {
                    _controller.SetStopFromPrice(rawStopPrice);
                    UpdateInteractionSnapshot();
                    e.Handled = true;
                    ForceRefresh();
                    return;
                }

                // Hover cursor feedback
                bool nearSl = _stateMachine != null &&
                              _stateMachine.Current == TradeState.Planning &&
                              _interaction.IsOverStopLine(pos.X, pos.Y);
                ChartControl.Cursor = nearSl ? Cursors.SizeNS : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TradeAssistant] MouseMove error: {ex}");
            }
        }

        private void OnChartMouseUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_interaction == null || !_interaction.IsDragging) return;
                _interaction.EndDrag();
                Mouse.Capture(null);
                if (ChartControl != null) ChartControl.Cursor = null;
                e.Handled = true;
                ForceRefresh();
            }
            catch (Exception ex)
            {
                ShowError("Mouse Error", $"Error on mouse up: {ex.Message}");
            }
        }

        /// <summary>
        /// Capture can be stolen by the chart or ChartTrader mid-drag. Never
        /// leave the interaction controller stuck — always reset.
        /// </summary>
        private void OnChartLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _interaction?.CancelDrag();
            if (ChartControl != null) ChartControl.Cursor = null;
        }

        private void OnChartKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
            if (_stateMachine == null) return;

            switch (e.Key)
            {
                case Key.B:
                    e.Handled = true;
                    if (_breakEvenMonitor != null)
                    {
                        BreakEvenResult result = _breakEvenMonitor.MoveToBreakEven();
                        string feedback = TradePlannerViewModel.DescribeBreakEvenResult(result);
                        if (feedback != null)
                        {
                            ShowError("Break-even Unavailable", feedback);
                            if (_viewModel != null)
                                _viewModel.StatusMessage = feedback;
                        }
                    }
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Execution & break-even
        // ─────────────────────────────────────────────────────────────────────

        private void OnExecuteRequested()
        {
            try
            {
                EnsureAccountCurrent();

                if (_stateMachine.Current != TradeState.Planning)
                {
                    ShowError("Execute Unavailable",
                        "Execute is only available when a trade plan is armed.");
                    return;
                }

                if (!_currentPlan.IsValid)
                {
                    ShowError("Invalid Trade Plan",
                        _currentPlan.ValidationError ?? "Trade plan is not valid.");
                    return;
                }

                _coordinator.Execute(_currentPlan);
            }
            catch (Exception ex)
            {
                ShowError("Execution Error", $"Failed to execute trade: {ex.Message}");
            }
        }

        private void OnBracketPlaced(BracketInfo bracket)
        {
            _breakEvenMonitor.Activate(
                bracket.FillPrice, bracket.StopPrice,
                _controller.Config.BreakEvenRr, bracket.Direction);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Layer event handlers
        // ─────────────────────────────────────────────────────────────────────

        private void OnPlanUpdated(TradePlan plan)
        {
            _currentPlan = plan;
            if (plan.IsValid)
            {
                _stopPrice = plan.StopPrice;
                _tpPrice   = plan.TpPrice;
            }
            UpdateInteractionSnapshot();
            ForceRefresh();
        }

        private void OnStateChanged(TradeState previous, TradeState next)
        {
            if (next == TradeState.Idle)
            {
                _breakEvenMonitor?.Deactivate();

                // Trade finished (closed or cancelled) → return to planning
                // automatically, keeping the last SL distance (the entry keeps
                // following the live price).
                if ((previous == TradeState.Closed || previous == TradeState.Cancelled) &&
                    _controller != null && _stateMachine.Current == TradeState.Idle)
                {
                    if (!_controller.HasStop) _controller.PlaceDefaultStop();
                    _controller.Recalculate();
                    _stateMachine.TryTransitionTo(TradeState.Planning);
                }
            }

            Dispatcher.InvokeAsync(() => ForceRefresh());
        }

        private void EnsureAccountCurrent()
        {
            Account account = ResolveAccount();
            if (account == null) return;
            if (_currentAccount != null && _currentAccount.Name == account.Name) return;

            _currentAccount = account;

            _accountData = new NinjaAccountDataProvider(account);
            _controller?.ReplaceAccountDataProvider(_accountData);
            _controller?.Recalculate();

            var oldGateway = _orderGateway;
            _orderGateway  = new NinjaOrderGateway(this, account);
            _coordinator?.ReplaceOrderGateway(_orderGateway);
            _breakEvenMonitor?.ReplaceOrderGateway(_orderGateway);
            oldGateway?.Dispose();

            Dispatcher.InvokeAsync(() =>
            {
                if (_viewModel != null)
                    _viewModel.StatusMessage = $"Account changed to {account.Name}";
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private Account ResolveAccount()
        {
            return ChartControl?.OwnerChart?.ChartTrader?.Account;
        }

        private void ShowError(string title, string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_viewModel != null)
                    _viewModel.StatusMessage = $"ERROR: {message}";

                try
                {
                    MessageBox.Show(message, $"Trade Assistant - {title}",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                    // Status message already updated — popup is best-effort.
                }
            });
        }

        private void DisposeBrushes()
        {
            _slZoneBrush?.Dispose();     _slZoneBrush     = null;
            _tpZoneBrush?.Dispose();     _tpZoneBrush     = null;
            _entryLineBrush?.Dispose();  _entryLineBrush  = null;
            _slLabelBgBrush?.Dispose();  _slLabelBgBrush  = null;
            _tpLabelBgBrush?.Dispose();  _tpLabelBgBrush  = null;
            _labelBrush?.Dispose();      _labelBrush      = null;
            _labelFormat?.Dispose();     _labelFormat     = null;
        }

        private void Cleanup()
        {
            try
            {
                DisposeBrushes();

                if (_controller != null)
                    _controller.PlanUpdated    -= OnPlanUpdated;
                if (_stateMachine != null)
                    _stateMachine.StateChanged -= OnStateChanged;

                if (_coordinator != null)
                {
                    _coordinator.BracketPlaced -= OnBracketPlaced;
                    if (_statusHandler != null) _coordinator.StatusMessage  -= _statusHandler;
                    if (_errorHandler  != null) _coordinator.ExecutionError -= _errorHandler;
                }

                if (_breakEvenMonitor != null)
                {
                    if (_beStatusHandler != null) _breakEvenMonitor.StatusMessage -= _beStatusHandler;
                    if (_beErrorHandler  != null) _breakEvenMonitor.Error         -= _beErrorHandler;
                }

                _coordinator?.Dispose();
                _breakEvenMonitor?.Dispose();
                _priceFeed?.Dispose();
                _orderGateway?.Dispose();

                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        _panel?.Detach();
                        _viewModel?.Dispose();
                    }
                    catch { /* ignore cleanup errors */ }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TradeAssistant] Cleanup error: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Adapter: ChartScale → IPriceAxisConverter
        // ─────────────────────────────────────────────────────────────────────

        private sealed class ChartScaleAxisConverter : IPriceAxisConverter
        {
            public ChartScale Scale { get; set; }

            public double PriceFromY(double y)
            {
                return Scale != null ? Scale.GetValueByY((float)y) : 0;
            }

            public double YFromPrice(double price)
            {
                return Scale != null ? Scale.GetYByValue(price) : 0;
            }
        }
    }
}
