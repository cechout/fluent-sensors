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

            // a nav item has nothing to rebuild when the hardware icon colour setting flips, the whole list is
            // simply told to re-read its brush
            // never detached: this view model is created once and stays alive for the rest of the apps lifetime
            SettingsService.Instance.HardwareIconColorsChanged += RefreshNavItemIconBrushes;

            // the event carries no value and fires for either time range, so both captions re-read theirs
            SettingsService.Instance.PerformanceGraphTimeSpanChanged += () =>
            {
                OnPropertyChanged(nameof(StandardGraphTimeSpanText));
                OnPropertyChanged(nameof(ExtendedGraphTimeSpanText));
            };

            // every category follows the exact same discovery pattern:
            // process instances that already exist (likely true for all of them, since LhmHardwareTreeService runs from
            // app start), then keep listening for future ones
            // getPrimaryGraph picks each Kinds "at a glance" utilization sensor, shown in the sidebar and the
            // start page; it hands over a whole chain rather than one sensor, see FirstAvailable
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

            // the longest chain by far: an Intel iGPU can report a single Power sensor and nothing else, no core
            // load, no temperature, not even a D3D engine counter
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

                // start-page <-> hardware-view transitions flip IsHardwareViewActive, which gates whether the sidebar
                // and info panel are allowed to show at all
                OnPropertyChanged(nameof(IsNavSidebarShown));
                OnPropertyChanged(nameof(NavSidebarVisibility));
                OnPropertyChanged(nameof(NavSidebarColumnMinWidth));
                OnPropertyChanged(nameof(NavSidebarColumnMaxWidth));
                OnPropertyChanged(nameof(InfoPanelVisibility));
                OnPropertyChanged(nameof(InfoPanelColumnMinWidth));
                OnPropertyChanged(nameof(InfoPanelColumnMaxWidth));
            }
        }

        // current effective theme
        // Resolved from the actually applied ActualTheme, not the raw AppTheme setting
        // Kept in sync by PerformancePage hooking its own ActualThemeChanged
        // Single source of truth for anything on this page that needs a different resource depending on the real
        // light/dark state
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

        // pre-computed Visibility for the per-hardware info panel; avoids a function binding inside each detail
        // views XAML
        // forced Collapsed on the start page: the info panel describes one specific hardware, so it only shows next
        // to a hardware view, even while its command-bar toggle stays checked (see IsHardwareViewActive)
        public Visibility InfoPanelVisibility => IsInfoPanelVisible && IsHardwareViewActive ? Visibility.Visible : Visibility.Collapsed;

        // nav sidebar (hardware selection list), toggled independently from the info panel; both can be open at once
        // above the width threshold, exclusive below it
        // Threshold handling lives in PerformancePage itself, this is just the raw on/off state
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

        // the zero-width column hides the sidebar but leaves its buttons in the tab order, unseen; the sidebar list
        // binds its IsEnabled to this, so tab passes over it while it is not shown
        public bool IsNavSidebarShown => IsNavSidebarVisible && IsHardwareViewActive;

        private bool IsInfoPanelShown => IsInfoPanelVisible && IsHardwareViewActive;

        // the splitters sit outside the column they resize, so each one is hidden on its own; the info panel splitter
        // uses InfoPanelVisibility above
        public Visibility NavSidebarVisibility => IsNavSidebarShown ? Visibility.Visible : Visibility.Collapsed;

        // pre-computed width limits for the two toggleable columns (nav sidebar on PerformancePage, info panel on each
        // detail view); their widths belong to the GridSplitter the user drags
        // a hidden panel gets zero for both, which collapses its column to a real zero size so it stops reserving
        // layout space, while the dragged width stays on the column for when the panel comes back
        // both are additionally gated on IsHardwareViewActive: on the start page neither column is shown, no matter
        // what the toggles say
        public double NavSidebarColumnMinWidth => IsNavSidebarShown ? _navSidebarMinWidth : 0;
        public double NavSidebarColumnMaxWidth => IsNavSidebarShown ? _navSidebarMaxWidth : 0;
        public double InfoPanelColumnMinWidth => IsInfoPanelShown ? _infoPanelMinWidth : 0;
        public double InfoPanelColumnMaxWidth => IsInfoPanelShown ? _infoPanelMaxWidth : 0;

        // the limits themselves, handed in by PerformancePage, which holds the values
        private double _navSidebarMinWidth;
        private double _navSidebarMaxWidth = double.PositiveInfinity;
        private double _infoPanelMinWidth;
        private double _infoPanelMaxWidth = double.PositiveInfinity;

        // a maximum below its minimum, on a page too narrow for it, falls back to the minimum
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

        // info panel width, shared by every detail view so switching hardware keeps it; the splitter of whichever
        // view was dragged hands it over once the drag ends
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

        // graph time range captions under the graphs of every hardware view, written like the settings page lists
        // the ranges
        public string StandardGraphTimeSpanText => $"Last {PerformanceGraphDefaults.StandardTimeSpanSeconds:0}s";
        public string ExtendedGraphTimeSpanText => $"Last {PerformanceGraphDefaults.ExtendedTimeSpanSeconds:0}s";

        // true while a specific hardwares detail view is shown, false on the start page (SelectedItem null)
        // the nav sidebar and info panel only make sense next to a hardware view, so both stay collapsed on the start
        // page even while their command-bar toggles remain checked
        private bool IsHardwareViewActive => SelectedItem != null;


        // === private helpers ===

        // first sensor of the chain this hardware actually reports; the natural first pick can be missing entirely
        // on hardware LHM only partially supports (an Intel iGPU that reports GPU Power and nothing else), which
        // left the sidebar row and the start page tile showing an empty graph frame
        // re-evaluated on every change of the instance, so the preferred sensor takes over as soon as it shows up
        private static SensorGraphViewModel FirstAvailable(params SensorGraphViewModel[] graphs)
        {
            foreach (var graph in graphs)
            {
                if (graph != null) return graph;
            }
            return null;
        }

        // public because the theme side is driven by the page, which is what owns the applied ActualTheme
        public void RefreshNavItemIconBrushes()
        {
            foreach (var item in NavItems)
            {
                item.RefreshIconBrush();
            }
        }

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
