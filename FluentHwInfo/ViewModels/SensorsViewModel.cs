using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentHwInfo.Services;
using Microsoft.UI.Dispatching; // important for winui3 threading

namespace FluentHwInfo.ViewModels
{
    /// <summary>
    /// Serves as the top-level DataContext directly bound to the entire SensorsPage UI, acting as the outermost container in the 
    /// ViewModel hierarchy.
    /// 
    /// Responsibilities:
    /// - Initializes the HardwareMonitorService and listens to the master payload event.
    /// - Dynamically generates or updates nested HardwareGroupViewModels based on incoming data.
    /// - Uses the DispatcherQueue to safely marshal background telemetry data onto the UI thread.
    /// </summary>
    public class SensorsViewModel
    {
        // ObservableCollection is a smart list that automatically notifies the UI
        // when you add, remove or change items in the list
        // this list now holds GROUPS instead of single rows
        public ObservableCollection<HardwareGroupViewModel> HardwareGroups { get; set; }

        // here we create the HardwareMonitorService object
        private HardwareMonitorService _service;

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

        public SensorsViewModel()
        {
            // Initialize the new group list
            HardwareGroups = new ObservableCollection<HardwareGroupViewModel>();

            // grabs the UI thread directly at startup
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            _service = new HardwareMonitorService();

            // we subscribe to the one big master event from HardwareMonitorService
            _service.HardwareDataUpdated += OnHardwareDataUpdated;

            _service.StartMonitoring();
        }

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            // we safely push the UI updates onto the main thread
            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var data in payload)
                {
                    // 1. check if we already have a GROUP for this specific hardware (e.g. "Intel Core i9-12900H")
                    var existingGroup = HardwareGroups.FirstOrDefault(g => g.HardwareName == data.HardwareName);

                    // If the group doesn't exist yet, we dynamically create a new Expander group
                    if (existingGroup == null)
                    {
                        existingGroup = new HardwareGroupViewModel { HardwareName = data.HardwareName };
                        HardwareGroups.Add(existingGroup);
                    }

                    // 2. check if we already have a row for this specific sensor ID INSIDE this group
                    var existingRow = existingGroup.Sensors.FirstOrDefault(r => r.Id == data.Id);

                    if (existingRow != null)
                    {
                        // 3a. Row already exists -> just update the value
                        existingRow.UpdateValue(data.Value);
                    }
                    else
                    {
                        // 3b. Row does not exist yet -> we dynamically create a new one
                        var newRow = new SensorRowViewModel
                        {
                            Id = data.Id,
                            // We don't need the HardwareName here anymore, because the Expander Header already shows it!
                            Name = data.Name,
                            SensorType = data.SensorType,
                        };

                        newRow.UpdateValue(data.Value);

                        // Add to the group's internal list, the UI will now automatically render the new row inside the Expander
                        existingGroup.Sensors.Add(newRow);
                    }
                }
            });
        }
    }
}
