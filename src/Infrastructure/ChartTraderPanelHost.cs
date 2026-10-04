using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Gui.Chart;

namespace TradeAssistant.Infrastructure
{
    /// <summary>
    /// Embeds a WPF element into the chart's Chart Trader panel by appending a
    /// row to ChartTrader's internal "grdMain" grid — the same pattern as the
    /// official NT8 SDK ChartTraderCustomButtonsExample. Thin adapter: all
    /// TradeAssistant logic stays in the upper layers.
    /// </summary>
    public class ChartTraderPanelHost
    {
        private Grid             _grid;
        private int              _rowIndex = -1;
        private FrameworkElement _content;

        public bool IsAttached => _grid != null;

        /// <summary>
        /// Chart Trader's themed text brush (from its bid display). A light
        /// brush means Chart Trader runs a dark theme; a dark brush means a
        /// light theme. Null when not attached or undiscoverable.
        /// </summary>
        public Brush ThemedTextBrush { get; private set; }

        /// <summary>
        /// Appends a bottom row to Chart Trader's main grid and places
        /// <paramref name="content"/> in it. Returns false (without side
        /// effects) when Chart Trader is unavailable or collapsed — the caller
        /// is expected to retry later.
        /// </summary>
        public bool Attach(ChartTrader trader, FrameworkElement content)
        {
            if (IsAttached)  return true;
            if (trader == null) return false;
            if (trader.ChartTraderVisibility == ChartTraderVisibility.Collapsed) return false;

            Grid grid = trader.FindName("grdMain") as Grid ?? FindGrid(trader);
            if (grid == null) return false;

            ThemedTextBrush = (trader.FindName("tbBid") as TextBlock)?.Foreground;

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _rowIndex = grid.RowDefinitions.Count - 1;
            Grid.SetRow(content, _rowIndex);
            grid.Children.Add(content);

            _grid    = grid;
            _content = content;
            return true;
        }

        /// <summary>
        /// Removes the content and the row definition added by Attach.
        /// </summary>
        public void Detach()
        {
            if (_grid == null) return;

            if (_content != null && ReferenceEquals(_content.Parent, _grid))
                _grid.Children.Remove(_content);
            if (_rowIndex >= 0 && _rowIndex < _grid.RowDefinitions.Count)
                _grid.RowDefinitions.RemoveAt(_rowIndex);

            _grid    = null;
            _content = null;
            _rowIndex = -1;
            ThemedTextBrush = null;
        }

        private static Grid FindGrid(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Grid grid) return grid;
                Grid nested = FindGrid(child);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
