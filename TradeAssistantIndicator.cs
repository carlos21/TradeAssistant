#region Using declarations
using System;
using System.Windows.Input;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript.Indicators;
using SharpDX;
using TradeAssistant.Application;
using TradeAssistant.Domain;
using TradeAssistant.Infrastructure;
using TradeAssistant.UI;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// TradeAssistant — NinjaTrader 8 Indicator.
    ///
    /// Wires all four layers and owns the chart surface:
    ///   - Floating WPF panel (TradePlannerView)
    ///   - SharpDX rendering of SL/TP zones and labels (OnRender)
    ///   - Mouse events for SL click-to-place and drag (via ChartControl events)
    ///   - Keyboard spacebar / Esc (via ChartControl.PreviewKeyDown)
    ///   - Price-tick forwarding to BreakEvenService
    /// </summary>
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
        private ChartScale _cachedScale;   // cached in OnRender for mouse handlers

        private enum DragMode { None, Sl, Tp }
        private DragMode _dragMode = DragMode.None;

        private const double HitTolerance = 6.0; // pixels

        // ── SharpDX resources ─────────────────────────────────────────────────
        private SharpDX.Direct2D1.SolidColorBrush _slZoneBrush;
        private SharpDX.Direct2D1.SolidColorBrush _tpZoneBrush;
        private SharpDX.Direct2D1.SolidColorBrush _slLineBrush;
        private SharpDX.Direct2D1.SolidColorBrush _tpLineBrush;
        private SharpDX.Direct2D1.SolidColorBrush _entryLineBrush;
        private SharpDX.Direct2D1.SolidColorBrush _labelBrush;
        private SharpDX.DirectWrite.TextFormat     _labelFormat;

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

                RiskPercent      = 1.0;
                FixedRiskDollars = 0;
                RrRatio          = 2.0;
                BreakEvenRr      = 1.0;
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

        // ── Parameters exposed in the NT8 indicator properties panel ─────────

        [NinjaScriptProperty]
        [Display(Name = "Risk %", Order = 1, GroupName = "Risk")]
        public double RiskPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fixed Risk $", Order = 2, GroupName = "Risk")]
        public double FixedRiskDollars { get; set; }

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
            _stateMachine   = new TradeStateMachine();
            _instrumentInfo = new NinjaInstrumentInfoProvider(this);
            _accountData    = new NinjaAccountDataProvider(this);
            _orderAdapter   = new NinjaOrderAdapter(this);

            _controller = new TradePlannerController(_instrumentInfo, _accountData);
            _controller.SetRiskPercent(RiskPercent);
            _controller.SetFixedRiskDollars(FixedRiskDollars);
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
                _viewModel.RiskPercent      = RiskPercent;
                _viewModel.FixedRiskDollars = FixedRiskDollars;
                _viewModel.RrRatio          = RrRatio;
                _viewModel.BreakEvenRr      = BreakEvenRr;

                _viewModel.ExecuteRequested      = OnExecuteRequested;
                _viewModel.ToggleDirectionAction = () =>
                {
                    _controller.SetDirection(_viewModel.Direction);
                    ForceRefresh();
                };

                _panel = new TradePlannerView(_viewModel);
                _panel.Closed += (_, __) => _panel = null;
                _panel.Show();
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Price tick (called every tick by NT8)
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            _entryPrice = Close[0];
            _controller.SetEntryPrice(_entryPrice);

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

            _slZoneBrush    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.80f, 0.10f, 0.10f, 0.25f));
            _tpZoneBrush    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.10f, 0.70f, 0.20f, 0.25f));
            _slLineBrush    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.90f, 0.20f, 0.20f, 1.0f));
            _tpLineBrush    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.20f, 0.85f, 0.30f, 1.0f));
            _entryLineBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(0.90f, 0.80f, 0.10f, 1.0f));
            _labelBrush     = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, Color4.White);
            _labelFormat    = new SharpDX.DirectWrite.TextFormat(
                Core.Globals.DirectWriteFactory, "Segoe UI", 12.0f);
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            _cachedScale = chartScale;   // save for mouse handlers

            if (RenderTarget == null || _stateMachine == null) return;
            if (_stateMachine.Current == TradeState.Idle) return;
            if (_stopPrice <= 0 && _entryPrice <= 0) return;

            float chartRight = (float)chartControl.ClientRectangle.Right;
            float labelX     = chartRight - 145f;

            float entryY = (float)chartScale.GetYByValue(_entryPrice);
            float slY    = _stopPrice > 0 ? (float)chartScale.GetYByValue(_stopPrice) : 0;
            float tpY    = _tpPrice   > 0 ? (float)chartScale.GetYByValue(_tpPrice)   : 0;

            // SL zone
            if (_stopPrice > 0 && _slZoneBrush != null)
            {
                float top    = Math.Min(entryY, slY);
                float bottom = Math.Max(entryY, slY);
                RenderTarget.FillRectangle(new RectangleF(0, top, chartRight, bottom - top), _slZoneBrush);
                RenderTarget.DrawLine(new Vector2(0, slY), new Vector2(chartRight, slY), _slLineBrush, 1.5f);
                DrawLabel($"SL  {_stopPrice:F2}", labelX, slY - 16);
            }

            // TP zone
            if (_tpPrice > 0 && _tpZoneBrush != null)
            {
                float top    = Math.Min(entryY, tpY);
                float bottom = Math.Max(entryY, tpY);
                RenderTarget.FillRectangle(new RectangleF(0, top, chartRight, bottom - top), _tpZoneBrush);
                RenderTarget.DrawLine(new Vector2(0, tpY), new Vector2(chartRight, tpY), _tpLineBrush, 1.5f);

                if (_currentPlan.IsValid)
                {
                    DrawLabel($"TP  {_tpPrice:F2}", labelX, tpY - 16);
                    double rrDisplay = _currentPlan.StopDistanceTicks > 0 && _tpPrice > 0 && _stopPrice > 0
                        ? Math.Abs(_tpPrice - _entryPrice) / Math.Abs(_entryPrice - _stopPrice)
                        : 0;
                    DrawLabel($"RR {rrDisplay:F1}   {_currentPlan.Contracts}x", labelX, tpY + 4);
                    DrawLabel($"Risk ${_currentPlan.RiskDollars:F0}  Profit ${_currentPlan.ProfitDollars:F0}", labelX, tpY + 22);
                }
            }

            // Entry line
            if (_entryLineBrush != null)
                RenderTarget.DrawLine(new Vector2(0, entryY), new Vector2(chartRight, entryY), _entryLineBrush, 1.0f);

            DrawLabel($"Entry {_entryPrice:F2}", labelX, entryY - 16);
        }

        private void DrawLabel(string text, float x, float y)
        {
            if (_labelFormat == null || _labelBrush == null || RenderTarget == null) return;
            var layout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, text, _labelFormat, 200, 20);
            RenderTarget.DrawTextLayout(new Vector2(x, y), layout, _labelBrush);
            layout.Dispose();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Mouse/keyboard — subscribed to ChartControl events (NT8 pattern)
        // ─────────────────────────────────────────────────────────────────────

        private void SubscribeChartEvents()
        {
            if (ChartControl == null) return;
            ChartControl.MouseDown     += OnChartMouseDown;
            ChartControl.MouseMove     += OnChartMouseMove;
            ChartControl.MouseUp       += OnChartMouseUp;
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
            double mousePrice = _cachedScale.GetValueByY((float)pos.Y);

            // Hit-test existing lines to start drag
            if (_stateMachine.Current == TradeState.Planning && _currentPlan.IsValid)
            {
                if (_stopPrice > 0 && IsNearPrice(_stopPrice, pos.Y))
                {
                    _dragMode = DragMode.Sl;
                    e.Handled = true;
                    return;
                }
                if (_tpPrice > 0 && IsNearPrice(_tpPrice, pos.Y))
                {
                    _dragMode = DragMode.Tp;
                    e.Handled = true;
                    return;
                }
            }

            // Fresh click → place SL
            PlaceStop(mousePrice);
            if (_stateMachine.Current == TradeState.Idle)
                _stateMachine.TryTransitionTo(TradeState.Planning);

            e.Handled = true;
        }

        private void OnChartMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dragMode == DragMode.None || _cachedScale == null) return;

            System.Windows.Point pos = e.GetPosition(ChartControl);
            double mousePrice = _cachedScale.GetValueByY((float)pos.Y);

            if (_dragMode == DragMode.Sl)
            {
                PlaceStop(mousePrice);
            }
            else if (_dragMode == DragMode.Tp && _currentPlan.IsValid)
            {
                double newTp       = SnapToTick(mousePrice);
                double stopDist    = Math.Abs(_entryPrice - _stopPrice);
                double tpDist      = Math.Abs(newTp - _entryPrice);
                double impliedRr   = stopDist > 0 ? Math.Round((tpDist / stopDist) * 4.0) / 4.0 : 2.0;
                impliedRr          = Math.Max(0.25, impliedRr);

                _controller.SetRrRatio(impliedRr);
                Dispatcher.InvokeAsync(() => { if (_viewModel != null) _viewModel.RrRatio = impliedRr; });
                ForceRefresh();
            }
        }

        private void OnChartMouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragMode = DragMode.None;
        }

        private void OnChartKeyDown(object sender, KeyEventArgs e)
        {
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
        // Internal helpers
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
                _tpPrice = plan.TpPrice;
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
            ForceRefresh();
        }

        private void DisposeBrushes()
        {
            _slZoneBrush?.Dispose();    _slZoneBrush    = null;
            _tpZoneBrush?.Dispose();    _tpZoneBrush    = null;
            _slLineBrush?.Dispose();    _slLineBrush    = null;
            _tpLineBrush?.Dispose();    _tpLineBrush    = null;
            _entryLineBrush?.Dispose(); _entryLineBrush = null;
            _labelBrush?.Dispose();     _labelBrush     = null;
            _labelFormat?.Dispose();    _labelFormat    = null;
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
                _panel?.Close();
                _viewModel?.Dispose();
            });
        }
    }
}
