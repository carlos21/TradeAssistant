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
using TradeAssistant.Domain;
using TradeAssistant.Infrastructure;
using TradeAssistant.UI;
using RiskMode = TradeAssistant.Domain.RiskMode;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class TradeAssistantIndicator : Indicator
    {
        // ── Layer objects ─────────────────────────────────────────────────────
        private TradeStateMachine           _stateMachine;
        private NinjaInstrumentInfoProvider _instrumentInfo;
        private NinjaAccountDataProvider    _accountData;
        private NinjaOrderAdapter           _orderAdapter;
        private TradePlannerController      _controller;
        private ExecutionCoordinator        _executionCoordinator;
        private TradePlannerViewModel       _viewModel;
        private TradePlannerView            _panel;

        // ── Chart state ───────────────────────────────────────────────────────
        private double    _stopPrice   = 0;
        private double    _tpPrice     = 0;
        private double    _entryPrice  = 0;
        private TradePlan _currentPlan = TradePlan.Empty;
        private ChartScale _cachedScale;
        private bool       _initialized = false;

        private enum DragMode { None, Sl, Tp }
        private DragMode _dragMode = DragMode.None;

        private const double HitTolerance = 12.0;
        private const float  RectWidth    = 120f;

        // ── SharpDX resources ─────────────────────────────────────────────────
        private SharpDX.Direct2D1.SolidColorBrush _slZoneBrush;
        private SharpDX.Direct2D1.SolidColorBrush _tpZoneBrush;
        private SharpDX.Direct2D1.SolidColorBrush _slLineBrush;
        private SharpDX.Direct2D1.SolidColorBrush _tpLineBrush;
        private SharpDX.Direct2D1.SolidColorBrush _entryLineBrush;
        private SharpDX.Direct2D1.SolidColorBrush _labelBrush;
        private SharpDX.Direct2D1.SolidColorBrush _slLabelBgBrush;
        private SharpDX.Direct2D1.SolidColorBrush _tpLabelBgBrush;
        private SharpDX.Direct2D1.SolidColorBrush _entryLabelBgBrush;
        private SharpDX.DirectWrite.TextFormat     _labelFormat;
        private SharpDX.DirectWrite.TextFormat     _labelFormatSmall;

        // ─────────────────────────────────────────────────────────────────────
        // NT8 lifecycle
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description  = "TradeAssistant — manual futures trade planner and executor";
                Name         = "TradeAssistant";
                Calculate    = Calculate.OnEachTick;
                IsOverlay    = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = false;

                RiskMode         = RiskMode.FixedAmount;
                RiskValue        = 240.0;
                RrRatio          = 4.0;
            }
            else if (State == State.DataLoaded)
            {
                InitializeLayers();
                SubscribeChartEvents();
                OpenPanel();
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

        // ─────────────────────────────────────────────────────────────────────
        // Layer wiring
        // ─────────────────────────────────────────────────────────────────────

        private void InitializeLayers()
        {
            try
            {
                Account account = ResolveAccount();
                if (account == null)
                {
                    ShowError("Account not found", "Could not resolve account from ChartTrader. Make sure ChartTrader is enabled and an account is selected.");
                    return;
                }

                _stateMachine   = new TradeStateMachine();
                _instrumentInfo = new NinjaInstrumentInfoProvider(this);
                _accountData    = new NinjaAccountDataProvider(account);
                _orderAdapter   = new NinjaOrderAdapter(this, account);

                _controller = new TradePlannerController(_instrumentInfo, _accountData);
                _controller.SetRiskMode(RiskMode);
                _controller.SetRiskValue(RiskValue);
                _controller.SetRrRatio(RrRatio);

                _executionCoordinator = new ExecutionCoordinator(_orderAdapter, _stateMachine);

                _controller.PlanUpdated    += OnPlanUpdated;
                _stateMachine.StateChanged += OnStateChanged;

                _executionCoordinator.StatusMessage += msg =>
                    Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.StatusMessage = msg; });
                _executionCoordinator.ExecutionError += msg =>
                    Dispatcher.InvokeAsync(() => { 
                        if (_viewModel != null) _viewModel.StatusMessage = $"Error: {msg}"; 
                        ShowError("Execution Error", msg);
                    });
            }
            catch (Exception ex)
            {
                ShowError("Initialization Error", $"Failed to initialize Trade Assistant: {ex.Message}\n\nStack trace: {ex.StackTrace}");
            }
        }

        private void OpenPanel()
        {
            if (_controller == null || _stateMachine == null)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _viewModel = new TradePlannerViewModel(_controller, _stateMachine);
                    _viewModel.RiskMode         = RiskMode;
                    _viewModel.RiskValue        = RiskValue;
                    _viewModel.RrRatio          = RrRatio;

                    _viewModel.ExecuteRequested      = OnExecuteRequested;
                    _viewModel.ToggleDirectionAction = () =>
                    {
                        try
                        {
                            _controller.SetDirection(_viewModel.Direction);
                            if (_entryPrice > 0)
                            {
                                if (_stopPrice > 0)
                                {
                                    double dist = Math.Abs(_entryPrice - _stopPrice);
                                    _stopPrice = _viewModel.Direction == TradeDirection.Long
                                        ? _entryPrice - dist
                                        : _entryPrice + dist;
                                    _stopPrice = SnapSlDistanceToMultipleOfFive(_stopPrice);
                                    _controller.SetStopPrice(_stopPrice);
                                }
                                else
                                {
                                    PlaceDefaultStop();
                                }
                            }
                            ForceRefresh();
                        }
                        catch (Exception ex)
                        {
                            ShowError("Direction Change Error", $"Failed to change direction: {ex.Message}");
                        }
                    };

                    _viewModel.ShowTradeBoxesChanged += (show) => ForceRefresh();

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
        // Auto-initialize SL/TP on first bar
        // ─────────────────────────────────────────────────────────────────────

        private void AutoInitialize()
        {
            try
            {
                if (_initialized || _entryPrice <= 0) return;
                if (State == State.Historical && CurrentBar < BarsArray[0].Count - 1) return;
                _initialized = true;

                PlaceDefaultStop();
                _stateMachine.TryTransitionTo(TradeState.Planning);
            }
            catch (Exception ex)
            {
                ShowError("Initialization Error", $"Failed to auto-initialize: {ex.Message}");
            }
        }

        private void PlaceDefaultStop()
        {
            try
            {
                TradeDirection dir = _viewModel?.Direction ?? TradeDirection.Long;
                double defaultDistance = 20.0;
                _stopPrice = dir == TradeDirection.Long
                    ? _entryPrice - defaultDistance
                    : _entryPrice + defaultDistance;
                _stopPrice = SnapSlDistanceToMultipleOfFive(_stopPrice);
                _controller.SetStopPrice(_stopPrice);
            }
            catch (Exception ex)
            {
                ShowError("Stop Error", $"Failed to place default stop: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Price tick
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnBarUpdate()
        {
            try
            {
                if (CurrentBar < 1) return;

                _entryPrice = Close[0];
                _controller.SetEntryPrice(_entryPrice);

                AutoInitialize();

                ForceRefresh();
            }
            catch (Exception ex)
            {
                // Only show error once to avoid spamming
                // ShowError("Bar Update Error", $"Error in OnBarUpdate: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SharpDX rendering
        // ─────────────────────────────────────────────────────────────────────

        public override void OnRenderTargetChanged()
        {
            DisposeBrushes();
            if (RenderTarget == null) return;

            _slZoneBrush      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.90f, 0.15f, 0.15f, 0.50f));
            _tpZoneBrush      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.10f, 0.75f, 0.25f, 0.50f));
            _slLineBrush      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.95f, 0.20f, 0.20f, 1.0f));
            _tpLineBrush      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.20f, 0.90f, 0.35f, 1.0f));
            _entryLineBrush   = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(1.0f, 0.85f, 0.10f, 1.0f));
            _slLabelBgBrush   = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.85f, 0.15f, 0.15f, 0.90f));
            _tpLabelBgBrush   = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.10f, 0.65f, 0.20f, 0.90f));
            _entryLabelBgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.60f, 0.55f, 0.05f, 0.90f));
            _labelBrush       = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, Color4.White);
            _labelFormat      = new SharpDX.DirectWrite.TextFormat(
                Core.Globals.DirectWriteFactory, "Segoe UI Semibold", 12.0f);
            _labelFormatSmall = new SharpDX.DirectWrite.TextFormat(
                Core.Globals.DirectWriteFactory, "Segoe UI", 10.0f);
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            _cachedScale = chartScale;

            if (RenderTarget == null || _stateMachine == null) return;
            if (_entryPrice <= 0) return;

            // ── Planning Mode: Show planning zones ──
            if (_stateMachine.Current != TradeState.Idle)
            {
                RenderPlanningZones(chartControl, chartScale);
            }
        }

        private void RenderPlanningZones(ChartControl chartControl, ChartScale chartScale)
        {
            float lastBarX  = chartControl.GetXByBarIndex(ChartBars, CurrentBar);
            float rectLeft  = lastBarX;
            float rectRight = lastBarX + RectWidth;

            float entryY = (float)chartScale.GetYByValue(_entryPrice);
            float slY    = _stopPrice > 0 ? (float)chartScale.GetYByValue(_stopPrice) : 0;
            float tpY    = _tpPrice   > 0 ? (float)chartScale.GetYByValue(_tpPrice)   : 0;

            // Check if trade boxes should be shown
            bool showBoxes = _viewModel?.ShowTradeBoxes ?? true;

            // ── SL zone ──
            if (showBoxes && _stopPrice > 0 && _slZoneBrush != null)
            {
                float top    = Math.Min(entryY, slY);
                float bottom = Math.Max(entryY, slY);
                float height = Math.Max(bottom - top, 1f);

                RenderTarget.FillRectangle(new RectangleF(rectLeft, top, RectWidth, height), _slZoneBrush);

                double slPts = Math.Abs(_entryPrice - _stopPrice);
                string slText = $"SL  -{slPts:F2} pts";
                float slLabelY = slY > entryY ? slY + 3 : slY - 20;
                DrawLabelWithBg(slText, rectLeft + 4, slLabelY, _slLabelBgBrush, _labelFormat);
            }

            // ── TP zone ──
            if (showBoxes && _tpPrice > 0 && _tpZoneBrush != null)
            {
                float top    = Math.Min(entryY, tpY);
                float bottom = Math.Max(entryY, tpY);
                float height = Math.Max(bottom - top, 1f);

                RenderTarget.FillRectangle(new RectangleF(rectLeft, top, RectWidth, height), _tpZoneBrush);

                double tpPts = Math.Abs(_tpPrice - _entryPrice);
                string tpText = $"TP  +{tpPts:F2} pts";
                float tpLabelY = tpY < entryY ? tpY - 20 : tpY + 3;
                DrawLabelWithBg(tpText, rectLeft + 4, tpLabelY, _tpLabelBgBrush, _labelFormat);
            }

            // ── Entry line ──
            if (showBoxes && _entryLineBrush != null)
            {
                RenderTarget.DrawLine(
                    new Vector2(rectLeft, entryY), new Vector2(rectRight, entryY), _entryLineBrush, 2.0f);
            }
        }

        private void DrawLabelWithBg(string text, float x, float y,
            SharpDX.Direct2D1.SolidColorBrush bgBrush, SharpDX.DirectWrite.TextFormat fmt)
        {
            if (fmt == null || _labelBrush == null || RenderTarget == null) return;
            var layout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, text, fmt, 300, 20);
            float w = layout.Metrics.Width;
            float h = layout.Metrics.Height;
            if (bgBrush != null)
                RenderTarget.FillRectangle(new RectangleF(x - 2, y - 1, w + 6, h + 2), bgBrush);
            RenderTarget.DrawTextLayout(new Vector2(x, y), layout, _labelBrush);
            layout.Dispose();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Mouse/keyboard
        // ─────────────────────────────────────────────────────────────────────

        private void SubscribeChartEvents()
        {
            if (ChartControl == null) return;
            ChartControl.MouseDown      += OnChartMouseDown;
            ChartControl.MouseMove      += OnChartMouseMove;
            ChartControl.MouseUp        += OnChartMouseUp;
            ChartControl.PreviewKeyDown += OnChartKeyDown;
        }

        private void UnsubscribeChartEvents()
        {
            if (ChartControl == null) return;
            ChartControl.MouseDown      -= OnChartMouseDown;
            ChartControl.MouseMove      -= OnChartMouseMove;
            ChartControl.MouseUp        -= OnChartMouseUp;
            ChartControl.PreviewKeyDown -= OnChartKeyDown;
        }

        private void OnChartMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                if (_cachedScale == null) return;
                if (_stateMachine == null) return;
                if (ChartControl == null) return;

                System.Windows.Point pos = e.GetPosition(ChartControl);

                if (_stateMachine.Current == TradeState.Planning)
                {
                    if (_stopPrice > 0 && IsNearPrice(_stopPrice, pos.Y))
                    {
                        _dragMode = DragMode.Sl;
                        Mouse.Capture(ChartControl);
                        e.Handled = true;
                        return;
                    }
                    if (_tpPrice > 0 && IsNearPrice(_tpPrice, pos.Y))
                    {
                        _dragMode = DragMode.Tp;
                        Mouse.Capture(ChartControl);
                        e.Handled = true;
                        return;
                    }
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
                if (ChartControl == null) return;
                
                System.Windows.Point pos = e.GetPosition(ChartControl);

                if (_dragMode == DragMode.None)
                {
                    if (_cachedScale != null && _stateMachine != null)
                    {
                        bool nearPlanningSl = _stateMachine.Current == TradeState.Planning && 
                                              _stopPrice > 0 && IsNearPrice(_stopPrice, pos.Y);
                        bool nearPlanningTp = _stateMachine.Current == TradeState.Planning && 
                                              _tpPrice > 0 && IsNearPrice(_tpPrice, pos.Y);

                        bool nearBorder = nearPlanningSl || nearPlanningTp;
                        ChartControl.Cursor = nearBorder ? Cursors.SizeNS : null;
                    }
                    return;
                }

                if (_cachedScale == null) return;
                if (_stateMachine == null) return;
                double mousePrice = _cachedScale.GetValueByY((float)pos.Y);

                if (_dragMode == DragMode.Sl)
                {
                    if (_stateMachine.Current == TradeState.Planning)
                        PlaceStop(mousePrice);
                }
                else if (_dragMode == DragMode.Tp)
                {
                    if (_stateMachine.Current == TradeState.Planning)
                    {
                        double newTp     = SnapToTick(mousePrice);
                        double stopDist  = Math.Abs(_entryPrice - _stopPrice);
                        double tpDist    = Math.Abs(newTp - _entryPrice);
                        double impliedRr = stopDist > 0 ? Math.Round((tpDist / stopDist) * 4.0) / 4.0 : 2.0;
                        impliedRr        = Math.Max(0.25, impliedRr);

                        _controller.SetRrRatio(impliedRr);
                        Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.RrRatio = impliedRr; });
                    }
                    ForceRefresh();
                }
            }
            catch (Exception ex)
            {
                // Don't show popup on every mouse move - just log to debug
                // ShowError("Mouse Error", $"Error on mouse move: {ex.Message}");
            }
        }

        private void OnChartMouseUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_dragMode == DragMode.None) return;
                if (ChartControl == null) return;

                System.Windows.Point pos = e.GetPosition(ChartControl);
                Mouse.Capture(null);

                if (_cachedScale != null && _stateMachine != null)
                {
                    double mousePrice = _cachedScale.GetValueByY((float)pos.Y);

                    if (_dragMode == DragMode.Sl)
                    {
                        double newPrice = SnapSlDistanceToMultipleOfFive(mousePrice);
                        if (_stateMachine.Current == TradeState.Planning)
                            PlaceStop(newPrice);
                    }
                    else if (_dragMode == DragMode.Tp)
                    {
                        double newPrice = SnapToTick(mousePrice);
                        if (_stateMachine.Current == TradeState.Planning)
                        {
                            double stopDist = Math.Abs(_entryPrice - _stopPrice);
                            double tpDist = Math.Abs(newPrice - _entryPrice);
                            double impliedRr = stopDist > 0 ? Math.Round((tpDist / stopDist) * 4.0) / 4.0 : 2.0;
                            impliedRr = Math.Max(0.25, impliedRr);
                            _controller.SetRrRatio(impliedRr);
                            Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.RrRatio = impliedRr; });
                        }
                    }
                }

                _dragMode = DragMode.None;
                ForceRefresh();
            }
            catch (Exception ex)
            {
                ShowError("Mouse Error", $"Error on mouse up: {ex.Message}");
            }
        }

        private void OnChartKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
            if (_stateMachine == null) return;

            switch (e.Key)
            {
                case Key.Space:
                    e.Handled = true;
                    OnSpacePressed();
                    break;
                    
                case Key.Escape:
                    e.Handled = true;
                    _stateMachine?.Reset();
                    ResetChartState();
                    break;
            }
        }

        private void OnSpacePressed()
        {
            try
            {
                if (_stateMachine.Current != TradeState.Planning) return;

                if (!_currentPlan.IsValid)
                {
                    ShowError("Invalid Trade Plan", _currentPlan.ValidationError ?? "Trade plan is not valid.");
                    return;
                }

                _stateMachine.TransitionTo(TradeState.Armed);
                
                // Use ATM strategy execution if available
                string atmStrategy = _controller?.CurrentAtmStrategyName;
                if (!string.IsNullOrEmpty(atmStrategy))
                {
                    _executionCoordinator.ExecuteWithAtm(_currentPlan, atmStrategy);
                }
                else
                {
                    _executionCoordinator.Execute(_currentPlan);
                }
            }
            catch (Exception ex)
            {
                ShowError("Execution Error", $"Failed to execute trade: {ex.Message}\n\nStack trace: {ex.StackTrace}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private void PlaceStop(double price)
        {
            try
            {
                _stopPrice = SnapSlDistanceToMultipleOfFive(price);
                _controller.SetStopPrice(_stopPrice);
                ForceRefresh();
            }
            catch (Exception ex)
            {
                ShowError("Stop Placement Error", $"Failed to place stop: {ex.Message}");
            }
        }

        private double SnapToTick(double price)
        {
            double tick = _instrumentInfo?.TickSize ?? 0.25;
            return tick > 0 ? Math.Round(price / tick) * tick : price;
        }

        private double SnapSlDistanceToMultipleOfFive(double desiredStopPrice)
        {
            double distance = Math.Abs(_entryPrice - desiredStopPrice);
            double snappedDistance = Math.Round(distance / 5.0) * 5.0;
            bool isBelow = desiredStopPrice < _entryPrice;
            return isBelow ? _entryPrice - snappedDistance : _entryPrice + snappedDistance;
        }

        private bool IsNearPrice(double price, double pixelY)
        {
            if (_cachedScale == null || price <= 0) return false;
            float pricePixelY = (float)_cachedScale.GetYByValue(price);
            return Math.Abs(pricePixelY - pixelY) <= HitTolerance;
        }

        private void OnPlanUpdated(TradePlan plan)
        {
            _currentPlan = plan;
            if (plan.IsValid)
                _stopPrice = plan.StopPrice;
            _tpPrice = plan.TpPrice;
            ForceRefresh();
        }

        private void OnStateChanged(TradeState previous, TradeState next)
        {
            Dispatcher.InvokeAsync(() => ForceRefresh());
        }

        private void OnExecuteRequested()
        {
            try
            {
                if (_stateMachine.Current != TradeState.Planning) return;
                
                if (!_currentPlan.IsValid)
                {
                    ShowError("Invalid Trade Plan", _currentPlan.ValidationError ?? "Trade plan is not valid.");
                    return;
                }
                
                _stateMachine.TryTransitionTo(TradeState.Armed);
                
                // Use ATM strategy execution if available
                string atmStrategy = _controller?.CurrentAtmStrategyName;
                if (!string.IsNullOrEmpty(atmStrategy))
                {
                    _executionCoordinator.ExecuteWithAtm(_currentPlan, atmStrategy);
                }
                else
                {
                    _executionCoordinator.Execute(_currentPlan);
                }
            }
            catch (Exception ex)
            {
                ShowError("Execution Error", $"Failed to execute trade: {ex.Message}\n\nStack trace: {ex.StackTrace}");
            }
        }

        private void ResetChartState()
        {
            _stopPrice   = 0;
            _tpPrice     = 0;
            _currentPlan = TradePlan.Empty;
            _initialized = false;
            ForceRefresh();
        }

        private void DisposeBrushes()
        {
            _slZoneBrush?.Dispose();       _slZoneBrush       = null;
            _tpZoneBrush?.Dispose();       _tpZoneBrush       = null;
            _slLineBrush?.Dispose();       _slLineBrush       = null;
            _tpLineBrush?.Dispose();       _tpLineBrush       = null;
            _entryLineBrush?.Dispose();    _entryLineBrush    = null;
            _slLabelBgBrush?.Dispose();    _slLabelBgBrush    = null;
            _tpLabelBgBrush?.Dispose();    _tpLabelBgBrush    = null;
            _entryLabelBgBrush?.Dispose(); _entryLabelBgBrush = null;
            _labelBrush?.Dispose();        _labelBrush        = null;
            _labelFormat?.Dispose();       _labelFormat       = null;
            _labelFormatSmall?.Dispose();  _labelFormatSmall  = null;
        }

        private Account ResolveAccount()
        {
            return ChartControl?.OwnerChart?.ChartTrader?.Account;
        }

        /// <summary>
        /// Shows an error message to the user via MessageBox and logs to status.
        /// </summary>
        private void ShowError(string title, string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // Update status message if available
                if (_viewModel != null)
                    _viewModel.StatusMessage = $"ERROR: {message}";
                
                // Show popup
                try
                {
                    MessageBox.Show(message, $"Trade Assistant - {title}", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                    // If MessageBox fails, at least we tried to update the status
                }
            });
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

                _executionCoordinator?.Dispose();
                _orderAdapter?.Dispose();

                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        _panel?.Detach();
                        _viewModel?.Dispose();
                    }
                    catch { /* Ignore cleanup errors */ }
                });
            }
            catch (Exception ex)
            {
                // Silently log cleanup errors - don't show popup during cleanup
                // ShowError("Cleanup Error", $"Error during cleanup: {ex.Message}");
            }
        }
    }
}
