using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using FluentSensors.Controls.SensorRow;
using FluentSensors.Core;
using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Widget
{
    public class WidgetViewModel
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;


        // === constructor ===

        public WidgetViewModel(List<SensorRowViewModel> selectedSensors) // accept the injected list from the View layer
        {
            PinnedSensors = new ObservableCollection<SensorGraphViewModel>();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // subscribe to the HardwareDataUpdated event of the HardwareMonitorService
            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;

            // Dynamically instantiate chart components based on the precise hardware IDs
            foreach (var sensor in selectedSensors)
            {
                PinnedSensors.Add(new SensorGraphViewModel(sensor.Id, sensor.Name, sensor.SensorType));
            }
        }


        // === bindable properties ===

        // this list contains all the sensors that the user has pinned
        public ObservableCollection<SensorGraphViewModel> PinnedSensors { get; set; }


        // === public methods ===

        // clears out sensors that are no longer selected, adds newly selected ones, and reorders the result to exactly match
        // selectedSensors order
        public void Reconfigure(List<SensorRowViewModel> selectedSensors)
        {
            var newIds = new HashSet<string>(selectedSensors.Select(s => s.Id));

            // remove sensors that are no longer part of the selection
            for (int i = PinnedSensors.Count - 1; i >= 0; i--)
            {
                if (!newIds.Contains(PinnedSensors[i].SensorId))
                {
                    PinnedSensors[i].Cleanup();
                    PinnedSensors.RemoveAt(i);
                }
            }

            // add newly selected sensors that are not pinned yet; already-pinned sensors are deliberately left alone
            var existingIds = new HashSet<string>(PinnedSensors.Select(s => s.SensorId));
            foreach (var sensor in selectedSensors)
            {
                if (!existingIds.Contains(sensor.Id))
                {
                    PinnedSensors.Add(new SensorGraphViewModel(sensor.Id, sensor.Name, sensor.SensorType));
                }
            }

            // reorder to match selectedSensors exactly, moving existing items into place instead of recreating them
            for (int targetIndex = 0; targetIndex < selectedSensors.Count; targetIndex++)
            {
                string id = selectedSensors[targetIndex].Id;

                int currentIndex = -1;
                for (int j = targetIndex; j < PinnedSensors.Count; j++)
                {
                    if (PinnedSensors[j].SensorId == id)
                    {
                        currentIndex = j;
                        break;
                    }
                }

                if (currentIndex != -1 && currentIndex != targetIndex)
                {
                    PinnedSensors.Move(currentIndex, targetIndex);
                }
            }
        }


        // === event handlers ===

        // event handler invoked by the HardwareMonitorService at the configured polling interval
        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            // The HardwareMonitorService executes on a background thread UI updates must be marshaled back to the main UI
            // thread via DispatcherQueue to prevent System.UnauthorizedAccessException
            _dispatcherQueue.TryEnqueue(() =>
            {
                // we go through all pinned sensors and try to find their real counterparts in the HardwareMonitorService's sensor list
                foreach (var pinnedSensor in PinnedSensors)
                {
                    // query the incoming payload list for the matching sensor ID
                    var realSensor = payload.FirstOrDefault(s => s.Id == pinnedSensor.SensorId);

                    if (realSensor != null)
                    {
                        // determine the correct unit string based on the sensor type
                        string unit = GetUnitString(realSensor.SensorType);

                        // push the updated value and the formatted string to the individual sensor view model
                        pinnedSensor.AddDataPoint(realSensor.Value, $"{realSensor.Value:F1} {unit}");
                    }
                }
            });
        }

        // helper method to append the correct physical unit to the UI text block
        private string GetUnitString(string sensorType)
        {
            return sensorType switch
            {
                "Power" => "W",
                "Temperature" => "°C",
                "Load" => "%",
                "Clock" => "MHz",
                "Data" => "GB",
                "SmallData" => "MB",
                "Fan" => "RPM",
                "Voltage" => "V",
                "Throughput" => "MB/s",
                _ => ""
            };
        }


        // === public methods ===

        // unsubscribe from the global event when the view is closed
        public void Cleanup()
        {
            HardwareMonitorService.Instance.HardwareDataUpdated -= OnHardwareDataUpdated;

            foreach (var sensor in PinnedSensors)
            {
                sensor.Cleanup();
            }
        }
    }
}