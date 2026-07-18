using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using FluentHwInfo.Common;
using FluentHwInfo.Features.Sensors;
using FluentHwInfo.Core;


namespace FluentHwInfo.Features.Widget
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
                PinnedSensors.Add(new SensorGraphViewModel(sensor.Id, sensor.Name));
            }
        }


        // === bindable properties ===

        // this list contains all the sensors that the user has pinned
        public ObservableCollection<SensorGraphViewModel> PinnedSensors { get; set; }


        // === event handlers ===

        // event handler invoked by the HardwareMonitorService at the configured polling interval
        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            // The HardwareMonitorService executes on a background thread
            // UI updates must be marshaled back to the main UI thread via DispatcherQueue
            // to prevent System.UnauthorizedAccessException
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