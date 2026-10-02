using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;


namespace FluentSensors.Features.Performance
{
    // the performance start view:
    // the PrimaryGraph of every hardware instance as a full SensorPanelControl tile in a SquareGridPanel, behind
    // the Hardware Overview button
    // a tile is one navigation target into its detail view, like the sidebar; the panel itself is read-only
    public sealed partial class PerformanceStartView : UserControl
    {
        // === constructor ===

        public PerformanceStartView()
        {
            InitializeComponent();
        }


        // === bindable properties ===

        public PerformanceViewModel ViewModel => PerformanceViewModel.Instance;


        // === event handlers ===

        // like NavItem_Click on PerformancePage
        private void Tile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is PerformanceNavItemViewModel item)
            {
                ViewModel.SelectedItem = item;
            }
        }
    }
}
