using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // battery discovery:
    // one LhmBatteryInstanceViewModel per battery in LhmHardwareTreeService, every raw LHM sensor parsed into its
    // property; the instance stays a data holder
    public class LhmBatteryPerformanceViewModel
    {
        // === constructor ===

        public LhmBatteryPerformanceViewModel()
        {
            Batteries = new ObservableCollection<LhmBatteryInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (instance.Kind == HardwareGroupKind.Battery) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        public ObservableCollection<LhmBatteryInstanceViewModel> Batteries { get; }


        // === event handlers ===

        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                foreach (LhmHardwareInstance instance in e.OldItems)
                {
                    if (instance.Kind == HardwareGroupKind.Battery) DetachFromInstance(instance);
                }
                return;
            }

            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                if (instance.Kind == HardwareGroupKind.Battery) AttachToInstance(instance);
            }
        }


        // === private helpers ===

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            var battery = new LhmBatteryInstanceViewModel(instance.HardwareName);
            Batteries.Add(battery);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(battery, entry);
            }
            ApplyDetailFallback(battery);
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(battery, e);
        }

        // the hardware left; the battery leaves the page, a new one is built when it is back
        private void DetachFromInstance(LhmHardwareInstance instance)
        {
            var battery = Batteries.FirstOrDefault(item => item.HardwareName == instance.HardwareName);
            if (battery != null) Batteries.Remove(battery);
        }

        // after every sensor batch: without a saved choice the IsDefault candidate (the voltage) beats discovery order;
        // with nothing active, the first candidate
        private static void ApplyDetailFallback(LhmBatteryInstanceViewModel battery)
        {
            if (battery.DetailOptions.Count == 0) return;

            if (SensorSwitchStateService.Instance.GetSelectedSensorId(battery.HardwareName, "Detail") == null)
            {
                var flaggedDefault = battery.DetailOptions.FirstOrDefault(c => c.IsDefault);
                if (flaggedDefault != null && battery.Detail?.SensorId != flaggedDefault.SensorId)
                {
                    battery.SetDetailWithoutPersisting(flaggedDefault.Resolve());
                    return;
                }
            }

            if (battery.Detail == null) battery.SetDetailWithoutPersisting(battery.DetailOptions[0].Resolve());
        }

        private void OnInstanceSensorsChanged(LhmBatteryInstanceViewModel battery, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(battery, entry);
            }
            ApplyDetailFallback(battery);
        }

        // by sensor type first; LHM renames the rate and the current with the direction, so their names cannot match
        private void OnSensorDiscovered(LhmBatteryInstanceViewModel battery, LhmSensorEntry entry)
        {
            switch (entry.SensorType, entry.Name)
            {
                case ("Level", "Charge Level"):
                    AttachGraph(entry, graph => battery.ChargeLevel = graph);
                    break;

                case ("Power", _):
                    AttachGraph(entry, graph => battery.Rate = graph);
                    break;

                case ("Voltage", _):
                    RegisterDetailCandidate(battery, entry, isDefault: true);
                    break;

                case ("Current", _):
                case ("TimeSpan", _):
                case ("Temperature", _):
                case ("Energy", "Remaining Capacity"):
                    RegisterDetailCandidate(battery, entry);
                    break;

                // designed and full capacity, degradation, and whatever a later LHM adds
                default:
                    AttachFact(battery, entry);
                    break;
            }
        }

        // a graph in a fixed place; it follows the name LHM gives the sensor and leaves its place while LHM has the
        // sensor deactivated
        private static void AttachGraph(LhmSensorEntry entry, Action<SensorGraphViewModel> setGraph)
        {
            var graph = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
            PushDataPoint(graph, entry);

            entry.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(LhmSensorEntry.Value): PushDataPoint(graph, entry); break;
                    case nameof(LhmSensorEntry.Name): graph.SensorName = entry.Name; break;
                    case nameof(LhmSensorEntry.IsActive): setGraph(entry.IsActive ? graph : null); break;
                }
            };

            setGraph(entry.IsActive ? graph : null);
        }

        // adds a candidate, active when nothing is yet and it is the saved choice (or none was saved, first found;
        // ApplyDetailFallback corrects to the flagged default)
        private static void RegisterDetailCandidate(LhmBatteryInstanceViewModel battery, LhmSensorEntry entry, bool isDefault = false)
        {
            SensorGraphViewModel cached = null;
            SensorGraphViewModel Resolve()
            {
                if (cached != null) return cached;
                cached = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(cached, entry);
                entry.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(LhmSensorEntry.Value)) PushDataPoint(cached, entry);
                    else if (e.PropertyName == nameof(LhmSensorEntry.Name)) cached.SensorName = entry.Name;
                };
                return cached;
            }

            battery.DetailOptions.Add(new SensorSwitchCandidate(entry.Id, entry.Name, Resolve, isDefault));

            if (battery.Detail != null) return; // resolved, one more alternative

            string persistedId = SensorSwitchStateService.Instance.GetSelectedSensorId(battery.HardwareName, "Detail");
            if (persistedId == entry.Id || persistedId == null)
            {
                battery.SetDetailWithoutPersisting(Resolve());
            }
        }

        private static void AttachFact(LhmBatteryInstanceViewModel battery, LhmSensorEntry entry)
        {
            var fact = new LhmBatteryFact(entry.Name)
            {
                ValueText = SensorUnitFormatter.Format(entry.Value, entry.SensorType)
            };
            battery.Facts.Add(fact);

            entry.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;
                fact.ValueText = SensorUnitFormatter.Format(entry.Value, entry.SensorType);
            };
        }

        private static void PushDataPoint(SensorGraphViewModel graph, LhmSensorEntry entry)
        {
            graph.AddDataPoint(entry.Value, SensorUnitFormatter.Format(entry.Value, entry.SensorType));
        }
    }
}
