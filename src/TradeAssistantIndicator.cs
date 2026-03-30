#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
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
        private BreakEvenService            _breakEvenService;
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

        private const double HitTolerance = 8.0;
        private const float  RectWidth    = 120f;   // width of SL/TP rectangles in pixels

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
                RrRatio          = 2.0;
                BreakEvenRr      = 0.0;
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

        [NinjaScriptProperty]
        [Display(Name = "Break-even R:R", Order = 4, GroupName = "Risk")]
        public double BreakEvenRr { get; set; }

        // ─────────────────────────────────────────────────────────────────────
        // Layer wiring
        // ─────────────────────────────────────────────────────────────────────

        private void InitializeLayers()
        {
            Account account = ChartControl.OwnerChart.ChartTrader.Account;

            _stateMachine   = new TradeStateMachine();
            _instrumentInfo = new NinjaInstrumentInfoProvider(this);
            _accountData    = new NinjaAccountDataProvider(account);
            _orderAdapter   = new NinjaOrderAdapter(this, account);

            _controller = new TradePlannerController(_instrumentInfo, _accountData);
            _controller.SetRiskMode(RiskMode);
            _controller.SetRiskValue(RiskValue);
            _controller.SetRrRatio(RrRatio);
            _controller.SetBreakEvenRr(BreakEvenRr);

            _breakEvenService     = new BreakEvenService(_orderAdapter, _stateMachine);
            _executionCoordinator = new ExecutionCoordinator(_orderAdapter, _stateMachine, _breakEvenService);

            _controller.PlanUpdated    += OnPlanUpdated;
            _stateMachine.StateChanged += OnStateChanged;

            _executionCoordinator.StatusMessage += msg =>
                Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.StatusMessage = msg; });
            _executionCoordinator.ExecutionError += msg =>
                Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.StatusMessage = $"Error: {msg}"; });
            _breakEvenService.StatusMessage += msg =>
                Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.StatusMessage = msg; });
        }

        private void OpenPanel()
        {
            Dispatcher.InvokeAsync(() =>
            {
                _viewModel = new TradePlannerViewModel(_controller, _stateMachine);
                _viewModel.RiskMode         = RiskMode;
                _viewModel.RiskValue        = RiskValue;
                _viewModel.RrRatio          = RrRatio;
                _viewModel.BreakEvenRr      = BreakEvenRr;

                _viewModel.ExecuteRequested      = OnExecuteRequested;
                _viewModel.ToggleDirectionAction = () =>
                {
                    _controller.SetDirection(_viewModel.Direction);
                    if (_entryPrice > 0)
                    {
                        if (_stopPrice > 0)
                        {
                            // Mirror the existing SL distance to the correct side
                            double dist = Math.Abs(_entryPrice - _stopPrice);
                            _stopPrice = _viewModel.Direction == TradeDirection.Long
                                ? SnapToTick(_entryPrice - dist)
                                : SnapToTick(_entryPrice + dist);
                            _controller.SetStopPrice(_stopPrice);
                        }
                        else
                        {
                            PlaceDefaultStop();
                        }
                    }
                    ForceRefresh();
                };

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
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Auto-initialize SL/TP on first bar
        // ─────────────────────────────────────────────────────────────────────

        private void AutoInitialize()
        {
            if (_initialized || _entryPrice <= 0) return;
            // Skip all historical bars except the last one so that the SL is placed
            // relative to the most recent price, not a price deep in history.
            if (State == State.Historical && CurrentBar < BarsArray[0].Count - 1) return;
            _initialized = true;

            PlaceDefaultStop();
            _stateMachine.TryTransitionTo(TradeState.Planning);
        }

        private void PlaceDefaultStop()
        {
            TradeDirection dir = _viewModel?.Direction ?? TradeDirection.Long;
            // 20 points on the correct side of entry for the current direction
            _stopPrice = dir == TradeDirection.Long
                ? SnapToTick(_entryPrice - 20.0)
                : SnapToTick(_entryPrice + 20.0);
            _controller.SetStopPrice(_stopPrice);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Price tick
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            _entryPrice = Close[0];
            _controller.SetEntryPrice(_entryPrice);

            AutoInitialize();

            TradeDirection dir = _viewModel?.Direction ?? TradeDirection.Long;
            _breakEvenService.OnPriceUpdate(_entryPrice, dir);

            ForceRefresh();
        }

        // ─────────────────────────────────────────────────────────────────────
        // SharpDX rendering
        // ─────────────────────────────────────────────────────────────────────

        public override void OnRenderTargetChanged()
        {
            DisposeBrushes();
            if (RenderTarget == null) return;

            // Zone fills — 50 % opacity like TradingView
            _slZoneBrush      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.90f, 0.15f, 0.15f, 0.50f));
            _tpZoneBrush      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.10f, 0.75f, 0.25f, 0.50f));
            // Lines
            _slLineBrush      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.95f, 0.20f, 0.20f, 1.0f));
            _tpLineBrush      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.20f, 0.90f, 0.35f, 1.0f));
            _entryLineBrush   = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(1.0f, 0.85f, 0.10f, 1.0f));
            // Label backgrounds
            _slLabelBgBrush   = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.85f, 0.15f, 0.15f, 0.90f));
            _tpLabelBgBrush   = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.10f, 0.65f, 0.20f, 0.90f));
            _entryLabelBgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.60f, 0.55f, 0.05f, 0.90f));
            // Text
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
            if (_stateMachine.Current == TradeState.Idle) return;
            if (_entryPrice <= 0) return;

            // Anchor rectangles at the last bar (left edge aligned to current bar)
            float lastBarX  = chartControl.GetXByBarIndex(ChartBars, CurrentBar);
            float rectLeft  = lastBarX;
            float rectRight = lastBarX + RectWidth;

            float entryY = (float)chartScale.GetYByValue(_entryPrice);
            float slY    = _stopPrice > 0 ? (float)chartScale.GetYByValue(_stopPrice) : 0;
            float tpY    = _tpPrice   > 0 ? (float)chartScale.GetYByValue(_tpPrice)   : 0;

            // ── SL zone (RED filled rectangle between entry and stop) ──
            if (_stopPrice > 0 && _slZoneBrush != null)
            {
                float top    = Math.Min(entryY, slY);
                float bottom = Math.Max(entryY, slY);
                float height = Math.Max(bottom - top, 1f);

                // Red filled zone (no border, no extending line — TradingView style)
                RenderTarget.FillRectangle(new RectangleF(rectLeft, top, RectWidth, height), _slZoneBrush);

                // SL label — show distance in points, not price
                double slPts = Math.Abs(_entryPrice - _stopPrice);
                string slText = $"SL  -{slPts:F2} pts";
                float slLabelY = slY > entryY ? slY + 3 : slY - 20;
                DrawLabelWithBg(slText, rectLeft + 4, slLabelY, _slLabelBgBrush, _labelFormat);
            }

            // ── TP zone (GREEN filled rectangle between entry and TP) ──
            if (_tpPrice > 0 && _tpZoneBrush != null)
            {
                float top    = Math.Min(entryY, tpY);
                float bottom = Math.Max(entryY, tpY);
                float height = Math.Max(bottom - top, 1f);

                // Green filled zone (no border, no extending line — TradingView style)
                RenderTarget.FillRectangle(new RectangleF(rectLeft, top, RectWidth, height), _tpZoneBrush);

                // TP label — show distance in points, not price
                double tpPts = Math.Abs(_tpPrice - _entryPrice);
                string tpText = $"TP  +{tpPts:F2} pts";
                float tpLabelY = tpY < entryY ? tpY - 20 : tpY + 3;
                DrawLabelWithBg(tpText, rectLeft + 4, tpLabelY, _tpLabelBgBrush, _labelFormat);
            }

            // ── Entry line (yellow, spans the rectangle width) ──
            if (_entryLineBrush != null)
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
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (_cachedScale == null) return;

            System.Windows.Point pos = e.GetPosition(ChartControl);

            // Only start drag if near the SL or TP line
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

        private void OnChartMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            System.Windows.Point pos = e.GetPosition(ChartControl);

            if (_dragMode == DragMode.None)
            {
                // Change cursor to resize arrow when hovering near a draggable border
                if (_cachedScale != null && _stateMachine?.Current == TradeState.Planning)
                {
                    bool nearBorder = (_stopPrice > 0 && IsNearPrice(_stopPrice, pos.Y))
                                   || (_tpPrice   > 0 && IsNearPrice(_tpPrice,   pos.Y));
                    ChartControl.Cursor = nearBorder ? Cursors.SizeNS : null;
                }
                return;
            }

            if (_cachedScale == null) return;
            double mousePrice = _cachedScale.GetValueByY((float)pos.Y);

            if (_dragMode == DragMode.Sl)
            {
                PlaceStop(mousePrice);
            }
            else if (_dragMode == DragMode.Tp)
            {
                double newTp     = SnapToTick(mousePrice);
                double stopDist  = Math.Abs(_entryPrice - _stopPrice);
                double tpDist    = Math.Abs(newTp - _entryPrice);
                double impliedRr = stopDist > 0 ? Math.Round((tpDist / stopDist) * 4.0) / 4.0 : 2.0;
                impliedRr        = Math.Max(0.25, impliedRr);

                _controller.SetRrRatio(impliedRr);
                Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.RrRatio = impliedRr; });
                ForceRefresh();
            }
        }

        private void OnChartMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode != DragMode.None)
            {
                Mouse.Capture(null);
                _dragMode = DragMode.None;
            }
        }

        private void OnChartKeyDown(object sender, KeyEventArgs e)
        {
            // Don't steal keystrokes from our panel's input fields
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

            if (e.Key == Key.Space)
            {
                e.Handled = true;
                if (_stateMachine.Current != TradeState.Planning) return;

                if (!_currentPlan.IsValid)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (_viewModel != null)
                            _viewModel.StatusMessage = _currentPlan.ValidationError;
                    });
                    return;
                }

                _stateMachine.TransitionTo(TradeState.Armed);
                _executionCoordinator.Execute(_currentPlan);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _stateMachine.Reset();
                ResetChartState();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private void PlaceStop(double price)
        {
            _stopPrice = SnapToTick(price);
            _controller.SetStopPrice(_stopPrice);
            ForceRefresh();
        }

        private double SnapToTick(double price)
        {
            double tick = _instrumentInfo?.TickSize ?? 0.25;
            return tick > 0 ? Math.Round(price / tick) * tick : price;
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
            {
                _stopPrice = plan.StopPrice;  // Sync SL from plan (maintains fixed tick distance)
            }
            // Always show TP box if we have a TP price, even for invalid plans
            // (e.g., insufficient risk budget but TP should still be visible)
            _tpPrice = plan.TpPrice;
            ForceRefresh();
        }

        private void OnStateChanged(TradeState previous, TradeState next)
        {
            Dispatcher.InvokeAsync(() => ForceRefresh());
        }

        private void OnExecuteRequested()
        {
            if (_stateMachine.Current != TradeState.Planning) return;
            if (!_currentPlan.IsValid) return;
            _stateMachine.TryTransitionTo(TradeState.Armed);
            _executionCoordinator.Execute(_currentPlan);
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

        private void Cleanup()
        {
            DisposeBrushes();

            _controller.PlanUpdated    -= OnPlanUpdated;
            _stateMachine.StateChanged -= OnStateChanged;

            _executionCoordinator?.Dispose();
            _orderAdapter?.Dispose();

            Dispatcher.InvokeAsync(() =>
            {
                _panel?.Detach();
                _viewModel?.Dispose();
            });
        }
    }
}
