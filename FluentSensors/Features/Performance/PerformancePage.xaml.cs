using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentSensors.Features.Performance
{
    public sealed partial class PerformancePage : Page
    {
        public PerformanceViewModel ViewModel => PerformanceViewModel.Instance;

        public PerformancePage()
        {
            InitializeComponent();
        }

        // x:Bind function bindings, used instead of a separate IValueConverter class for this simple case
        private Visibility ShowIfTrue(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
        private Visibility ShowIfFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

        // shows the CPU detail block only while a CPU nav item is selected; the same one-method-per-Kind
        // pattern will be added for Ram/Gpu/Storage/Network as their detail blocks get built
        private Visibility ShowIfCpuSelected(PerformanceNavItemViewModel item) =>
            item != null && item.Kind == PerformanceNavItemKind.Cpu ? Visibility.Visible : Visibility.Collapsed;

        private void ShowOverall_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Cpu.IsShowingAllThreads = false;
        }

        private void ShowAllThreads_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Cpu.IsShowingAllThreads = true;
        }

        // sidebar selection: every nav item button shares this one handler, the clicked item's own
        // DataContext (set by the ItemTemplate) tells us which PerformanceNavItemViewModel was chosen
        private void NavItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PerformanceNavItemViewModel item)
            {
                ViewModel.SelectedItem = item;
            }
        }
    }
}