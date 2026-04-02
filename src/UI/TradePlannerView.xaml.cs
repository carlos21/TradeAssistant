using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TradeAssistant.Domain;

namespace TradeAssistant.UI
{
    /// <summary>
    /// Trade planner panel embedded directly into the NinjaTrader chart.
    /// Built as a Border so it can be added to the chart's WPF Grid.
    /// Positioned top-right with semi-transparent background.
    /// </summary>
    public class TradePlannerView : Border
    {
        private Button _dirBtn;

        private static readonly SolidColorBrush LongBrush  = CreateFrozenBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly SolidColorBrush ShortBrush = CreateFrozenBrush(Color.FromRgb(0xC6, 0x28, 0x28));

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public TradePlannerView(TradePlannerViewModel viewModel)
        {
            DataContext          = viewModel;
            Width                = 260;
            HorizontalAlignment  = HorizontalAlignment.Right;
            VerticalAlignment    = VerticalAlignment.Top;
            Margin               = new Thickness(0, 10, 75, 0);
            Background           = new SolidColorBrush(Color.FromArgb(0xE8, 0x1E, 0x1E, 0x1E));
            BorderBrush          = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            BorderThickness      = new Thickness(1);
            CornerRadius         = new CornerRadius(4);
            Padding              = new Thickness(12);
            IsHitTestVisible     = true;

            Child = BuildLayout();

            // Keep direction button color in sync with direction changes
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TradePlannerViewModel.DirectionLabel) && _dirBtn != null)
                    _dirBtn.Background = viewModel.DirectionLabel == "LONG" ? LongBrush : ShortBrush;
            };
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
            root.SetValue(TextBlock.FontSizeProperty, 13.0);
            root.SetValue(TextBlock.ForegroundProperty, Brushes.White);

            // Header
            root.Children.Add(new TextBlock
            {
                Text       = "TRADE ASSISTANT",
                FontSize   = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin     = new Thickness(0, 0, 0, 10)
            });

            // Direction toggle — full-width, green for LONG / red for SHORT
            var vm = DataContext as TradePlannerViewModel;
            _dirBtn = new Button
            {
                FontSize        = 14,
                FontWeight      = FontWeights.Bold,
                Height          = 36,
                Cursor          = System.Windows.Input.Cursors.Hand,
                Foreground      = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin          = new Thickness(0, 0, 0, 8),
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
            root.Children.Add(MakeCheckboxRow("Show Boxes", "ShowTradeBoxes"));
            root.Children.Add(MakeSeparator());

            // Plan display
            root.Children.Add(MakeValueRow("Contracts", "Contracts",     null,      null, 20));
            root.Children.Add(MakeValueRow("Risk $",    "RiskDollars",   "${0:F0}", Color.FromRgb(0xEF, 0x53, 0x50)));
            root.Children.Add(MakeValueRow("Profit $",  "ProfitDollars", "${0:F0}", Color.FromRgb(0x66, 0xBB, 0x6A)));
            root.Children.Add(MakeSeparator());

            // Armed indicator
            var armedBorder = new Border
            {
                Background   = new SolidColorBrush(Color.FromRgb(0xF5, 0x7F, 0x17)),
                CornerRadius = new CornerRadius(4),
                Padding      = new Thickness(8, 4, 8, 4),
                Margin       = new Thickness(0, 8, 0, 8),
                Child = new TextBlock
                {
                    Text                = "ARMED — Press SPACE to Execute",
                    FontWeight          = FontWeights.Bold,
                    Foreground          = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize            = 11
                }
            };
            armedBorder.SetBinding(VisibilityProperty,
                new Binding("IsArmed") { Converter = new BooleanToVisibilityConverter() });
            root.Children.Add(armedBorder);

            // Status
            var status = new TextBlock
            {
                Foreground   = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontSize     = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 8)
            };
            status.SetBinding(TextBlock.TextProperty, new Binding("StatusMessage"));
            root.Children.Add(status);

            // Execute button
            var execBtn = new Button
            {
                Content         = "EXECUTE",
                Background      = new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)),
                Foreground      = Brushes.White,
                FontSize        = 13,
                FontWeight      = FontWeights.Bold,
                Height          = 36,
                Cursor          = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            execBtn.SetBinding(Button.CommandProperty, new Binding("ExecuteCommand"));
            root.Children.Add(execBtn);

            return root;
        }

        private static UIElement MakeInputRow(string label, string binding, string format)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lbl = new TextBlock
            {
                Text              = label,
                Foreground        = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 12
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var tb = new TextBox
            {
                Background               = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Foreground               = Brushes.White,
                BorderBrush              = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness          = new Thickness(1),
                Padding                  = new Thickness(4, 2, 4, 2),
                Height                   = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize                 = 12
            };
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

        private static UIElement MakeRiskModeRow()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lbl = new TextBlock
            {
                Text = "Risk Mode",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var combo = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2),
                Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            
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

        private static UIElement MakeCheckboxRow(string label, string binding)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var checkBox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };
            checkBox.SetBinding(CheckBox.IsCheckedProperty,
                new Binding(binding)
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });

            Grid.SetColumn(checkBox, 1);
            grid.Children.Add(checkBox);

            return grid;
        }

        private static UIElement MakeDynamicInputRow(string labelBinding, string valueBinding, string format)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lbl = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            lbl.SetBinding(TextBlock.TextProperty, new Binding(labelBinding));
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var tb = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2),
                Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
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

        private static UIElement MakeValueRow(string label, string binding, string format,
            Color? fg, double fontSize = 13)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lbl = new TextBlock
            {
                Text              = label,
                Foreground        = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 12
            };
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
            val.SetBinding(TextBlock.TextProperty,
                new Binding(binding) { StringFormat = format });
            Grid.SetColumn(val, 1);
            grid.Children.Add(val);

            return grid;
        }

        private static UIElement MakeSeparator()
        {
            return new Separator
            {
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                Margin     = new Thickness(0, 2, 0, 8)
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
