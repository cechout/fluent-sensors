using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using FluentSensors.Controls.SensorRow;
using FluentSensors.Core;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Features.Widget;
using FluentSensors.Persistence.Services;
using FluentSensors.Common.Sensors;
using FluentSensors.Core.Lhm;
using FluentSensors.Features.TaskbarWidget;


namespace FluentSensors.Features.Sensors
{
    public class SensorsViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private TaskCompletionSource<bool> _initialLoadTcs = new TaskCompletionSource<bool>();
        public Task WaitForInitialLoadAsync() => _initialLoadTcs.Task; // MainWindow waits on this

        // keeps OnSensorRowSelectionChanged quiet while ResyncCheckboxesForActiveProfile mirrors a saved selection
        private bool _isResyncingCheckboxes = false;

        // when the min, max and avg columns started; app start, then the last Reset Stats
        private DateTime _statsStartedUtc = DateTime.UtcNow;


        // === singleton instance ===

        private static SensorsViewModel _instance;
        public static SensorsViewModel Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SensorsViewModel();
                }
                return _instance;
            }
        }


        // === constructor ===

        private SensorsViewModel()
        {
            HardwareGroups = new ObservableCollection<HardwareGroupViewModel>();

            // the first access creates the lazy LhmHardwareTreeService; since this view model is built at the splash,
            // the tree runs from app start
            var tree = LhmHardwareTreeService.Instance;

            // what the tree already found, then every later discovery
            foreach (var instance in tree.HardwareGroups)
            {
                OnHardwareInstanceDiscovered(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;

            // a widget may have reopened from its saved state before this view model existed
            IsWidgetOpen = WidgetWindow.CurrentInstance != null;
            IsTaskbarWidgetOpen = TaskbarWidgetWindow.CurrentInstance != null;
            WidgetWindow.WidgetStateChanged += OnWidgetStateChanged;
            TaskbarWidgetWindow.WidgetStateChanged += OnWidgetStateChanged;

            // never detached; this view model lives for the whole session
            SettingsService.Instance.HardwareIconColorsChanged += RefreshGroupIconBrushes;
        }


        // === bindable properties ===

        public ObservableCollection<HardwareGroupViewModel> HardwareGroups { get; set; }
        public bool HasHiddenSensors => HardwareGroups.Any(g => g.HasHiddenSensors);

        // the profile the checkboxes reflect and persist to
        private SensorSelectionProfile _activeProfile = SensorSelectionProfile.WidgetWindow;
        public SensorSelectionProfile ActiveProfile
        {
            get => _activeProfile;
            set
            {
                if (_activeProfile == value) return;
                _activeProfile = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWidgetProfileActive));
                OnPropertyChanged(nameof(IsCsvProfileActive));
                OnPropertyChanged(nameof(IsTaskbarProfileActive));
                OnPropertyChanged(nameof(IsPinnedAvailable));
                ResyncCheckboxesForActiveProfile();
            }
        }
        public bool IsWidgetProfileActive => ActiveProfile == SensorSelectionProfile.WidgetWindow;
        public bool IsCsvProfileActive => ActiveProfile == SensorSelectionProfile.Csv;
        public bool IsTaskbarProfileActive => ActiveProfile == SensorSelectionProfile.Taskbar;

        public bool IsPinnedAvailable => ActiveProfile switch
        {
            SensorSelectionProfile.WidgetWindow => WidgetWindow.CurrentInstance != null,
            SensorSelectionProfile.Taskbar => TaskbarWidgetWindow.CurrentInstance != null && TaskbarWidgetWindow.CurrentInstance.IsEmbedded,
            _ => false
        };

        private bool _isWidgetOpen;
        public bool IsWidgetOpen
        {
            get => _isWidgetOpen;
            private set
            {
                if (_isWidgetOpen != value)
                {
                    _isWidgetOpen = value;
                    OnPropertyChanged();
                }
            }
        }

        // the taskbar commit button reads "Update Taskbar" and the close button shows while it is open
        private bool _isTaskbarWidgetOpen;
        public bool IsTaskbarWidgetOpen
        {
            get => _isTaskbarWidgetOpen;
            private set
            {
                if (_isTaskbarWidgetOpen != value)
                {
                    _isTaskbarWidgetOpen = value;
                    OnPropertyChanged();
                }
            }
        }

        // how long the stats have been collecting; the bottom bar
        private string _statsElapsedText = "0:00:00";
        public string StatsElapsedText
        {
            get => _statsElapsedText;
            private set
            {
                if (_statsElapsedText != value)
                {
                    _statsElapsedText = value;
                    OnPropertyChanged();
                }
            }
        }


        // === event handlers ===

        // newly discovered hardware instances
        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                OnHardwareInstanceDiscovered(instance);
            }
        }

        // newly discovered sensors on a known instance
        private void OnInstanceSensorsChanged(HardwareGroupViewModel group, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(group, entry);
            }
        }

        // a group hidden-state change into the aggregate
        private void Group_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HardwareGroupViewModel.HasHiddenSensors))
            {
                OnPropertyChanged(nameof(HasHiddenSensors));
            }
        }

        // IsWidgetOpen, IsTaskbarWidgetOpen and IsPinnedAvailable follow the widget and the taskbar widget
        private void OnWidgetStateChanged()
        {
            IsWidgetOpen = WidgetWindow.CurrentInstance != null;
            IsTaskbarWidgetOpen = TaskbarWidgetWindow.CurrentInstance != null;
            OnPropertyChanged(nameof(IsPinnedAvailable));
        }

        // persists every real toggle on the active profile, a data write only (the action buttons open things); hidden
        // sensors are left out, their checkbox is the restore selection of the hidden sensors window
        private void OnSensorRowSelectionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SensorRowViewModel.IsSelected)) return;
            if (_isResyncingCheckboxes) return;
            if (sender is not SensorRowViewModel row || row.IsHidden) return;

            SensorSelectionService.Instance.SetMembership(ActiveProfile, row.Id, row.IsSelected);
        }


        // === private helpers ===

        // re-resolves the group header icons after the icon colour setting or the theme moved; public, the sensors page
        // and the hidden sensors window (same groups) drive the theme side
        public void RefreshGroupIconBrushes()
        {
            foreach (var group in HardwareGroups)
            {
                group.RefreshIconBrush();
            }
        }

        // the expander group of a new instance, with its sensors now and later
        private void OnHardwareInstanceDiscovered(LhmHardwareInstance instance)
        {
            var profile = HardwareGroupInfo.GetProfile(instance.Kind);

            var group = new HardwareGroupViewModel
            {
                HardwareName = GetDisplayName(instance),
                LhmHardwareName = instance.HardwareName,
                GroupLabel = profile.Label,
                IconGlyph = GetIconGlyph(instance),
                Kind = instance.Kind
            };
            group.PropertyChanged += Group_PropertyChanged;
            HardwareGroups.Add(group);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(group, entry);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(group, e);
        }

        // display only: storage and network show the matched model or adapter description (like PerformanceViewModel),
        // which reads better than the LHM name
        private static string GetDisplayName(LhmHardwareInstance instance)
        {
            switch (instance.Kind)
            {
                case HardwareGroupKind.Storage:
                    var drive = HardwareNameMatcher.FindBestMatch(
                        instance.HardwareName,
                        WinStaticInfoService.Instance.Drives,
                        d => d.FriendlyName);
                    return drive?.FriendlyName ?? instance.HardwareName;

                case HardwareGroupKind.Network:
                    var adapter = HardwareNameMatcher.FindBestMatch(
                        instance.HardwareName,
                        WinStaticInfoService.Instance.NetworkAdapters,
                        a => a.Name);
                    return adapter?.Description ?? instance.HardwareName;

                default:
                    return instance.HardwareName;
            }
        }

        // the category glyph; a wireless adapter gets its own, matched like GetDisplayName
        private static string GetIconGlyph(LhmHardwareInstance instance)
        {
            if (instance.Kind != HardwareGroupKind.Network) return HardwareGroupInfo.GetProfile(instance.Kind).IconGlyph;

            var adapter = HardwareNameMatcher.FindBestMatch(
                instance.HardwareName,
                WinStaticInfoService.Instance.NetworkAdapters,
                a => a.Name);
            return HardwareGroupInfo.GetNetworkIconGlyph(adapter?.InterfaceType);
        }

        // mirrors the saved selection of the active profile onto every visible checkbox
        // hidden sensors stay off; (HideSensorsCompletely=false leaves a soft-hidden sensor in group.Sensors)
        private void ResyncCheckboxesForActiveProfile()
        {
            _isResyncingCheckboxes = true;

            foreach (var group in HardwareGroups)
            {
                foreach (var sensor in group.Sensors)
                {
                    sensor.IsSelected = !sensor.IsHidden && SensorSelectionService.Instance.IsSelected(ActiveProfile, sensor.Id);
                }
            }

            _isResyncingCheckboxes = false;
        }

        // the row of a newly discovered sensor; (it may carry state from an earlier run, hidden or selected)
        private void OnSensorDiscovered(HardwareGroupViewModel group, LhmSensorEntry entry)
        {
            var persistedState = SensorStateService.Instance.GetState(entry.Id);
            bool isHidden = persistedState.IsHidden;

            // IsHidden, then Entry, then IsSelected: the Entry setter syncs the value unless hidden, the IsSelected
            // setter persists and needs Entry.Id
            // the checkbox seeds from the active profile selection, not from persistedState.IsSelected
            var newRow = new SensorRowViewModel
            {
                SortOrder = group.Sensors.Count + group.HiddenSensors.Count,
                HardwareKind = group.Kind,
                IsHidden = isHidden,
                Entry = entry,
                IsSelected = !isHidden && SensorSelectionService.Instance.IsSelected(ActiveProfile, entry.Id),
            };
            newRow.PropertyChanged += OnSensorRowSelectionChanged;

            if (isHidden)
            {
                // hidden in an earlier run; registered as excluded (the skip itself is off, see
                // HardwareMonitorService.LoopAsync)
                HardwareMonitorService.Instance.AddExcludedSensor(entry.Id);
            }

            group.AddDiscoveredSensor(newRow, isHidden);

            // the first sensor is in; MainWindow may go on
            if (!_initialLoadTcs.Task.IsCompleted && HardwareGroups.Count > 0)
            {
                HardwareGroups[0].IsExpanded = true;
                _initialLoadTcs.SetResult(true);
            }
        }


        // === public methods ===

        // every selected sensor, across all groups
        public void HideSelectedSensors()
        {
            foreach (var group in HardwareGroups)
            {
                group.HideSelectedSensors();
            }
        }
        // every selected hidden sensor, across all groups
        public void RestoreSelectedHiddenSensors()
        {
            foreach (var group in HardwareGroups)
            {
                group.RestoreSelectedHiddenSensors();
            }
        }

        // checks exactly the sensors pinned to the active widget or taskbar widget, every other visible one goes off
        public void SelectPinnedSensors()
        {
            HashSet<string> pinnedIds = null;

            if (ActiveProfile == SensorSelectionProfile.Taskbar)
            {
                var taskbarViewModel = TaskbarWidgetWindow.CurrentInstance?.ViewModel;
                if (taskbarViewModel == null) return;
                pinnedIds = new HashSet<string>(taskbarViewModel.PinnedSensors.Select(s => s.SensorId));
            }
            else if (ActiveProfile == SensorSelectionProfile.WidgetWindow)
            {
                var widgetViewModel = WidgetWindow.CurrentInstance?.ViewModel;
                if (widgetViewModel == null) return;
                pinnedIds = new HashSet<string>(widgetViewModel.PinnedSensors.Select(s => s.SensorId));
            }
            else
            {
                return;
            }

            foreach (var group in HardwareGroups)
            {
                foreach (var sensor in group.Sensors)
                {
                    // a sensor hidden after pinning lingers in PinnedSensors; never selected back
                    sensor.IsSelected = !sensor.IsHidden && pinnedIds.Contains(sensor.Id);
                }
            }
        }

        // clears the main list; hidden sensors keep the selection of their own window
        public void DeselectAllSensors()
        {
            foreach (var group in HardwareGroups)
            {
                foreach (var sensor in group.Sensors)
                {
                    sensor.IsSelected = false;
                }
            }
        }

        // the elapsed readout from zero, with Reset Stats
        public void RestartStatsElapsed()
        {
            _statsStartedUtc = DateTime.UtcNow;
            RefreshStatsElapsed();
        }

        // formatted like the start page uptime, 2:14:37 or 1d 2:14:37; called more often than once a second (see
        // SensorsPage.StatsElapsedTimerInterval), the property only moves with the shown second
        public void RefreshStatsElapsed()
        {
            TimeSpan elapsed = DateTime.UtcNow - _statsStartedUtc;

            string clock = $"{elapsed.Hours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            StatsElapsedText = elapsed.Days > 0 ? $"{elapsed.Days}d {clock}" : clock;
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
