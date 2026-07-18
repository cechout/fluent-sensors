using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using FluentHwInfo.Features.Widget;
using FluentHwInfo.Persistence.Services;
using FluentHwInfo.Core;


namespace FluentHwInfo.Features.Sensors
{
    public class SensorsViewModel : INotifyPropertyChanged
    {
        // === fields ===
        
        private HardwareMonitorService _service; 
        private DispatcherQueue _dispatcherQueue;
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
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread(); // grab the ui threads dispatcher queue at startup

            // HardwareMonitorService
            _service = HardwareMonitorService.Instance; // get HardwareMonitorService instance
            _service.HardwareDataUpdated += OnHardwareDataUpdated; // subscribe to the master event

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

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            // safely push the UI updates onto the main thread
            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var data in payload)
                {
                    // check if there is already a group for this specific hardware (e.g. "Intel Core i9-12900H")
                    var existingGroup = HardwareGroups.FirstOrDefault(g => g.HardwareName == data.HardwareName);

                    // if the group doesnt exist yet, we dynamically create a new expander group
                    if (existingGroup == null)
                    {
                        existingGroup = new HardwareGroupViewModel { HardwareName = data.HardwareName };
                        existingGroup.PropertyChanged += Group_PropertyChanged;
                        HardwareGroups.Add(existingGroup);
                    }

                    // check if we already have a row for this specific sensor ID inside this group (visible or hidden)
                    var existingRow = existingGroup.Sensors.FirstOrDefault(r => r.Id == data.Id)
                        ?? existingGroup.HiddenSensors.FirstOrDefault(r => r.Id == data.Id);

                    if (existingRow != null)
                    {
                        // hidden and disabled sensors never show live values anywhere, so skip updating them entirely
                        if (!existingRow.IsHidden)
                        {
                            existingRow.UpdateValue(data.Value);
                        }
                    }
                    else
                    {
                        // row does not exist yet -> we dynamically create a new one
                        // a sensor discovered for the first time this session may already have persisted state from a
                        // previous run (e.g. it was hidden or selected before closing)
                        var persistedState = SensorStateService.Instance.GetState(data.Id);
                        bool isHidden = persistedState.IsHidden;
                        var newRow = new SensorRowViewModel
                        {
                            Id = data.Id,
                            Name = data.Name,
                            SensorType = data.SensorType,
                            SortOrder = existingGroup.Sensors.Count + existingGroup.HiddenSensors.Count,
                            IsHidden = isHidden,
                            IsSelected = persistedState.IsSelected,
                        };

                        if (isHidden)
                        {
                            // sensor was hidden before app was closed: block the backend from sending further values right away,
                            // so no CPU cycles are wasted on a sensor the user does not want to see
                            HardwareMonitorService.Instance.AddExcludedSensor(data.Id);
                        }
                        else
                        {
                            newRow.UpdateValue(data.Value);
                        }

                        existingGroup.AddDiscoveredSensor(newRow, isHidden);
                    }
                }

                // signalize that the first data batch has been successfully processed
                if (!_initialLoadTcs.Task.IsCompleted && HardwareGroups.Count > 0)
                {
                    HardwareGroups[0].IsExpanded = true;
                    _initialLoadTcs.SetResult(true);
                }
            });
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
