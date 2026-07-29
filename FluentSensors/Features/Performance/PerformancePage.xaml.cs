using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;


namespace FluentSensors.Features.Performance
{
    public sealed partial class PerformancePage : Page
    {
        // === fields ===

        public PerformanceViewModel ViewModel => PerformanceViewModel.Instance;


        // === constructor ===

        public PerformancePage()
        {
            InitializeComponent();
        }


        // === event handlers ===

        // sidebar selection: every nav item button shares this one handler, the clicked items own DataContext tells
        // which PerformanceNavItemViewModel was chosen
        private void NavItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PerformanceNavItemViewModel item)
            {
                ViewModel.SelectedItem = item;
            }
        }
    }
}