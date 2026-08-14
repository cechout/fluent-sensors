using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;


namespace FluentSensors.Features.Performance
{
    // start page for the Performance section; shows every hardware instances PrimaryGraph (the exact same sensor the
    // sidebar shows) as tiles in a SquareGridPanel, each rendered as a full SensorPanelControl instead of the sidebars
    // bare mini graph
    // reachable via the Home button in PerformancePages command bar; the tile header jumps into that hardwares detail
    // view, same as picking it in the sidebar
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

        // mirrors NavItem_Click on PerformancePage; jumps into the clicked tiles hardware detail view
        // only the header navigates, so the SensorPanelControls own controls (y-axis, threshold, color) stay usable
        private void Header_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is PerformanceNavItemViewModel item)
            {
                ViewModel.SelectedItem = item;
            }
        }
    }
}
