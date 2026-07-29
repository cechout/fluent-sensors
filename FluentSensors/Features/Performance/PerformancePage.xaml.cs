using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;

using FluentSensors.Core.StaticInfo;
using FluentSensors.Common.Sensors;
using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance
{
    public sealed partial class PerformancePage : Page
    {
        // === fields

        public PerformanceViewModel ViewModel => PerformanceViewModel.Instance;

        // cpu static info (draft)
        // each property re-reads WinStaticInfoService.Instance.Cpu; cheap after the very first access (singleton
        // already constructed by then)
        private string CpuPhysicalCoresText => WinStaticInfoService.Instance.Cpu.PhysicalCores.ToString();
        private string CpuLogicalProcessorsText => WinStaticInfoService.Instance.Cpu.LogicalProcessors.ToString();
        private string CpuL2CacheText => FormatCacheSize(WinStaticInfoService.Instance.Cpu.L2CacheSizeKb);
        private string CpuL3CacheText => FormatCacheSize(WinStaticInfoService.Instance.Cpu.L3CacheSizeKb);
        private string CpuMaxClockText => $"{WinStaticInfoService.Instance.Cpu.MaxClockSpeedMhz} MHz";
        private string CpuSocketText => WinStaticInfoService.Instance.Cpu.SocketDesignation;
        private string CpuVirtualizationFirmwareText => FormatBool(WinStaticInfoService.Instance.Cpu.VirtualizationFirmwareEnabled);
        private string CpuVirtualizationExtensionsText => FormatBool(WinStaticInfoService.Instance.Cpu.VirtualizationExtensionsSupported);
        private string CpuCoreTopologyText => FormatCoreTopology(WinStaticInfoService.Instance.Cpu);
        private static string FormatCacheSize(int cacheSizeKb) => cacheSizeKb > 0 ? $"{cacheSizeKb} KB" : "-";
        private static string FormatBool(bool value) => value ? "Yes" : "No";
        private static string FormatCoreTopology(WinCpuInfo cpu)
        {
            int smtCores = cpu.CoreTopology.Count(c => c.HasSmt);
            int efficiencyClasses = cpu.CoreTopology.Select(c => c.EfficiencyClass).Distinct().Count();
            return $"{cpu.CoreTopology.Count} cores read, {smtCores} with SMT, {efficiencyClasses} efficiency class(es)";
        }


        // === constructor ===

        public PerformancePage()
        {
            InitializeComponent();
        }


        // === private helpers ===

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