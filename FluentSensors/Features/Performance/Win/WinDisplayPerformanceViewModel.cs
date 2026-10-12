using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;


namespace FluentSensors.Features.Performance.Win
{
    // display discovery:
    // one WinDisplayInstanceViewModel per display in the hardware tree, every sensor of the Windows source parsed into
    // its property; the instance stays a data holder
    // the tree is the LHM one: the Windows source feeds the same payload, so a display is an instance like any other
    public class WinDisplayPerformanceViewModel
    {
        // === constructor ===

        public WinDisplayPerformanceViewModel()
        {
            Displays = new ObservableCollection<WinDisplayInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (instance.Kind == HardwareGroupKind.Display) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        public ObservableCollection<WinDisplayInstanceViewModel> Displays { get; }


        // === event handlers ===

        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                foreach (LhmHardwareInstance instance in e.OldItems)
                {
                    if (instance.Kind == HardwareGroupKind.Display) DetachFromInstance(instance);
                }
                return;
            }

            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                if (instance.Kind == HardwareGroupKind.Display) AttachToInstance(instance);
            }
        }


        // === private helpers ===

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            var display = new WinDisplayInstanceViewModel(instance.HardwareName);
            Displays.Add(display);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(display, entry);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(display, e);
        }

        // the display left (unplugged, or its switch turned off); a new one is built when it is back
        private void DetachFromInstance(LhmHardwareInstance instance)
        {
            var display = Displays.FirstOrDefault(item => item.HardwareName == instance.HardwareName);
            if (display == null) return;

            Displays.Remove(display);
            display.Detach();
        }

        private void OnInstanceSensorsChanged(WinDisplayInstanceViewModel display, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(display, entry);
            }
        }

        // the names WinSensorSource gives them
        private static void OnSensorDiscovered(WinDisplayInstanceViewModel display, LhmSensorEntry entry)
        {
            switch (entry.Name)
            {
                case "Brightness":
                    AttachGraph(entry, graph => display.Brightness = graph);
                    break;

                case "Refresh Rate":
                    AttachGraph(entry, graph => display.RefreshRate = graph);
                    break;
            }
        }

        // a graph in a fixed place; it leaves its place while the source has no value for the sensor
        private static void AttachGraph(LhmSensorEntry entry, Action<SensorGraphViewModel> setGraph)
        {
            var graph = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
            PushDataPoint(graph, entry);

            entry.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(LhmSensorEntry.Value): PushDataPoint(graph, entry); break;
                    case nameof(LhmSensorEntry.IsActive): setGraph(entry.IsActive ? graph : null); break;
                }
            };

            setGraph(entry.IsActive ? graph : null);
        }

        private static void PushDataPoint(SensorGraphViewModel graph, LhmSensorEntry entry)
        {
            graph.AddDataPoint(entry.Value, SensorUnitFormatter.Format(entry.Value, entry.SensorType));
        }
    }
}
