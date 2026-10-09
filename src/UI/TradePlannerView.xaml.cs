using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TradeAssistant.Domain;

namespace TradeAssistant.UI
{
    /// <summary>
    /// Trade planner panel. Hosted either as a floating overlay on the chart
    /// (PanelHostMode.ChartOverlay) or embedded inside the chart's Chart
    /// Trader panel (PanelHostMode.ChartTrader). Built as a Border so it can
    /// be added to a WPF Grid in either host.
    /// </summary>
    public class TradePlannerView : Border
    {
        private Button _dirBtn;

        // Controls restyled by ApplyContainerTheme when hosted inside Chart Trader
        private readonly List<TextBlock> _themeLabels = new List<TextBlock>();
        private readonly List<TextBlock> _themeValues = new List<TextBlock>();
        private readonly List<TextBox>   _themeInputs = new List<TextBox>();
        private readonly List<ComboBox>  _themeCombos = new List<ComboBox>();
        private readonly List<CheckBox>  _themeChecks = new List<CheckBox>();

        private static readonly SolidColorBrush LongBrush  = CreateFrozenBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly SolidColorBrush ShortBrush = CreateFrozenBrush(Color.FromRgb(0xC6, 0x28, 0x28));

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public TradePlannerView(TradePlannerViewModel viewModel,
            PanelHostMode mode = PanelHostMode.ChartOverlay)
        {
            DataContext = viewModel;
            Child       = BuildLayout();

            if (mode == PanelHostMode.ChartTrader)
                ApplyChartTraderChrome();
            else
                ApplyChartOverlayChrome();

            // Keep direction button color in sync with direction changes
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TradePlannerViewModel.DirectionLabel) && _dirBtn != null)
                    _dirBtn.Background = viewModel.DirectionLabel == "LONG" ? LongBrush : ShortBrush;
            };
        }

        private void ApplyChartOverlayChrome()
        {
            Width               = 260;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment   = VerticalAlignment.Top;
            Margin              = new Thickness(0, 24, 75, 0);
            Background          = new SolidColorBrush(Color.FromArgb(0xE8, 0x1E, 0x1E, 0x1E));
            BorderBrush         = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            BorderThickness     = new Thickness(1);
            CornerRadius        = new CornerRadius(4);
            Padding             = new Thickness(12);
            IsHitTestVisible    = true;
        }

        private void ApplyChartTraderChrome()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment   = VerticalAlignment.Top;
            Margin              = new Thickness(4, 12, 4, 4);
            Background          = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            BorderBrush         = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));
            BorderThickness     = new Thickness(1);
            CornerRadius        = new CornerRadius(4);
            Padding             = new Thickness(8, 12, 8, 8);
            IsHitTestVisible    = true;
        }

        /// <summary>
        /// Restyles the panel for its container card inside Chart Trader.
        /// <paramref name="hostTextBrush"/> is Chart Trader's themed text
        /// brush: a light brush means Chart Trader itself is dark, so the card
        /// is lightened to stay visibly separate; a dark brush means a light
        /// Chart Trader, so the card stays dark. Card text keeps a fixed
        /// light palette for guaranteed contrast in both cases.
        /// </summary>
        public void ApplyContainerTheme(Brush hostTextBrush)
        {
            bool hostIsDark = IsLight(hostTextBrush);
            Color cardColor = hostIsDark
                ? Color.FromRgb(0x3A, 0x3A, 0x3A)
                : Color.FromRgb(0x1E, 0x1E, 0x1E);
            Background  = new SolidColorBrush(cardColor);
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));

            var labelBrush  = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            var valueBrush  = Brushes.White;
            var inputBg     = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            var inputBorder = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

            foreach (TextBlock lbl in _themeLabels) lbl.Foreground = labelBrush;
            foreach (TextBlock val in _themeValues) val.Foreground = valueBrush;
            foreach (TextBox tb in _themeInputs)
            {
                tb.Foreground   = valueBrush;
                tb.Background   = inputBg;
                tb.BorderBrush  = inputBorder;
            }
            foreach (ComboBox cb in _themeCombos)
            {
                cb.Foreground   = valueBrush;
                cb.Background   = inputBg;
                cb.BorderBrush  = inputBorder;
            }
            foreach (CheckBox chk in _themeChecks) chk.Foreground = labelBrush;
        }

        private static bool IsLight(Brush brush)
        {
            var solid = brush as SolidColorBrush;
            if (solid == null) return false;
            Color c = solid.Color;
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 128;
        }

        public void Detach()
        {
            (DataContext as TradePlannerViewModel)?.Dispose();
            var parent = Parent as Panel;
            parent?.Children.Remove(this);
        }

        private UIElement BuildLayout()
        {
            var root = new StackPanel();
            root.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI"));
            root.SetValue(TextBlock.FontSizeProperty, 12.0);
            root.SetValue(TextBlock.ForegroundProperty, Brushes.White);

            // Header
            var header = new TextBlock
            {
                Text       = "TRADE ASSISTANT",
                FontSize   = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin     = new Thickness(0, 0, 0, 6)
            };
            _themeLabels.Add(header);
            root.Children.Add(header);

            // Direction toggle — full-width, green for LONG / red for SHORT
            var vm = DataContext as TradePlannerViewModel;
            _dirBtn = new Button
            {
                FontSize        = 12,
                FontWeight      = FontWeights.Bold,
                Height          = 28,
                Cursor          = System.Windows.Input.Cursors.Hand,
                Foreground      = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin          = new Thickness(0, 0, 0, 5),
                Background      = vm?.DirectionLabel == "SHORT" ? ShortBrush : LongBrush
            };
            _dirBtn.SetBinding(Button.ContentProperty, new Binding("DirectionLabel"));
            _dirBtn.SetBinding(Button.CommandProperty, new Binding("ToggleDirectionCommand"));
            root.Children.Add(_dirBtn);
            root.Children.Add(MakeSeparator());

            // Risk Mode dropdown
            root.Children.Add(MakeRiskModeRow());
            
            // Risk value input (label changes based on RiskMode)
            root.Children.Add(MakeDynamicInputRow("RiskValueLabel", "RiskValue", "{0:F2}"));
            root.Children.Add(MakeInputRow("R:R Ratio",    "RrRatio",          "{0:F1}"));
            root.Children.Add(MakeUpdateRrButton());
            root.Children.Add(MakeBreakEvenRow());
            root.Children.Add(MakeCheckboxRow("Show Boxes", "ShowTradeBoxes"));
            root.Children.Add(MakeSeparator());

            // SL adjuster: ▼ / ▲ buttons (±1 step per click)
            root.Children.Add(MakeSlAdjusterRow());
            root.Children.Add(MakeInputRow("SL Step (pts)", "SlStepPoints", "{0:0.##}"));
            root.Children.Add(MakeSeparator());

            // Plan display
            root.Children.Add(MakeValueRow("Contracts", "Contracts",     null,      null, 15));
            root.Children.Add(MakeValueRow("Risk $",    "RiskDollars",   "${0:F0}", Color.FromRgb(0xEF, 0x53, 0x50)));
            root.Children.Add(MakeValueRow("Profit $",  "ProfitDollars", "${0:F0}", Color.FromRgb(0x66, 0xBB, 0x6A)));
            root.Children.Add(MakeSeparator());

            // Armed indicator
            var armedBorder = new Border
            {
                Background   = new SolidColorBrush(Color.FromRgb(0xF5, 0x7F, 0x17)),
                CornerRadius = new CornerRadius(3),
                Padding      = new Thickness(6, 2, 6, 2),
                Margin       = new Thickness(0, 5, 0, 5),
                Child = new TextBlock
                {
                    Text                = "ARMED — Click Execute",
                    FontWeight          = FontWeights.Bold,
                    Foreground          = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize            = 10
                }
            };
            armedBorder.SetBinding(VisibilityProperty,
                new Binding("IsArmed") { Converter = new BooleanToVisibilityConverter() });
            root.Children.Add(armedBorder);

            // Status
            var status = new TextBlock
            {
                Foreground   = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontSize     = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 5)
            };
            status.SetBinding(TextBlock.TextProperty, new Binding("StatusMessage"));
            _themeLabels.Add(status);
            root.Children.Add(status);

            // Execute button
            var execBtn = new Button
            {
                Content         = "EXECUTE",
                Background      = new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)),
                Foreground      = Brushes.White,
                FontSize        = 12,
                FontWeight      = FontWeights.Bold,
                Height          = 28,
                Cursor          = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            execBtn.SetBinding(Button.CommandProperty, new Binding("ExecuteCommand"));
            root.Children.Add(execBtn);

            // Break-even button (manual)
            var beBtn = new Button
            {
                Content         = "BREAK-EVEN",
                Background      = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x1A)),
                Foreground      = Brushes.Black,
                FontSize        = 11,
                FontWeight      = FontWeights.Bold,
                Height          = 22,
                Cursor          = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                Margin          = new Thickness(0, 4, 0, 0)
            };
            beBtn.SetBinding(Button.CommandProperty, new Binding("BreakEvenCommand"));
            root.Children.Add(beBtn);

            return root;
        }

        private UIElement MakeInputRow(string label, string binding, string format)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lbl = new TextBlock
            {
                Text              = label,
                Foreground        = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 11
            };
            _themeLabels.Add(lbl);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var tb = new TextBox
            {
                Background               = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Foreground               = Brushes.White,
                BorderBrush              = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness          = new Thickness(1),
                Padding                  = new Thickness(3, 1, 3, 1),
                Height                   = 20,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize                 = 11
            };
            _themeInputs.Add(tb);
            tb.SetBinding(TextBox.TextProperty,
                new Binding(binding)
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                    StringFormat        = format
                });
            // NT8's instrument search fires on PreviewKeyDown at the chart/window level.
            // We intercept ALL keys here (e.Handled = true) before NT8 sees them, then
            // manually handle numeric text input so the TextBox still works correctly.
            tb.PreviewKeyDown += NumericTextBoxPreviewKeyDown;
            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);

            return grid;
        }

        private static UIElement MakeUpdateRrButton()
        {
            var btn = new Button
            {
                Content         = "UPDATE RR",
                Background      = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x80)),
                Foreground      = Brushes.White,
                FontSize        = 10,
                FontWeight      = FontWeights.Bold,
                Height          = 18,
                Cursor          = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                Margin          = new Thickness(0, 0, 0, 3),
                ToolTip         = "Move the live TP order to the new R:R (active trades only)"
            };
            btn.SetBinding(Button.CommandProperty, new Binding("UpdateRrCommand"));
            return btn;
        }

        private UIElement MakeBreakEvenRow()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text              = "BE at R:R",
                Foreground        = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 11
            };
            _themeLabels.Add(lbl);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var tb = new TextBox
            {
                Background               = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Foreground               = Brushes.White,
                BorderBrush              = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness          = new Thickness(1),
                Padding                  = new Thickness(3, 1, 3, 1),
                Height                   = 20,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize                 = 11
            };
            _themeInputs.Add(tb);
            tb.SetBinding(TextBox.TextProperty,
                new Binding("BreakEvenRr")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                    StringFormat        = "{0:F1}"
                });
            tb.PreviewKeyDown += NumericTextBoxPreviewKeyDown;
            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);

            var autoBe = new CheckBox
            {
                Content           = "Auto BE",
                Foreground        = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(8, 0, 0, 0)
            };
            _themeChecks.Add(autoBe);
            autoBe.SetBinding(CheckBox.IsCheckedProperty,
                new Binding("AutoBreakEven")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            Grid.SetColumn(autoBe, 2);
            grid.Children.Add(autoBe);

            return grid;
        }

        private UIElement MakeRiskModeRow()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lbl = new TextBlock
            {
                Text = "Risk Mode",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            _themeLabels.Add(lbl);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var combo = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3, 1, 3, 1),
                Height = 20,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            _themeCombos.Add(combo);
            
            // Add RiskMode enum values
            combo.Items.Add(new ComboBoxItem { Content = "Percentage", Tag = RiskMode.Percentage });
            combo.Items.Add(new ComboBoxItem { Content = "Fixed Amount", Tag = RiskMode.FixedAmount });
            
            // Bind selected value to RiskMode property
            combo.SetBinding(ComboBox.SelectedValueProperty,
                new Binding("RiskMode")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            combo.SelectedValuePath = "Tag";
            
            Grid.SetColumn(combo, 1);
            grid.Children.Add(combo);

            return grid;
        }

        private UIElement MakeCheckboxRow(string label, string binding)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            _themeLabels.Add(lbl);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var checkBox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };
            _themeChecks.Add(checkBox);
            checkBox.SetBinding(CheckBox.IsCheckedProperty,
                new Binding(binding)
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });

            Grid.SetColumn(checkBox, 1);
            grid.Children.Add(checkBox);

            return grid;
        }

        private UIElement MakeDynamicInputRow(string labelBinding, string valueBinding, string format)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lbl = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            lbl.SetBinding(TextBlock.TextProperty, new Binding(labelBinding));
            _themeLabels.Add(lbl);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var tb = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3, 1, 3, 1),
                Height = 20,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            _themeInputs.Add(tb);
            tb.SetBinding(TextBox.TextProperty,
                new Binding(valueBinding)
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                    StringFormat = format
                });
            tb.PreviewKeyDown += NumericTextBoxPreviewKeyDown;
            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);

            return grid;
        }

        private UIElement MakeValueRow(string label, string binding, string format,
            Color? fg, double fontSize = 12)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lbl = new TextBlock
            {
                Text              = label,
                Foreground        = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 11
            };
            _themeLabels.Add(lbl);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var val = new TextBlock
            {
                FontWeight        = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = fontSize,
                Foreground        = fg.HasValue
                    ? new SolidColorBrush(fg.Value)
                    : Brushes.White
            };
            if (!fg.HasValue) _themeValues.Add(val);
            val.SetBinding(TextBlock.TextProperty,
                new Binding(binding) { StringFormat = format });
            Grid.SetColumn(val, 1);
            grid.Children.Add(val);

            return grid;
        }

        private static UIElement MakeSlAdjusterRow()
        {
            var border = new Border
            {
                Background    = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                BorderBrush   = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                CornerRadius  = new CornerRadius(3),
                Padding       = new Thickness(5),
                Margin        = new Thickness(0, 0, 0, 3)
            };

            var buttons = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var downBtn = MakeNudgeButton("▼", "NudgeDownCommand", "Tighter SL (−1 step)");
            Grid.SetColumn(downBtn, 0);
            buttons.Children.Add(downBtn);

            var upBtn = MakeNudgeButton("▲", "NudgeUpCommand", "Wider SL (+1 step)");
            Grid.SetColumn(upBtn, 2);
            buttons.Children.Add(upBtn);

            border.Child = buttons;
            return border;
        }

        private static Button MakeNudgeButton(string content, string commandBinding, string tooltip)
        {
            var btn = new Button
            {
                Content                    = content,
                Background                 = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
                Foreground                 = Brushes.White,
                FontSize                   = 8,
                Width                      = 14,
                Height                     = 14,
                Padding                    = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment   = VerticalAlignment.Center,
                BorderThickness            = new Thickness(0),
                Cursor                     = System.Windows.Input.Cursors.Hand,
                ToolTip                    = tooltip
            };
            btn.SetBinding(Button.CommandProperty, new Binding(commandBinding));
            return btn;
        }

        private static UIElement MakeSeparator()
        {
            return new Separator
            {
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                Margin     = new Thickness(0, 1, 0, 5)
            };
        }

        // ── Keyboard handling for numeric TextBoxes ───────────────────────────
        // PreviewKeyDown is intercepted (e.Handled = true) to prevent NT8's
        // instrument search from firing. Text is then managed manually.

        private static void NumericTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true; // block NT8 instrument search before it sees this key
            var box = (TextBox)sender;

            switch (e.Key)
            {
                case Key.Enter:
                    box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    return;
                case Key.Tab:
                case Key.Escape:
                    e.Handled = false; // let Tab/Escape route normally for focus/cancel
                    return;
                case Key.Back:
                    if (box.SelectionLength > 0) { DeleteSelection(box); return; }
                    if (box.CaretIndex > 0)
                    {
                        int i = box.CaretIndex - 1;
                        box.Text = box.Text.Remove(i, 1);
                        box.CaretIndex = i;
                    }
                    return;
                case Key.Delete:
                    if (box.SelectionLength > 0) { DeleteSelection(box); return; }
                    if (box.CaretIndex < box.Text.Length)
                    {
                        int i = box.CaretIndex;
                        box.Text = box.Text.Remove(i, 1);
                        box.CaretIndex = i;
                    }
                    return;
                case Key.Left:
                    if (box.CaretIndex > 0) box.CaretIndex--;
                    return;
                case Key.Right:
                    if (box.CaretIndex < box.Text.Length) box.CaretIndex++;
                    return;
                case Key.Home: box.CaretIndex = 0; return;
                case Key.End:  box.CaretIndex = box.Text.Length; return;
                case Key.A when Keyboard.Modifiers == ModifierKeys.Control:
                    box.SelectAll();
                    return;
                case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                case Key.V when Keyboard.Modifiers == ModifierKeys.Control:
                case Key.X when Keyboard.Modifiers == ModifierKeys.Control:
                case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                    e.Handled = false;
                    return;
                default:
                    char c = NumericKeyToChar(e.Key);
                    if (c == '\0') return;
                    if (box.SelectionLength > 0) DeleteSelection(box);
                    int pos = box.CaretIndex;
                    box.Text = box.Text.Insert(pos, c.ToString());
                    box.CaretIndex = pos + 1;
                    return;
            }
        }

        private static void DeleteSelection(TextBox box)
        {
            int start = box.SelectionStart;
            box.Text   = box.Text.Remove(start, box.SelectionLength);
            box.CaretIndex = start;
        }

        private static char NumericKeyToChar(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return (char)('0' + (key - Key.D0));
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return (char)('0' + (key - Key.NumPad0));
            if (key == Key.OemPeriod || key == Key.Decimal)  return '.';
            if (key == Key.OemMinus  || key == Key.Subtract) return '-';
            return '\0';
        }
    }
}
