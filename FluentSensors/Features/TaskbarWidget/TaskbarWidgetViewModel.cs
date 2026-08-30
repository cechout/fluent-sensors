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
    // ViewModel managing pinned sensor graphs displayed in the taskbar widget and companion flyout
    // handles live sensor subscriptions, background pause/resume, and in-place collection reconciliation
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
                PinnedSensors.Add(new SensorGraphViewModel(sensor.Id, sensor.Name, sensor.SensorType, scope: SensorGraphScope.Taskbar));
            }
        }


        // === bindable properties ===

        public ObservableCollection<SensorGraphViewModel> PinnedSensors { get; set; }


        // === public methods ===

        // clears out sensors that are no longer selected, adds newly selected ones, and reorders to match selectedSensors exactly
        // existing unchanged sensors keep their history and are not recreated
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

            // add newly selected sensors that are not pinned yet; already-pinned sensors are left alone
            var existingIds = new HashSet<string>(PinnedSensors.Select(s => s.SensorId));
            foreach (var sensor in selectedSensors)
            {
                if (!existingIds.Contains(sensor.Id))
                {
                    PinnedSensors.Add(new SensorGraphViewModel(sensor.Id, sensor.Name, sensor.SensorType, scope: SensorGraphScope.Taskbar));
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

