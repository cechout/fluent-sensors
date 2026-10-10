using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Core;
using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Widget
{
    public class WidgetViewModel
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;

        // fed by the live data stream; true from the constructor, SetLiveDataActive toggles it for a closed widget
        private bool _isLiveDataActive = true;


        // === constructor ===

        public WidgetViewModel(List<SensorRowViewModel> selectedSensors, double timeSpanSeconds)
        {
            PinnedSensors = new ObservableCollection<SensorGraphViewModel>();
            TimeSpanSeconds = timeSpanSeconds;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;

            // one graph per selected sensor
            foreach (var sensor in selectedSensors)
            {
                PinnedSensors.Add(CreateGraph(sensor));
            }
        }


        // === bindable properties ===

        // the pinned sensors
        public ObservableCollection<SensorGraphViewModel> PinnedSensors { get; set; }

        // the snapshot; not persisted, a closed widget ends it
        public bool IsPaused { get; private set; }

        // the time range of this window; every graph takes it as its own, never the setting
        public double TimeSpanSeconds { get; private set; }


        // === public methods ===

        // drops deselected sensors, adds new ones and reorders to match selectedSensors
        public void Reconfigure(List<SensorRowViewModel> selectedSensors)
        {
            var newIds = new HashSet<string>(selectedSensors.Select(s => s.Id));

            // deselected ones
            for (int i = PinnedSensors.Count - 1; i >= 0; i--)
            {
                if (!newIds.Contains(PinnedSensors[i].SensorId))
                {
                    PinnedSensors[i].Cleanup();
                    PinnedSensors.RemoveAt(i);
                }
            }

            // new ones; pinned ones stay as they are
            var existingIds = new HashSet<string>(PinnedSensors.Select(s => s.SensorId));
            foreach (var sensor in selectedSensors)
            {
                if (!existingIds.Contains(sensor.Id))
                {
                    var graph = CreateGraph(sensor);
                    graph.SetFrozen(IsPaused); // joins a running snapshot frozen
                    PinnedSensors.Add(graph);
                }
            }

            // the order of selectedSensors, by moving, not recreating
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


        public void SetTimeSpan(double seconds)
        {
            if (TimeSpanSeconds == seconds) return;
            TimeSpanSeconds = seconds;

            foreach (var sensor in PinnedSensors)
            {
                sensor.ApplyViewOverrides(seconds, null, null);
            }
        }


        // every pinned graph color against the settings and the live accent
        public void RefreshGraphColors()
        {
            foreach (var sensor in PinnedSensors)
            {
                sensor.RefreshGraphColor();
            }
        }


        // decouples a closed widget from the live data, so nothing runs in the background (minimize keeps it):
        // off - unsubscribed, every history wiped
        // on - a flat baseline, subscribed again; the reopened widget starts fresh
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
                SetPaused(false);
                foreach (var sensor in PinnedSensors)
                {
                    sensor.ClearHistory();
                }
            }
        }


        // freezes or thaws every pinned graph
        public void SetPaused(bool paused)
        {
            IsPaused = paused;

            foreach (var sensor in PinnedSensors)
            {
                sensor.SetFrozen(paused);
            }
        }


        // === private helpers ===

        private SensorGraphViewModel CreateGraph(SensorRowViewModel sensor) =>
            new SensorGraphViewModel(sensor.Id, sensor.Name, sensor.SensorType, TimeSpanSeconds, hardwareKind: sensor.HardwareKind);


        // === event handlers ===

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            // raised on the polling thread
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

        // unused
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


        // === cleanup ===

        // when the view closes
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
