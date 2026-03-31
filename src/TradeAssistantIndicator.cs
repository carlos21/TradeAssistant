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
        private OrderLevelTracker           _orderTracker;

        // ── Chart state ───────────────────────────────────────────────────────
        private double    _stopPrice   = 0;
        private double    _tpPrice     = 0;
        private double    _entryPrice  = 0;
        private TradePlan _currentPlan = TradePlan.Empty;
        private ChartScale _cachedScale;
        private bool       _initialized = false;

        private enum DragMode { None, Sl, Tp }
        private DragMode _dragMode = DragMode.None;
        private bool _isModifyingOrder = false;

        private const double HitTolerance = 12.0;  // Increased for easier grabbing
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
                AccountName      = "";  // Empty = use ChartTrader account
                TestMode         = false;
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

        [NinjaScriptProperty]
        [Display(Name = "Account Name (optional)", Order = 0, GroupName = "Account")]
        public string AccountName { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Test Mode", Order = 0, GroupName = "Debug")]
        public bool TestMode { get; set; }

        // ── Test mode state ──────────────────────────────────────────────────
        private double _testStopPrice = 0;
        private double _testTpPrice = 0;
        private double _testEntryPrice = 0;

        // ── Automatic order discovery ────────────────────────────────────────
        // Continuously tries to find orders until they appear (handles F5 refresh)

        // ─────────────────────────────────────────────────────────────────────
        // Layer wiring
        // ─────────────────────────────────────────────────────────────────────

        private void InitializeLayers()
        {
            Account account = ResolveAccount();
            if (account == null)
            {
                // No account available - indicator will show error
                return;
            }

            _stateMachine   = new TradeStateMachine();
            _instrumentInfo = new NinjaInstrumentInfoProvider(this);
            _accountData    = new NinjaAccountDataProvider(account);
            _orderAdapter   = new NinjaOrderAdapter(this, account);
            _orderTracker   = new OrderLevelTracker(account, Instrument);
            _orderTracker.OrdersUpdated += OnActiveOrdersUpdated;
            _orderTracker.LevelsCleared += OnActiveOrdersCleared;

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

            // Check for existing working orders (survives F5)
            _orderTracker.DiscoverWorkingOrders();
        }

        private void OpenPanel()
        {
            // Don't open panel if initialization failed (e.g., account not found)
            if (_controller == null || _stateMachine == null)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                _viewModel = new TradePlannerViewModel(_controller, _stateMachine);
                _viewModel.RiskMode         = RiskMode;
                _viewModel.RiskValue        = RiskValue;
                _viewModel.RrRatio          = RrRatio;
                _viewModel.BreakEvenRr      = BreakEvenRr;
                _viewModel.TestMode         = TestMode;

                // Populate account selector
                var accountNames = new System.Collections.Generic.List<string>();
                foreach (Account acct in Account.All)
                    accountNames.Add(acct.Name);
                _viewModel.AvailableAccounts = accountNames;

                // Set current account selection
                Account currentAccount = ResolveAccount();
                if (currentAccount != null)
                    _viewModel.SelectedAccount = currentAccount.Name;
                else if (accountNames.Count > 0)
                    _viewModel.SelectedAccount = accountNames[0];

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
                                ? _entryPrice - dist
                                : _entryPrice + dist;
                            // Snap distance to multiple of 5
                            _stopPrice = SnapSlDistanceToMultipleOfFive(_stopPrice);
                            _controller.SetStopPrice(_stopPrice);
                        }
                        else
                        {
                            PlaceDefaultStop();
                        }
                    }
                    ForceRefresh();
                };

                _viewModel.AccountChangedAction = (acctName) =>
                {
                    // Switch to the selected account
                    AccountName = acctName;
                    Account newAccount = ResolveAccount();
                    if (newAccount != null && _orderTracker != null)
                    {
                        // Recreate order tracker with new account
                        _orderTracker.Dispose();
                        _orderTracker = new OrderLevelTracker(newAccount, Instrument);
                        _orderTracker.OrdersUpdated += OnActiveOrdersUpdated;
                        _orderTracker.LevelsCleared += OnActiveOrdersCleared;
                        _orderTracker.DiscoverWorkingOrders();
                        ForceRefresh();
                    }
                };

                _viewModel.TestModeChangedAction = (enabled) =>
                {
                    TestMode = enabled;
                    if (enabled)
                    {
                        // Only initialize if we have a valid entry price
                        if (_entryPrice > 0)
                        {
                            InitializeTestLevels();
                        }
                        // If _entryPrice is not ready yet, OnBarUpdate will init when it is
                    }
                    else
                    {
                        // Reset test levels
                        _testEntryPrice = 0;
                        _testStopPrice = 0;
                        _testTpPrice = 0;
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
            // Default 20 points, but snap to nearest multiple of 5 for clean distance
            double defaultDistance = 20.0;
            _stopPrice = dir == TradeDirection.Long
                ? _entryPrice - defaultDistance
                : _entryPrice + defaultDistance;
            // Ensure the distance is snapped to multiple of 5
            _stopPrice = SnapSlDistanceToMultipleOfFive(_stopPrice);
            _controller.SetStopPrice(_stopPrice);
        }

        /// <summary>
        /// Initializes fake SL/TP levels for Test Mode (no real orders).
        /// </summary>
        private void InitializeTestLevels()
        {
            _testEntryPrice = _entryPrice;
            // Place SL 20 points away (snapped to multiple of 5 distance)
            _testStopPrice = SnapSlDistanceToMultipleOfFive(_entryPrice - 20.0);
            // Place TP 40 points away (2:1 RR)
            _testTpPrice = SnapToTick(_entryPrice + 40.0);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Price tick
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            _entryPrice = Close[0];
            _controller.SetEntryPrice(_entryPrice);

            // Test Mode: Initialize fake levels if not already set
            if (TestMode && _testEntryPrice == 0 && _entryPrice > 0)
            {
                InitializeTestLevels();
                ForceRefresh();
            }

            AutoInitialize();

            TradeDirection dir = _viewModel?.Direction ?? TradeDirection.Long;
            _breakEvenService.OnPriceUpdate(_entryPrice, dir);

            // Update order tracker with position info for active trade display
            // Lazy initialization: if tracker wasn't created at startup (no account), create it now
            Account account = ResolveAccount();
            if (account != null)
            {
                if (_orderTracker == null)
                {
                    _orderTracker = new OrderLevelTracker(account, Instrument);
                    _orderTracker.OrdersUpdated += OnActiveOrdersUpdated;
                    _orderTracker.LevelsCleared += OnActiveOrdersCleared;
                }

                Position pos = GetPositionForAccount(account);
                _orderTracker.UpdatePosition(pos);

                // Continuously try to discover orders until found (handles F5 refresh)
                // Checks every 5 bars to avoid spamming, keeps trying indefinitely
                // This works even without a position (pending OCO orders)
                if (!_orderTracker.HasWorkingOrders && CurrentBar % 5 == 0)
                {
                    _orderTracker.DiscoverWorkingOrders();
                }
            }
            else if (account != null && _orderTracker == null)
            {
                // Initialize tracker even without position (to catch pending orders)
                _orderTracker = new OrderLevelTracker(account, Instrument);
                _orderTracker.OrdersUpdated += OnActiveOrdersUpdated;
                _orderTracker.LevelsCleared += OnActiveOrdersCleared;
                _orderTracker.DiscoverWorkingOrders();
            }

            ForceRefresh();
        }

        private Position GetPositionForAccount(Account account)
        {
            if (account == null) return null;
            foreach (Position pos in account.Positions)
            {
                if (pos.Instrument == Instrument)
                    return pos;
            }
            return null;
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
            if (_entryPrice <= 0) return;

            // ── Test Mode: Show fake levels for testing ──
            if (TestMode && _testEntryPrice > 0)
            {
                RenderTestLevels(chartControl, chartScale);
                return;
            }

            // ── Active Trade Mode: Show persistent order levels ──
            if (_stateMachine.Current == TradeState.Active || 
                _stateMachine.Current == TradeState.BreakEvenTriggered ||
                _orderTracker?.HasWorkingOrders == true)
            {
                RenderActiveTradeLevels(chartControl, chartScale);
                return;
            }

            // ── Planning Mode: Show planning zones ──
            if (_stateMachine.Current != TradeState.Idle)
            {
                RenderPlanningZones(chartControl, chartScale);
            }
        }

        private void RenderPlanningZones(ChartControl chartControl, ChartScale chartScale)
        {
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

                RenderTarget.FillRectangle(new RectangleF(rectLeft, top, RectWidth, height), _slZoneBrush);

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

                RenderTarget.FillRectangle(new RectangleF(rectLeft, top, RectWidth, height), _tpZoneBrush);

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

        private void RenderActiveTradeLevels(ChartControl chartControl, ChartScale chartScale)
        {
            float chartLeft = (float)chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
            float chartRight = (float)chartControl.GetXByBarIndex(ChartBars, ChartBars.ToIndex);

            double entryPrice = _orderTracker?.AverageEntryPrice > 0 ? _orderTracker.AverageEntryPrice : _entryPrice;
            float entryY = (float)chartScale.GetYByValue(entryPrice);

            // Draw Entry Line (always show if we have a position)
            if (_entryLineBrush != null)
            {
                RenderTarget.DrawLine(
                    new Vector2(chartLeft, entryY), 
                    new Vector2(chartRight, entryY), 
                    _entryLineBrush, 1.0f);
            }

            // Draw SL Line
            if (_orderTracker?.StopPrice.HasValue == true && _slLineBrush != null)
            {
                DrawHorizontalLine(_orderTracker.StopPrice.Value, _slLineBrush, chartLeft, chartRight, chartScale);
                DrawLevelLabel("SL", _orderTracker.StopPrice.Value, entryPrice, _slLabelBgBrush, chartControl, chartScale);
            }

            // Draw TP Line
            if (_orderTracker?.TpPrice.HasValue == true && _tpLineBrush != null)
            {
                DrawHorizontalLine(_orderTracker.TpPrice.Value, _tpLineBrush, chartLeft, chartRight, chartScale);
                DrawLevelLabel("TP", _orderTracker.TpPrice.Value, entryPrice, _tpLabelBgBrush, chartControl, chartScale);
            }

            // Note: Orders are now auto-discovered every 5 bars after F5
            // No manual refresh needed - just wait a few seconds after chart reload
        }

        private void DrawHorizontalLine(double price, SharpDX.Direct2D1.SolidColorBrush brush,
            float left, float right, ChartScale scale)
        {
            if (brush == null || price <= 0) return;
            float y = (float)scale.GetYByValue(price);
            RenderTarget.DrawLine(new Vector2(left, y), new Vector2(right, y), brush, 2.0f);

            // Draw drag handle indicator (small rectangle at right edge)
            float handleSize = 6f;
            RenderTarget.FillRectangle(
                new RectangleF(right - handleSize - 2, y - handleSize / 2, handleSize, handleSize),
                brush);
        }

        private void DrawLevelLabel(string label, double price, double entryPrice,
            SharpDX.Direct2D1.SolidColorBrush bgBrush, ChartControl chartControl, ChartScale chartScale)
        {
            if (_labelFormat == null || _labelBrush == null) return;

            float y = (float)chartScale.GetYByValue(price);
            float x = (float)chartControl.GetXByBarIndex(ChartBars, ChartBars.ToIndex) - 80;

            double distance = Math.Abs(price - entryPrice);
            string text = $"{label} {price:F2} ({distance:F2})";

            var layout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, text, _labelFormat, 200, 20);

            float w = layout.Metrics.Width;
            float h = layout.Metrics.Height;

            if (bgBrush != null)
                RenderTarget.FillRectangle(new RectangleF(x - 4, y - h - 4, w + 8, h + 6), bgBrush);

            RenderTarget.DrawTextLayout(new Vector2(x, y - h - 3), layout, _labelBrush);
            layout.Dispose();
        }

        /// <summary>
        /// Renders fake SL/TP levels for Test Mode (no real orders needed).
        /// </summary>
        private void RenderTestLevels(ChartControl chartControl, ChartScale chartScale)
        {
            float chartLeft = (float)chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
            float chartRight = (float)chartControl.GetXByBarIndex(ChartBars, ChartBars.ToIndex);

            float entryY = (float)chartScale.GetYByValue(_testEntryPrice);

            // Draw Entry Line (dashed style for test)
            if (_entryLineBrush != null)
            {
                RenderTarget.DrawLine(
                    new Vector2(chartLeft, entryY),
                    new Vector2(chartRight, entryY),
                    _entryLineBrush, 1.0f);
            }

            // Draw Test SL Line
            if (_testStopPrice > 0 && _slLineBrush != null)
            {
                DrawHorizontalLine(_testStopPrice, _slLineBrush, chartLeft, chartRight, chartScale);
                DrawLevelLabel("SL (TEST)", _testStopPrice, _testEntryPrice, _slLabelBgBrush, chartControl, chartScale);
            }

            // Draw Test TP Line
            if (_testTpPrice > 0 && _tpLineBrush != null)
            {
                DrawHorizontalLine(_testTpPrice, _tpLineBrush, chartLeft, chartRight, chartScale);
                DrawLevelLabel("TP (TEST)", _testTpPrice, _testEntryPrice, _tpLabelBgBrush, chartControl, chartScale);
            }

            // Draw "TEST MODE" label
            if (_labelFormat != null && _labelBrush != null)
            {
                var testLayout = new SharpDX.DirectWrite.TextLayout(
                    Core.Globals.DirectWriteFactory, "TEST MODE", _labelFormat, 100, 20);
                float testX = chartLeft + 10;
                float testY = entryY + 20;
                RenderTarget.FillRectangle(new RectangleF(testX - 4, testY - 2, testLayout.Metrics.Width + 8, testLayout.Metrics.Height + 4), _slLabelBgBrush);
                RenderTarget.DrawTextLayout(new Vector2(testX, testY), testLayout, _labelBrush);
                testLayout.Dispose();
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
            if (_isModifyingOrder) return;

            System.Windows.Point pos = e.GetPosition(ChartControl);

            // Planning mode: drag planning SL/TP
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

            // Active trade mode: drag working order SL/TP
            if (_orderTracker?.HasWorkingOrders == true)
            {
                if (_orderTracker.StopPrice.HasValue && IsNearPrice(_orderTracker.StopPrice.Value, pos.Y))
                {
                    _dragMode = DragMode.Sl;
                    Mouse.Capture(ChartControl);
                    e.Handled = true;
                    return;
                }
                if (_orderTracker.TpPrice.HasValue && IsNearPrice(_orderTracker.TpPrice.Value, pos.Y))
                {
                    _dragMode = DragMode.Tp;
                    Mouse.Capture(ChartControl);
                    e.Handled = true;
                    return;
                }
            }

            // Test Mode: drag fake SL/TP
            if (TestMode && _testEntryPrice > 0)
            {
                if (_testStopPrice > 0 && IsNearPrice(_testStopPrice, pos.Y))
                {
                    _dragMode = DragMode.Sl;
                    Mouse.Capture(ChartControl);
                    e.Handled = true;
                    return;
                }
                if (_testTpPrice > 0 && IsNearPrice(_testTpPrice, pos.Y))
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
                // Change cursor to resize arrow when hovering near a draggable line
                if (_cachedScale != null)
                {
                    bool nearPlanningSl = _stateMachine?.Current == TradeState.Planning && 
                                          _stopPrice > 0 && IsNearPrice(_stopPrice, pos.Y);
                    bool nearPlanningTp = _stateMachine?.Current == TradeState.Planning && 
                                          _tpPrice > 0 && IsNearPrice(_tpPrice, pos.Y);
                    bool nearActiveSl = _orderTracker?.StopPrice.HasValue == true && 
                                        IsNearPrice(_orderTracker.StopPrice.Value, pos.Y);
                    bool nearActiveTp = _orderTracker?.TpPrice.HasValue == true && 
                                        IsNearPrice(_orderTracker.TpPrice.Value, pos.Y);
                    bool nearTestSl = TestMode && _testStopPrice > 0 && IsNearPrice(_testStopPrice, pos.Y);
                    bool nearTestTp = TestMode && _testTpPrice > 0 && IsNearPrice(_testTpPrice, pos.Y);

                    bool nearBorder = nearPlanningSl || nearPlanningTp || nearActiveSl || nearActiveTp || nearTestSl || nearTestTp;
                    ChartControl.Cursor = nearBorder ? Cursors.SizeNS : null;
                }
                return;
            }

            if (_cachedScale == null) return;
            double mousePrice = _cachedScale.GetValueByY((float)pos.Y);

            if (_dragMode == DragMode.Sl)
            {
                if (_stateMachine.Current == TradeState.Planning)
                {
                    PlaceStop(mousePrice);
                }
                else if (TestMode)
                {
                    // Test mode dragging - show snapped position visually
                    _testStopPrice = SnapSlDistanceToMultipleOfFive(mousePrice);
                    ForceRefresh();
                }
                else
                {
                    // Active order dragging - show snapped position visually
                    _stopPrice = SnapSlDistanceToMultipleOfFive(mousePrice);
                    ForceRefresh();
                }
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
                else if (TestMode)
                {
                    // Test mode dragging
                    _testTpPrice = SnapToTick(mousePrice);
                    ForceRefresh();
                }
                // Active order dragging - visual feedback only, actual modify on MouseUp
                ForceRefresh();
            }
        }

        private void OnChartMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode == DragMode.None) return;

            System.Windows.Point pos = e.GetPosition(ChartControl);
            Mouse.Capture(null);

            if (_cachedScale != null)
            {
                double mousePrice = _cachedScale.GetValueByY((float)pos.Y);

                if (_dragMode == DragMode.Sl)
                {
                    // SL distance from entry snaps to multiple of 5
                    double newPrice = SnapSlDistanceToMultipleOfFive(mousePrice);

                    if (_stateMachine.Current == TradeState.Planning)
                    {
                        PlaceStop(newPrice);
                    }
                    else if (TestMode)
                    {
                        // Test mode - just update the fake SL
                        _testStopPrice = newPrice;
                    }
                    else if (_orderTracker?.HasWorkingOrders == true)
                    {
                        // Modify active stop order
                        _isModifyingOrder = true;
                        _orderTracker.ModifyStopPrice(newPrice);
                        _isModifyingOrder = false;
                    }
                }
                else if (_dragMode == DragMode.Tp)
                {
                    // TP snaps to tick size
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
                    else if (TestMode)
                    {
                        // Test mode - just update the fake TP
                        _testTpPrice = newPrice;
                    }
                    else if (_orderTracker?.HasWorkingOrders == true)
                    {
                        // Modify active TP order
                        _isModifyingOrder = true;
                        _orderTracker.ModifyTpPrice(newPrice);
                        _isModifyingOrder = false;
                    }
                }
            }

            _dragMode = DragMode.None;
            ForceRefresh();
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
                if (TestMode)
                {
                    // Reset test levels
                    InitializeTestLevels();
                }
                else
                {
                    _stateMachine.Reset();
                    ResetChartState();
                }
            }
            else if (e.Key == Key.R)
            {
                // Manual refresh - rediscover orders (useful after F5)
                e.Handled = true;
                if (_orderTracker != null)
                {
                    _orderTracker.DiscoverWorkingOrders();
                    ForceRefresh();
                }
                else if (_entryPrice > 0)
                {
                    // Try to initialize tracker if it doesn't exist
                    Account account = ResolveAccount();
                    if (account != null)
                    {
                        _orderTracker = new OrderLevelTracker(account, Instrument);
                        _orderTracker.OrdersUpdated += OnActiveOrdersUpdated;
                        _orderTracker.LevelsCleared += OnActiveOrdersCleared;
                        _orderTracker.DiscoverWorkingOrders();
                        ForceRefresh();
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private void PlaceStop(double price)
        {
            _stopPrice = SnapSlDistanceToMultipleOfFive(price);
            _controller.SetStopPrice(_stopPrice);
            ForceRefresh();
        }

        private double SnapToTick(double price)
        {
            double tick = _instrumentInfo?.TickSize ?? 0.25;
            return tick > 0 ? Math.Round(price / tick) * tick : price;
        }

        /// <summary>
        /// Snaps SL distance from entry to nearest multiple of 5 points.
        /// The SL price will be entry ± (multiple of 5), not rounded to 5 itself.
        /// </summary>
        private double SnapSlDistanceToMultipleOfFive(double desiredStopPrice)
        {
            double distance = Math.Abs(_entryPrice - desiredStopPrice);
            double snappedDistance = Math.Round(distance / 5.0) * 5.0;
            
            // Determine direction (SL is below entry for long, above for short)
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

            // When trade becomes active, try to discover working orders
            if (next == TradeState.Active && _orderTracker != null)
            {
                _orderTracker.DiscoverWorkingOrders();
            }
        }

        private void OnActiveOrdersUpdated()
        {
            Dispatcher.InvokeAsync(() => ForceRefresh());
        }

        private void OnActiveOrdersCleared()
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

        private Account ResolveAccount()
        {
            // If AccountName is specified, find that specific account
            if (!string.IsNullOrWhiteSpace(AccountName))
            {
                foreach (Account acct in Account.All)
                {
                    if (acct.Name.Equals(AccountName.Trim(), StringComparison.OrdinalIgnoreCase))
                        return acct;
                }
                // Account not found - will return null
                return null;
            }

            // Otherwise, use the ChartTrader account (current chart's selected account)
            return ChartControl?.OwnerChart?.ChartTrader?.Account;
        }

        private void Cleanup()
        {
            DisposeBrushes();

            _controller.PlanUpdated    -= OnPlanUpdated;
            _stateMachine.StateChanged -= OnStateChanged;

            if (_orderTracker != null)
            {
                _orderTracker.OrdersUpdated -= OnActiveOrdersUpdated;
                _orderTracker.LevelsCleared -= OnActiveOrdersCleared;
                _orderTracker.Dispose();
                _orderTracker = null;
            }

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
