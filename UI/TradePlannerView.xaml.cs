using System.Windows;

namespace TradeAssistant.UI
{
    /// <summary>
    /// Minimal code-behind for the floating trade planner window.
    /// All logic lives in TradePlannerViewModel.
    /// </summary>
    public partial class TradePlannerView : Window
    {
        public TradePlannerView(TradePlannerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            // Notify ViewModel of closure so it can unsubscribe events
            (DataContext as TradePlannerViewModel)?.Dispose();
        }
    }
}
