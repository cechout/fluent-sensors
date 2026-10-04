using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Features.Performance.Lhm;
using FluentSensors.Persistence.Services;
using Microsoft.UI.Xaml;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;


namespace FluentSensors.Features.Performance
{
    // the performance view model:
    // the data context of PerformancePage over the engine view models; LHM is the only engine (a HWiNFO set would sit
    // beside it in its own folder)
    public class PerformanceViewModel : INotifyPropertyChanged
    {
        // === singleton instance ===

        // lazy on purpose (unlike SensorsViewModel.Instance), so the background graphs only run once the page is
        // visited; it then lives for the session
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

            // the nav items only re-read their brush on an icon colour change; never
            // detached, this lives for the session
            SettingsService.Instance.HardwareIconColorsChanged += RefreshNavItemIconBrushes;

            // no value, any range; all pickers re-read
            SettingsService.Instance.PerformanceGraphTimeSpanChanged += () =>
            {
                OnPropertyChanged(nameof(StandardGraphTimeSpanSeconds));
                OnPropertyChanged(nameof(CpuExtendedGraphTimeSpanSeconds));
                OnPropertyChanged(nameof(GpuExtendedGraphTimeSpanSeconds));
            };

            // every category the same way: the existing instances, then later ones; getPrimaryGraph picks the
            // at-a-glance sensor of the sidebar and the start view, as a chain (see FirstAvailable)
            AttachExistingAndFuture(Cpu.Cpus, HardwareGroupKind.Cpu,
                item => ((LhmCpuInstanceViewModel)item).HardwareName,
                item =>
                {
                    var cpu = (LhmCpuInstanceViewModel)item;
                    return FirstAvailable(cpu.TotalLoad, cpu.MaxTemperature, cpu.PackagePower);
                });

            AttachExistingAndFuture(Memory.Memories, HardwareGroupKind.Ram,
                item => ((LhmMemoryInstanceViewModel)item).PerformanceDisplayName,
                item =>
                {
                    var memory = (LhmMemoryInstanceViewModel)item;
                    return FirstAvailable(memory.Used, memory.Available, memory.VirtualMemoryUsed);
                });

            // the longest chain: an Intel iGPU can report a single Power sensor and nothing else
            AttachExistingAndFuture(Gpu.Gpus, HardwareGroupKind.Gpu,
                item => ((LhmGpuInstanceViewModel)item).HardwareName,
                item =>
                {
                    var gpu = (LhmGpuInstanceViewModel)item;
                    return FirstAvailable(gpu.CoreLoad, gpu.D3dEngineSlot1, gpu.Temperature, gpu.PackagePower, gpu.MemoryUsed);
                });

            AttachExistingAndFuture(Storage.Drives, HardwareGroupKind.Storage,
                item => ((LhmStorageInstanceViewModel)item).PerformanceDisplayName,
                item =>
                {
                    var drive = (LhmStorageInstanceViewModel)item;
                    return FirstAvailable(drive.TotalActivity, drive.ReadRate, drive.WriteRate);
                });

            AttachExistingAndFuture(Network.Adapters, HardwareGroupKind.Network,
                item => ((LhmNetworkInstanceViewModel)item).PerformanceDisplayName,
                item =>
                {
                    var adapter = (LhmNetworkInstanceViewModel)item;
                    return FirstAvailable(adapter.NetworkUtilization, adapter.DownloadSpeed, adapter.UploadSpeed);
                });

            SelectedItem = NavItems.FirstOrDefault(i => i.Kind == HardwareGroupKind.Cpu) ?? NavItems.FirstOrDefault();
        }


        // === bindable properties ===

        public LhmCpuPerformanceViewModel Cpu { get; }
        public LhmGpuPerformanceViewModel Gpu { get; }
        public LhmMemoryPerformanceViewModel Memory { get; }
        public LhmStoragePerformanceViewModel Storage { get; }
        public LhmNetworkPerformanceViewModel Network { get; }

        // one per hardware instance, in the sidebar
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

                // start page and hardware view flip IsHardwareViewActive, which gates sidebar and info panel
                OnPropertyChanged(nameof(IsNavSidebarShown));
                OnPropertyChanged(nameof(NavSidebarVisibility));
                OnPropertyChanged(nameof(NavSidebarColumnMinWidth));
                OnPropertyChanged(nameof(NavSidebarColumnMaxWidth));
                OnPropertyChanged(nameof(InfoPanelVisibility));
                OnPropertyChanged(nameof(InfoPanelColumnMinWidth));
                OnPropertyChanged(nameof(InfoPanelColumnMaxWidth));
            }
        }

        // the applied theme, from ActualTheme rather than the setting (PerformancePage keeps it in sync);
        // the one source for this page
        private bool _isDarkTheme;
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (_isDarkTheme != value)
                {
                    _isDarkTheme = value;
                    OnPropertyChanged();
                }
            }
        }

        // info panel
        private bool _isInfoPanelVisible = false;
        public bool IsInfoPanelVisible
        {
            get => _isInfoPanelVisible;
            set
            {
                if (_isInfoPanelVisible == value) return;
                _isInfoPanelVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InfoPanelVisibility));
                OnPropertyChanged(nameof(InfoPanelColumnMinWidth));
                OnPropertyChanged(nameof(InfoPanelColumnMaxWidth));
            }
        }

        // pre-computed, no function binding in each detail view; collapsed on the start page whatever the toggle
        // says (see IsHardwareViewActive)
        public Visibility InfoPanelVisibility => IsInfoPanelVisible && IsHardwareViewActive ? Visibility.Visible : Visibility.Collapsed;

        // the nav sidebar, toggled apart from the info panel; both open above the width threshold, exclusive below
        // (PerformancePage handles that, this is the raw state)
        private bool _isNavSidebarVisible = true;
        public bool IsNavSidebarVisible
        {
            get => _isNavSidebarVisible;
            set
            {
                if (_isNavSidebarVisible == value) return;
                _isNavSidebarVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNavSidebarShown));
                OnPropertyChanged(nameof(NavSidebarVisibility));
                OnPropertyChanged(nameof(NavSidebarColumnMinWidth));
                OnPropertyChanged(nameof(NavSidebarColumnMaxWidth));
            }
        }

        // the zero-width column hides the sidebar but not its tab stops; its IsEnabled binds this
        public bool IsNavSidebarShown => IsNavSidebarVisible && IsHardwareViewActive;

        private bool IsInfoPanelShown => IsInfoPanelVisible && IsHardwareViewActive;

        // the splitters sit outside their column and hide on their own (the info panel one uses InfoPanelVisibility)
        public Visibility NavSidebarVisibility => IsNavSidebarShown ? Visibility.Visible : Visibility.Collapsed;

        // width limits of the two toggleable columns, whose widths belong to the splitter; a hidden panel gets zero for
        // both and keeps its dragged width for its return (never on the start page)
        public double NavSidebarColumnMinWidth => IsNavSidebarShown ? _navSidebarMinWidth : 0;
        public double NavSidebarColumnMaxWidth => IsNavSidebarShown ? _navSidebarMaxWidth : 0;
        public double InfoPanelColumnMinWidth => IsInfoPanelShown ? _infoPanelMinWidth : 0;
        public double InfoPanelColumnMaxWidth => IsInfoPanelShown ? _infoPanelMaxWidth : 0;

        // handed in by PerformancePage
        private double _navSidebarMinWidth;
        private double _navSidebarMaxWidth = double.PositiveInfinity;
        private double _infoPanelMinWidth;
        private double _infoPanelMaxWidth = double.PositiveInfinity;

        // a maximum below the minimum (a narrow page) falls back to the minimum
        public void SetSidePanelLimits(double navSidebarMinWidth, double navSidebarMaxWidth, double infoPanelMinWidth, double infoPanelMaxWidth)
        {
            _navSidebarMinWidth = navSidebarMinWidth;
            _navSidebarMaxWidth = Math.Max(navSidebarMinWidth, navSidebarMaxWidth);
            _infoPanelMinWidth = infoPanelMinWidth;
            _infoPanelMaxWidth = Math.Max(infoPanelMinWidth, infoPanelMaxWidth);

            OnPropertyChanged(nameof(NavSidebarColumnMinWidth));
            OnPropertyChanged(nameof(NavSidebarColumnMaxWidth));
            OnPropertyChanged(nameof(InfoPanelColumnMinWidth));
            OnPropertyChanged(nameof(InfoPanelColumnMaxWidth));
        }

        // shared by every detail view, so a switch keeps it; the dragged splitter hands it over at the end
        private double _infoPanelWidth;
        public double InfoPanelWidth
        {
            get => _infoPanelWidth;
            set
            {
                if (_infoPanelWidth == value) return;
                _infoPanelWidth = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InfoPanelColumnWidth));
            }
        }

        public GridLength InfoPanelColumnWidth => new GridLength(InfoPanelWidth);

        // the time range pickers under the graphs; a pick is the setting itself
        public double StandardGraphTimeSpanSeconds
        {
            get => PerformanceGraphDefaults.StandardTimeSpanSeconds;
            set => SettingsService.Instance.PerformanceGraphTimeSpanSeconds = value;
        }

        public double CpuExtendedGraphTimeSpanSeconds
        {
            get => PerformanceGraphDefaults.CpuExtendedTimeSpanSeconds;
            set => SettingsService.Instance.PerformanceCpuExtendedGraphTimeSpanSeconds = value;
        }

        public double GpuExtendedGraphTimeSpanSeconds
        {
            get => PerformanceGraphDefaults.GpuExtendedTimeSpanSeconds;
            set => SettingsService.Instance.PerformanceGpuExtendedGraphTimeSpanSeconds = value;
        }

        // a detail view is shown (false on the start page); sidebar and info panel need one, whatever the toggles say
        private bool IsHardwareViewActive => SelectedItem != null;


        // === private helpers ===

        // the first sensor of the chain the hardware reports (partly supported hardware can lack the natural pick);
        // re-evaluated on every change, so the preferred one takes over once it shows up
        private static SensorGraphViewModel FirstAvailable(params SensorGraphViewModel[] graphs)
        {
            foreach (var graph in graphs)
            {
                if (graph != null) return graph;
            }
            return null;
        }

        // public; the page owns the applied theme
        public void RefreshNavItemIconBrushes()
        {
            foreach (var item in NavItems)
            {
                item.RefreshIconBrush();
            }
        }

        // the instances found before this view model existed, then the later ones; every category takes this path
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
