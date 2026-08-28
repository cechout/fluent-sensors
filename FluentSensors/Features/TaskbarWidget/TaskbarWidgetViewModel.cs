using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Core;


namespace FluentSensors.Features.TaskbarWidget
{
    public class TaskbarWidgetViewModel
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;
        private bool _isLiveDataActive = true;


        // === constructor ===

        public TaskbarWidgetViewModel(List<SensorRowViewModel> selectedSensors)
        {
            PinnedSensors = new ObservableCollection<SensorGraphViewModel>();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;

            foreach (var sensor in selectedSensors)
            {
                PinnedSensors.Add(new SensorGraphViewModel(sensor.Id, sensor.Name, sensor.SensorType));
            }
        }


        // === bindable properties ===

        public ObservableCollection<SensorGraphViewModel> PinnedSensors { get; set; }


        // === public methods ===

        // syncs pinned sensors with updated selection without recreating existing graphs
        public void Reconfigure(List<SensorRowViewModel> selectedSensors)
        {
            var newIds = new HashSet<string>(selectedSensors.Select(s => s.Id));

            // remove unselected sensors
            for (int i = PinnedSensors.Count - 1; i >= 0; i--)
            {
                if (!newIds.Contains(PinnedSensors[i].SensorId))
                {
                    PinnedSensors[i].Cleanup();
                    PinnedSensors.RemoveAt(i);
                }
            }

            // add newly selected sensors
            var existingIds = new HashSet<string>(PinnedSensors.Select(s => s.SensorId));
            foreach (var sensor in selectedSensors)
            {
                if (!existingIds.Contains(sensor.Id))
                {
                    PinnedSensors.Add(new SensorGraphViewModel(sensor.Id, sensor.Name, sensor.SensorType));
                }
            }

            // reorder to match selection order
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

        // pauses or resumes live data subscription and resets baseline
        public void SetLiveDataActive(bool active)
        {
            if (_isLiveDataActive == active) return;
            _isLiveDataActive = active;

            if (active)
            {
                foreach (var sensor in PinnedSensors)
                {
                    sensor.ResetToBaseline();
                }
                HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;
            }
            else
            {
                HardwareMonitorService.Instance.HardwareDataUpdated -= OnHardwareDataUpdated;
                foreach (var sensor in PinnedSensors)
                {
                    sensor.ClearHistory();
                }
            }
        }


        // === event handlers ===

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var pinnedSensor in PinnedSensors)
                {
                    var realSensor = payload.FirstOrDefault(s => s.Id == pinnedSensor.SensorId);

                    if (realSensor != null)
                    {
                        pinnedSensor.AddDataPoint(realSensor.Value, SensorUnitFormatter.Format(realSensor.Value, realSensor.SensorType));
                    }
                }
            });
        }


        // === cleanup ===

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
