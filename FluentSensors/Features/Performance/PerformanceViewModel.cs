using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Features.Performance.Lhm;

namespace FluentSensors.Features.Performance
{
    // top-level data context for the single PerformancePage; orchestrates whichever engine-specific child view
    // models are active
    // LHM is the only engine for now, HWiNFO will sit alongside it later as its own set of child view models under a
    // separate namespace/folder, without touching the LHM properties here
    public class PerformanceViewModel : INotifyPropertyChanged
    {
        // === singleton instance ===

        // lazy on purpose (unlike SensorsViewModel.Instance):
        // only created the first time PerformancePage actually asks for it, so nobody pays the cost of these background
        // graphs running unless they visit the page
        // NavigationCacheMode="Enabled" on PerformancePage then keeps this instance alive and bound for the rest of the apps
        // lifetime once created
        private static PerformanceViewModel _instance;
        public static PerformanceViewModel Instance => _instance ??= new PerformanceViewModel();


        // === constructor ===

        private PerformanceViewModel()
        {
            Cpu = new LhmCpuPerformanceViewModel();
            Gpu = new LhmGpuPerformanceViewModel();
            Memory = new LhmMemoryPerformanceViewModel();
            Storage = new LhmStoragePerformanceViewModel();
            Network = new LhmNetworkPerformanceViewModel();

            NavItems = new ObservableCollection<PerformanceNavItemViewModel>();

            // Cpu and Ram always exist exactly once, regardless of hardware discovery timing; DisplayName
            // starts null and fills in once the underlying view model learns its HardwareName from the first
            // matching payload entry (see the PropertyChanged subscriptions below)
            var cpuNavItem = new PerformanceNavItemViewModel(PerformanceNavItemKind.Cpu, "CPU", Cpu.HardwareName, Cpu);
            var ramNavItem = new PerformanceNavItemViewModel(PerformanceNavItemKind.Ram, "RAM", Memory.HardwareName, Memory);
            NavItems.Add(cpuNavItem);
            NavItems.Add(ramNavItem);

            Cpu.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(LhmCpuPerformanceViewModel.HardwareName))
                {
                    cpuNavItem.DisplayName = Cpu.HardwareName;
                }
            };
            Memory.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(LhmMemoryPerformanceViewModel.HardwareName))
                {
                    ramNavItem.DisplayName = Memory.HardwareName;
                }
            };

            // Gpu/Storage/Network instances are only known once discovered at runtime, so their nav entries are
            // added reactively as those collections grow. Only Add is handled - this matches the existing
            // behavior of the underlying Lhm*PerformanceViewModel collections themselves, which also never
            // remove a once-discovered instance
            Gpu.Gpus.CollectionChanged += (s, e) => OnHardwareCollectionChanged(
                e, PerformanceNavItemKind.Gpu, "GPU", item => ((LhmGpuInstanceViewModel)item).HardwareName);

            Storage.Drives.CollectionChanged += (s, e) => OnHardwareCollectionChanged(
                e, PerformanceNavItemKind.Storage, "Storage", item => ((LhmStorageInstanceViewModel)item).HardwareName);

            Network.Adapters.CollectionChanged += (s, e) => OnHardwareCollectionChanged(
                e, PerformanceNavItemKind.Network, "Network", item => ((LhmNetworkInstanceViewModel)item).HardwareName);

            SelectedItem = cpuNavItem;
        }


        // === bindable properties ===

        public LhmCpuPerformanceViewModel Cpu { get; }
        public LhmGpuPerformanceViewModel Gpu { get; }
        public LhmMemoryPerformanceViewModel Memory { get; }
        public LhmStoragePerformanceViewModel Storage { get; }
        public LhmNetworkPerformanceViewModel Network { get; }

        // one entry per selectable hardware category/instance, shown in the sidebar
        public ObservableCollection<PerformanceNavItemViewModel> NavItems { get; }

        private PerformanceNavItemViewModel _selectedItem;
        public PerformanceNavItemViewModel SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem == value) return;

                if (_selectedItem != null) _selectedItem.IsSelected = false;
                _selectedItem = value;
                if (_selectedItem != null) _selectedItem.IsSelected = true;

                OnPropertyChanged();
            }
        }


        // === private helpers ===

        private void OnHardwareCollectionChanged(NotifyCollectionChangedEventArgs e, PerformanceNavItemKind kind,
            string groupLabel, Func<object, string> getHardwareName)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (var newItem in e.NewItems)
            {
                NavItems.Add(new PerformanceNavItemViewModel(kind, groupLabel, getHardwareName(newItem), newItem));
            }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}