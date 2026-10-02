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
    // the taskbar widget view model:
    // the pinned graphs of the widget and, as a second set, of its flyout, with the live subscription, pause and
    // in-place reconciliation
    // two sets, since a graph view model owns its time span; one view model for both, so a window rebuild hands the
    // flyout history and its snapshot over with the rest
    public class TaskbarWidgetViewModel
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;
        private bool _isLiveDataActive = true;


        // === constructor ===

        public TaskbarWidgetViewModel(List<SensorRowViewModel> selectedSensors)
        {
            PinnedSensors = new ObservableCollection<SensorGraphViewModel>();
            FlyoutSensors = new ObservableCollection<SensorGraphViewModel>();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;

            foreach (var sensor in selectedSensors)
            {
                PinnedSensors.Add(CreateGraph(sensor, SensorGraphScope.Taskbar));
                FlyoutSensors.Add(CreateGraph(sensor, SensorGraphScope.TaskbarFlyout));
            }
        }


        // === bindable properties ===

        public ObservableCollection<SensorGraphViewModel> PinnedSensors { get; set; }

        // the same sensors in the same order, on the flyout time range
        public ObservableCollection<SensorGraphViewModel> FlyoutSensors { get; }

        // the flyout snapshot; not persisted, a closed widget ends it
        public bool IsFlyoutPaused { get; private set; }


        // === public methods ===

        // removes, adds and reorders both sets to match selectedSensors; a kept sensor keeps its history
        public void Reconfigure(List<SensorRowViewModel> selectedSensors)
        {
            Reconcile(PinnedSensors, selectedSensors, SensorGraphScope.Taskbar);
            Reconcile(FlyoutSensors, selectedSensors, SensorGraphScope.TaskbarFlyout);
        }

        // against the settings and the live SystemAccentColor; one call serves the widget and the flyout
        public void RefreshGraphColors()
        {
            foreach (var sensor in AllGraphs())
            {
                sensor.RefreshGraphColor();
            }
        }

        // resumes from a reset baseline, pauses with a cleared history and without a flyout snapshot
        public void SetLiveDataActive(bool active)
        {
            if (_isLiveDataActive == active) return;
            _isLiveDataActive = active;

            if (active)
            {
                foreach (var sensor in AllGraphs())
                {
                    sensor.ResetToBaseline();
                }
                HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;
            }
            else
            {
                HardwareMonitorService.Instance.HardwareDataUpdated -= OnHardwareDataUpdated;
                SetFlyoutPaused(false);
                foreach (var sensor in AllGraphs())
                {
                    sensor.ClearHistory();
                }
            }
        }

        // freezes or thaws every flyout graph; the taskbar graphs always run on
        public void SetFlyoutPaused(bool paused)
        {
            IsFlyoutPaused = paused;

            foreach (var sensor in FlyoutSensors)
            {
                sensor.SetFrozen(paused);
            }
        }


        // === event handlers ===

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var pinnedSensor in AllGraphs())
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

            foreach (var sensor in AllGraphs())
            {
                sensor.Cleanup();
            }
        }


        // === private helpers ===

        private static SensorGraphViewModel CreateGraph(SensorRowViewModel sensor, SensorGraphScope scope)
        {
            return new SensorGraphViewModel(sensor.Id, sensor.Name, sensor.SensorType, scope: scope, hardwareKind: sensor.HardwareKind);
        }

        private IEnumerable<SensorGraphViewModel> AllGraphs() => PinnedSensors.Concat(FlyoutSensors);

        // a new flyout graph joins a running snapshot frozen
        private void Reconcile(ObservableCollection<SensorGraphViewModel> graphs, List<SensorRowViewModel> selectedSensors, SensorGraphScope scope)
        {
            var newIds = new HashSet<string>(selectedSensors.Select(s => s.Id));

            for (int i = graphs.Count - 1; i >= 0; i--)
            {
                if (!newIds.Contains(graphs[i].SensorId))
                {
                    graphs[i].Cleanup();
                    graphs.RemoveAt(i);
                }
            }

            var existingIds = new HashSet<string>(graphs.Select(s => s.SensorId));
            foreach (var sensor in selectedSensors)
            {
                if (!existingIds.Contains(sensor.Id))
                {
                    var graph = CreateGraph(sensor, scope);
                    if (scope == SensorGraphScope.TaskbarFlyout) graph.SetFrozen(IsFlyoutPaused);
                    graphs.Add(graph);
                }
            }

            // moved into place, not recreated
            for (int targetIndex = 0; targetIndex < selectedSensors.Count; targetIndex++)
            {
                string id = selectedSensors[targetIndex].Id;

                int currentIndex = -1;
                for (int j = targetIndex; j < graphs.Count; j++)
                {
                    if (graphs[j].SensorId == id)
                    {
                        currentIndex = j;
                        break;
                    }
                }

                if (currentIndex != -1 && currentIndex != targetIndex)
                {
                    graphs.Move(currentIndex, targetIndex);
                }
            }
        }
    }
}

