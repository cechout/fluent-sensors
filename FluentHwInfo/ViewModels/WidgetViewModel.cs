using FluentHwInfo.Services;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FluentHwInfo.ViewModels
{
    public class WidgetViewModel
    {
        // this list contains all the sensors that the user has pinned
        public ObservableCollection<WidgetSensorViewModel> PinnedSensors { get; set; }

        private readonly DispatcherQueue _dispatcherQueue;

        public WidgetViewModel()
        {
            PinnedSensors = new ObservableCollection<WidgetSensorViewModel>();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // subscribe to the HardwareDataUpdated event of the HardwareMonitorService
            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;

            // test
            PinnedSensors.Add(new WidgetSensorViewModel("CPU Package"));
            PinnedSensors.Add(new WidgetSensorViewModel("GPU Temperature"));
        }

        // Event handler invoked by the HardwareMonitorService at the configured polling interval
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
                    // Query the incoming payload list for the matching sensor name
                    // For production, matching by SensorData.Id is recommended over Name
                    var realSensor = payload.FirstOrDefault(s => s.Name == pinnedSensor.SensorName);

                    if (realSensor != null)
                    {
                        // determine the correct unit string based on the sensor type
                        string unit = GetUnitString(realSensor.SensorType);

                        // push the updated value and the formatted string to the individual sensor view model
                        pinnedSensor.AddDataPoint(realSensor.Value, $"{realSensor.Value:F1} {unit}");
                    }


                    // test simulation
                    //Random rnd = new Random();
                    //double fakeValue = rnd.Next(40, 80) + rnd.NextDouble();
                    //pinnedSensor.AddDataPoint(fakeValue, $"{fakeValue:F1} W");
                }
            });
        }

        // Helper method to append the correct physical unit to the UI text block.
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

        // Unsubscribe from the global event when the view is closed
        // Failing to detach this event handler will result in a memory leak
        public void Cleanup()
        {
            HardwareMonitorService.Instance.HardwareDataUpdated -= OnHardwareDataUpdated;
        }
    }
}