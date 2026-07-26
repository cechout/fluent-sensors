using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FluentSensors.Common.Sensors;
using FluentSensors.Features.Performance.Lhm;

namespace FluentSensors.Features.Performance
{
    public sealed partial class PerformancePage : Page
    {
        public PerformanceViewModel ViewModel => PerformanceViewModel.Instance;

        public PerformancePage()
        {
            InitializeComponent();
        }

        // shows the CPU detail block only while a CPU nav item is selected
        // (the same one-method-per-Kind pattern will be added for Ram/Gpu/Storage/Network) 
        private Visibility ShowIfCpuSelected(PerformanceNavItemViewModel item) =>
            item != null && item.Kind == HardwareGroupKind.Cpu ? Visibility.Visible : Visibility.Collapsed;

        // resolves the currently selected nav item's Target to its CPU instance, or null if a non-CPU item
        // (or nothing) is selected; used both for the ContentControl binding and the two click handlers below
        private LhmCpuInstanceViewModel GetSelectedCpu(PerformanceNavItemViewModel item) => item?.Target as LhmCpuInstanceViewModel;

        private void ShowOverall_Click(object sender, RoutedEventArgs e)
        {
            var cpu = GetSelectedCpu(ViewModel.SelectedItem);
            if (cpu != null) cpu.IsShowingAllThreads = false;
        }

        private void ShowAllThreads_Click(object sender, RoutedEventArgs e)
        {
            var cpu = GetSelectedCpu(ViewModel.SelectedItem);
            if (cpu != null) cpu.IsShowingAllThreads = true;
        }

        // sidebar selection: every nav item button shares this one handler, the clicked items own DataContext (set by
        // the ItemTemplate) tells us which PerformanceNavItemViewModel was chosen
        private void NavItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PerformanceNavItemViewModel item)
            {
                ViewModel.SelectedItem = item;
            }
        }
    }
}