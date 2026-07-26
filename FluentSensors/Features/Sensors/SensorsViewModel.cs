using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Core;
using FluentSensors.Features.Widget;
using FluentSensors.Persistence.Services;
using FluentSensors.Common.Sensors;


namespace FluentSensors.Features.Sensors
{
    public class SensorsViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private TaskCompletionSource<bool> _initialLoadTcs = new TaskCompletionSource<bool>();
        public Task WaitForInitialLoadAsync() => _initialLoadTcs.Task; // MainWindow waits on this


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
            HardwareGroups = new ObservableCollection<HardwareGroupViewModel>(); // initialize the empty list of hardware groups

            // this is the first access site that creates LhmHardwareTreeService (lazy singleton), since SensorsViewModel
            // itself is eager at splash screen; this is an accepted side effect, the tree service effectively also
            // runs from app start instead of only once the Performance page is first visited
            var tree = LhmHardwareTreeService.Instance;

            // process whatever the tree service already discovered before we subscribed, then track further discoveries live
            foreach (var instance in tree.HardwareGroups)
            {
                OnHardwareInstanceDiscovered(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;

            // covers the case where a widget auto-reopened (saved state) before this VM was constructed
            IsWidgetOpen = WidgetWindow.CurrentInstance != null;
            WidgetWindow.WidgetStateChanged += OnWidgetStateChanged;
        }


        // === bindable properties ===

        public ObservableCollection<HardwareGroupViewModel> HardwareGroups { get; set; }
        public bool HasHiddenSensors => HardwareGroups.Any(g => g.HasHiddenSensors);
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


        // === event handlers ===

        // reacts to newly discovered hardware instances (e.g. a GPU appearing for the first time)
        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                OnHardwareInstanceDiscovered(instance);
            }
        }

        // reacts to newly discovered sensors on an already-known hardware instance
        private void OnInstanceSensorsChanged(HardwareGroupViewModel group, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(group, entry);
            }
        }

        // relays a groups hidden-state change into our own aggregated properties
        private void Group_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HardwareGroupViewModel.HasHiddenSensors))
            {
                OnPropertyChanged(nameof(HasHiddenSensors));
            }
        }

        // keeps IsWidgetOpen in sync whenever the widget window opens or closes
        private void OnWidgetStateChanged()
        {
            IsWidgetOpen = WidgetWindow.CurrentInstance != null;
        }


        // === private helpers ===

        // creates the expander group for a newly discovered hardware instance, then processes its sensors
        // (already-known ones immediately, future ones reactively)
        private void OnHardwareInstanceDiscovered(LhmHardwareInstance instance)
        {
            var profile = HardwareGroupInfo.GetProfile(instance.Kind);

            var group = new HardwareGroupViewModel
            {
                HardwareName = instance.HardwareName,
                GroupLabel = profile.Label,
                IconGlyph = profile.IconGlyph
            };
            group.PropertyChanged += Group_PropertyChanged;
            HardwareGroups.Add(group);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(group, entry);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(group, e);
        }

        // creates and places the row for one newly discovered sensor; a sensor discovered for the first time this
        // session may already have persisted state from a previous run (e.g. it was hidden or selected before closing)
        private void OnSensorDiscovered(HardwareGroupViewModel group, LhmSensorEntry entry)
        {
            var persistedState = SensorStateService.Instance.GetState(entry.Id);
            bool isHidden = persistedState.IsHidden;

            // IsHidden must be set before Entry, and Entry before IsSelected:
            // Entry's setter does the initial value sync and skips it if IsHidden is already true; IsSelected's setter
            // persists immediately and needs Entry.Id to already be available
            var newRow = new SensorRowViewModel
            {
                SortOrder = group.Sensors.Count + group.HiddenSensors.Count,
                IsHidden = isHidden,
                Entry = entry,
                IsSelected = persistedState.IsSelected,
            };

            if (isHidden)
            {
                // sensor was hidden before app was closed: block the backend from sending further values right away,
                // so no CPU cycles are wasted on a sensor the user does not want to see
                HardwareMonitorService.Instance.AddExcludedSensor(entry.Id);
            }

            group.AddDiscoveredSensor(newRow, isHidden);

            // signalize that the first sensor has been successfully processed
            if (!_initialLoadTcs.Task.IsCompleted && HardwareGroups.Count > 0)
            {
                HardwareGroups[0].IsExpanded = true;
                _initialLoadTcs.SetResult(true);
            }
        }


        // === public methods ===

        // hides every currently selected sensor, across all hardware groups at once
        public void HideSelectedSensors()
        {
            foreach (var group in HardwareGroups)
            {
                group.HideSelectedSensors();
            }
        }
        // restores every currently selected hidden sensor, across all hardware groups at once
        public void RestoreSelectedHiddenSensors()
        {
            foreach (var group in HardwareGroups)
            {
                group.RestoreSelectedHiddenSensors();
            }
        }

        // sets the checkbox exactly on the sensors currently pinned to the active widget window
        // all other visible sensors get deselected so the checkbox state mirrors the widget contents 1:1
        public void SelectPinnedSensors()
        {
            var widgetViewModel = WidgetWindow.CurrentInstance?.ViewModel;
            if (widgetViewModel == null) return; // widget is closed, nothing to sync against

            var pinnedIds = new HashSet<string>(widgetViewModel.PinnedSensors.Select(s => s.SensorId));

            foreach (var group in HardwareGroups)
            {
                foreach (var sensor in group.Sensors)
                {
                    // a sensor that got hidden after being pinned still lingers in the widgets PinnedSensors list
                    // (it just stops receiving updates); never select it back, no matter which mode hid it
                    sensor.IsSelected = !sensor.IsHidden && pinnedIds.Contains(sensor.Id);
                }
            }
        }

        // clears every checkbox in the main sensor list
        // hidden sensors are untouched because they live in their own window with their own selection scope
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


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}