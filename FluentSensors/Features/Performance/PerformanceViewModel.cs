using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Features.Performance.Lhm;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;


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
        // NavigationCacheMode="Enabled" on PerformancePage then keeps this instance alive and bound for the rest of the
        // apps lifetime once created
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

            // every category follows the exact same discovery pattern:
            // process instances that already exist (likely true for all of them, since LhmHardwareTreeService runs from
            // app start), then keep listening for future ones
            // getPrimaryGraph picks each Kinds "at a glance" utilization sensor, shown in the sidebar (and later the
            // start page)
            AttachExistingAndFuture(Cpu.Cpus, HardwareGroupKind.Cpu,
                item => ((LhmCpuInstanceViewModel)item).HardwareName,
                item => ((LhmCpuInstanceViewModel)item).TotalLoad);

            AttachExistingAndFuture(Memory.Memories, HardwareGroupKind.Ram,
                item => ((LhmMemoryInstanceViewModel)item).HardwareName,
                item => ((LhmMemoryInstanceViewModel)item).Used);

            AttachExistingAndFuture(Gpu.Gpus, HardwareGroupKind.Gpu,
                item => ((LhmGpuInstanceViewModel)item).HardwareName,
                item => ((LhmGpuInstanceViewModel)item).CoreLoad);

            AttachExistingAndFuture(Storage.Drives, HardwareGroupKind.Storage,
                item => ((LhmStorageInstanceViewModel)item).HardwareName,
                item => ((LhmStorageInstanceViewModel)item).TotalActivity);

            AttachExistingAndFuture(Network.Adapters, HardwareGroupKind.Network,
                item => ((LhmNetworkInstanceViewModel)item).HardwareName,
                item => ((LhmNetworkInstanceViewModel)item).DownloadSpeed);

            SelectedItem = NavItems.FirstOrDefault(i => i.Kind == HardwareGroupKind.Cpu) ?? NavItems.FirstOrDefault();
        }


        // === bindable properties ===

        public LhmCpuPerformanceViewModel Cpu { get; }
        public LhmGpuPerformanceViewModel Gpu { get; }
        public LhmMemoryPerformanceViewModel Memory { get; }
        public LhmStoragePerformanceViewModel Storage { get; }
        public LhmNetworkPerformanceViewModel Network { get; }

        // one entry per selectable hardware instance, shown in the sidebar
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

        // processes hardware instances discovered before this ViewModel existed, then keeps listening for future
        // ones; every category (Cpu/Ram/Gpu/Storage/Network) goes through this exact same path
        private void AttachExistingAndFuture(IEnumerable collection, HardwareGroupKind kind,
            Func<object, string> getHardwareName, Func<object, SensorGraphViewModel> getPrimaryGraph)
        {
            string groupLabel = HardwareGroupInfo.GetProfile(kind).Label;

            foreach (var item in collection)
            {
                NavItems.Add(new PerformanceNavItemViewModel(kind, groupLabel, getHardwareName(item), item, getPrimaryGraph));
            }

            ((INotifyCollectionChanged)collection).CollectionChanged += (s, e) => OnHardwareCollectionChanged(e, kind, getHardwareName, getPrimaryGraph);
        }

        private void OnHardwareCollectionChanged(NotifyCollectionChangedEventArgs e, HardwareGroupKind kind,
            Func<object, string> getHardwareName, Func<object, SensorGraphViewModel> getPrimaryGraph)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            string groupLabel = HardwareGroupInfo.GetProfile(kind).Label;

            foreach (var newItem in e.NewItems)
            {
                NavItems.Add(new PerformanceNavItemViewModel(kind, groupLabel, getHardwareName(newItem), newItem, getPrimaryGraph));
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