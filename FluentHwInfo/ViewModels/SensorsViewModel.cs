using FluentHwInfo.Services;
using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace FluentHwInfo.ViewModels
{
    public class SensorsViewModel : INotifyPropertyChanged
    {
        // fields
        public ObservableCollection<HardwareGroupViewModel> HardwareGroups { get; set; }
        private HardwareMonitorService _service; // create the HardwareMonitorService object
        // the problem:
        // as the HardwareDataUpdated event is triggered from the background thread, it is also recieved by the 
        // ViewModel here on the very same background thread. However, our ObservableCollection "SensorList" is directly
        // bound to the UI, which means it can be only updated from the main ui thread
        // any write access outside the main ui thread imemediately results in a cross thread exeption and crashes
        // the solution:
        // the dispatcher queue acts as a secure mailbox to the ui thread. It recieves the data packet from the background
        // thread and safely enqueues it onto the main ui thread, which then processes the update
        // As a result, the UI thread ultimately updates the SensorList with the recieved package from the subscribed
        // HardwareMonitorService event, when it has time to do so
        private DispatcherQueue _dispatcherQueue;
        private TaskCompletionSource<bool> _initialLoadTcs = new TaskCompletionSource<bool>();
        public Task WaitForInitialLoadAsync() => _initialLoadTcs.Task; // MainWindow waits on this
        private static SensorsViewModel _instance; // SensorsViewModel is a singleton
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

        // hidden sensors
        public bool HasHiddenSensors => HardwareGroups.Any(g => g.HasHiddenSensors);


        // constructor
        private SensorsViewModel()
        {
            HardwareGroups = new ObservableCollection<HardwareGroupViewModel>(); // initialize the empty list of hardware groups
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread(); // grab the ui threads dispatcher queue at startup

            // HardwareMonitorService
            _service = HardwareMonitorService.Instance; // get HardwareMonitorService instance
            _service.HardwareDataUpdated += OnHardwareDataUpdated; // subscribe to the master event
        }


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
                        var newRow = new SensorRowViewModel
                        {
                            Id = data.Id,
                            Name = data.Name,
                            SensorType = data.SensorType,
                            SortOrder = existingGroup.Sensors.Count + existingGroup.HiddenSensors.Count,
                        };

                        newRow.UpdateValue(data.Value);
                        existingGroup.Sensors.Add(newRow);
                    }
                }

                // signalize that the first data batch has been successfully processed
                if (!_initialLoadTcs.Task.IsCompleted && HardwareGroups.Count > 0)
                {
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


        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
