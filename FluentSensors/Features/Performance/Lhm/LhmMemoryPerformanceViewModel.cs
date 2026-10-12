using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // memory discovery:
    // the physical and the "Virtual Memory" instance merged into one LhmMemoryInstanceViewModel,
    // without a nav entry of its own
    // derives the y-max helpers RoundedTotalMemory and VirtualMemoryTotal, LHM has no total sensor
    // the module instances (one per DIMM whose SPD chip answers) give the temperature graph and the SPD facts of the
    // module cards
    public class LhmMemoryPerformanceViewModel
    {
        // === fields ===

        // created by whichever of the groups comes first
        private LhmMemoryInstanceViewModel _memory;

        // the module instances in discovery order, and the fact each of their static sensors feeds right now
        private readonly List<LhmHardwareInstance> _moduleInstances = new();
        private readonly Dictionary<string, HardwareFact> _factsBySensorId = new();


        // === constructor ===

        public LhmMemoryPerformanceViewModel()
        {
            Memories = new ObservableCollection<LhmMemoryInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (IsPhysicalMemory(instance)) AttachPhysical(instance);
                else if (IsVirtualMemory(instance)) AttachVirtual(instance);
                else if (IsModule(instance)) AttachModule(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        public ObservableCollection<LhmMemoryInstanceViewModel> Memories { get; }


        // === event handlers ===

        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                if (IsPhysicalMemory(instance)) AttachPhysical(instance);
                else if (IsVirtualMemory(instance)) AttachVirtual(instance);
                else if (IsModule(instance)) AttachModule(instance);
            }
        }


        // === private helpers ===

        private static bool IsPhysicalMemory(LhmHardwareInstance instance) =>
            instance.Kind == HardwareGroupKind.Ram && instance.HardwareName == "Total Memory";

        // LHM reports the commit charge as a Ram instance "Virtual Memory" with the same "Memory Used" and "Memory
        // Available"; roughly the Task Manager "Committed"
        private static bool IsVirtualMemory(LhmHardwareInstance instance) =>
            instance.Kind == HardwareGroupKind.Ram && instance.HardwareName == "Virtual Memory";

        // every other memory group is one module ("Samsung - M425R2GA3BB0-CQKOL (#0)")
        private static bool IsModule(LhmHardwareInstance instance) => instance.Kind == HardwareGroupKind.Ram;

        private LhmMemoryInstanceViewModel GetOrCreateMemory(string hardwareName)
        {
            if (_memory == null)
            {
                _memory = new LhmMemoryInstanceViewModel(hardwareName);
                Memories.Add(_memory);
            }
            return _memory;
        }

        private void AttachPhysical(LhmHardwareInstance instance)
        {
            var memory = GetOrCreateMemory(instance.HardwareName);

            foreach (var entry in instance.Sensors)
            {
                OnPhysicalSensorDiscovered(memory, entry, instance);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnPhysicalSensorsChanged(memory, e, instance);
        }

        private void AttachVirtual(LhmHardwareInstance instance)
        {
            var memory = GetOrCreateMemory(instance.HardwareName);

            foreach (var entry in instance.Sensors)
            {
                OnVirtualSensorDiscovered(memory, entry, instance);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnVirtualSensorsChanged(memory, e, instance);
        }

        private void AttachModule(LhmHardwareInstance instance)
        {
            // a module can come before the two memory groups; the view model keeps the name it would get from them
            var memory = GetOrCreateMemory(LhmMemoryInstanceViewModel.SwitchStateName);
            _moduleInstances.Add(instance);

            foreach (var entry in instance.Sensors)
            {
                OnModuleSensorDiscovered(memory, entry);
            }
            RebuildModuleFacts(memory);

            instance.Sensors.CollectionChanged += (s, e) =>
            {
                if (e.Action != NotifyCollectionChangedAction.Add) return;
                foreach (LhmSensorEntry entry in e.NewItems)
                {
                    OnModuleSensorDiscovered(memory, entry);
                }
                RebuildModuleFacts(memory);
            };
        }

        // the live temperature becomes a graph candidate; timings and thermal limits never move and are facts
        private void OnModuleSensorDiscovered(LhmMemoryInstanceViewModel memory, LhmSensorEntry entry)
        {
            if (IsModuleFact(entry))
            {
                entry.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;
                    if (_factsBySensorId.TryGetValue(entry.Id, out var fact)) fact.ValueText = FormatValue(entry);
                };
            }
            else if (entry.SensorType == "Temperature" && !entry.Name.Contains("Resolution"))
            {
                RegisterModuleTemperatureCandidate(memory, entry);
            }
        }

        // "Capacity" is left out on purpose: LHM 0.9.6 reads 32 GB for a 16 GB module, and Windows has the right one
        private static bool IsModuleFact(LhmSensorEntry entry) =>
            entry.SensorType == "Timing" || (entry.SensorType == "Temperature" && entry.Name.Contains("Limit"));

        // adds a candidate, active when nothing is yet and it is the saved choice (or none was saved, or the saved one
        // is a module that is gone; first found)
        private static void RegisterModuleTemperatureCandidate(LhmMemoryInstanceViewModel memory, LhmSensorEntry entry)
        {
            if (memory.ModuleTemperatureOptions.Any(c => c.SensorId == entry.Id)) return;

            SensorGraphViewModel cached = null;
            SensorGraphViewModel Resolve()
            {
                if (cached != null) return cached;
                cached = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(cached, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(cached, entry, e);
                return cached;
            }

            memory.ModuleTemperatureOptions.Add(new SensorSwitchCandidate(entry.Id, entry.Name, Resolve));

            string persistedId = SensorSwitchStateService.Instance.GetSelectedSensorId(LhmMemoryInstanceViewModel.SwitchStateName, "ModuleTemperature");
            if (persistedId == entry.Id)
            {
                memory.SetModuleTemperatureWithoutPersisting(Resolve());
            }
            else if (memory.ModuleTemperature == null)
            {
                // shown until the saved module arrives, which then takes the place
                memory.SetModuleTemperatureWithoutPersisting(Resolve());
            }
        }

        // every module card gets the same fact labels, the ones any module reports; a card whose module LHM does not
        // reach shows "-" for each
        private void RebuildModuleFacts(LhmMemoryInstanceViewModel memory)
        {
            var names = _moduleInstances
                .SelectMany(instance => instance.Sensors)
                .Where(IsModuleFact)
                .Select(entry => entry.Name)
                .Distinct()
                .ToList();

            var instanceByModule = MatchModules(memory.MemoryModules);
            _factsBySensorId.Clear();

            foreach (var module in memory.MemoryModules)
            {
                instanceByModule.TryGetValue(module, out var instance);

                var facts = new List<HardwareFact>();
                foreach (string name in names)
                {
                    var entry = instance?.Sensors.FirstOrDefault(s => s.Name == name && IsModuleFact(s));
                    var fact = new HardwareFact(name, entry != null ? FormatValue(entry) : "-");
                    if (entry != null) _factsBySensorId[entry.Id] = fact;
                    facts.Add(fact);
                }
                module.SpdFacts = facts;
            }
        }

        // which LHM module belongs to which module of Windows: by the part number in the LHM name, each instance taken
        // once, so two modules of one kit pair up in order; what is left pairs up in order only when the counts agree,
        // otherwise a soldered module would take the data of a socketed one
        private Dictionary<LhmMemoryModuleViewModel, LhmHardwareInstance> MatchModules(IReadOnlyList<LhmMemoryModuleViewModel> modules)
        {
            var result = new Dictionary<LhmMemoryModuleViewModel, LhmHardwareInstance>();
            var remaining = _moduleInstances.ToList();

            foreach (var module in modules)
            {
                string partNumber = module.Info.PartNumber;
                if (string.IsNullOrWhiteSpace(partNumber)) continue;

                var match = remaining.FirstOrDefault(i => i.HardwareName.Contains(partNumber, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;

                result[module] = match;
                remaining.Remove(match);
            }

            var unmatched = modules.Where(m => !result.ContainsKey(m)).ToList();
            if (remaining.Count > 0 && remaining.Count == unmatched.Count)
            {
                for (int i = 0; i < unmatched.Count; i++) result[unmatched[i]] = remaining[i];
            }

            return result;
        }

        private static string FormatValue(LhmSensorEntry entry) => SensorUnitFormatter.Format(entry.Value, entry.SensorType);

        private void OnPhysicalSensorsChanged(LhmMemoryInstanceViewModel memory, NotifyCollectionChangedEventArgs e, LhmHardwareInstance instance)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnPhysicalSensorDiscovered(memory, entry, instance);
            }
        }

        private void OnVirtualSensorsChanged(LhmMemoryInstanceViewModel memory, NotifyCollectionChangedEventArgs e, LhmHardwareInstance instance)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnVirtualSensorDiscovered(memory, entry, instance);
            }
        }

        private void OnPhysicalSensorDiscovered(LhmMemoryInstanceViewModel memory, LhmSensorEntry entry, LhmHardwareInstance instance)
        {
            if (entry.Name == "Memory Used")
            {
                memory.Used = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(memory.Used, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(memory.Used, entry, e);
                entry.PropertyChanged += (s, e) => RecalculateRoundedTotal(memory, instance, e);
                RecalculateRoundedTotal(memory, instance, null);
            }
            else if (entry.Name == "Memory Available")
            {
                memory.Available = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(memory.Available, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(memory.Available, entry, e);
                entry.PropertyChanged += (s, e) => RecalculateRoundedTotal(memory, instance, e);
                RecalculateRoundedTotal(memory, instance, null);
            }
        }

        private void OnVirtualSensorDiscovered(LhmMemoryInstanceViewModel memory, LhmSensorEntry entry, LhmHardwareInstance instance)
        {
            if (entry.Name == "Memory Used")
            {
                memory.VirtualMemoryUsed = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(memory.VirtualMemoryUsed, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(memory.VirtualMemoryUsed, entry, e);
                entry.PropertyChanged += (s, e) => RecalculateVirtualTotal(memory, instance, e);
                RecalculateVirtualTotal(memory, instance, null);
            }
            else if (entry.Name == "Memory Available")
            {
                entry.PropertyChanged += (s, e) => RecalculateVirtualTotal(memory, instance, e);
                RecalculateVirtualTotal(memory, instance, null);
            }
        }

        // Used + Available up to the next 4 GB, on every physical update
        private void RecalculateRoundedTotal(LhmMemoryInstanceViewModel memory, LhmHardwareInstance instance, PropertyChangedEventArgs e)
        {
            if (e != null && e.PropertyName != nameof(LhmSensorEntry.Value)) return;

            var used = instance.Sensors.FirstOrDefault(s => s.Name == "Memory Used");
            var available = instance.Sensors.FirstOrDefault(s => s.Name == "Memory Available");
            if (used == null || available == null) return;

            memory.RoundedTotalMemory = Math.Ceiling((used.Value + available.Value) / 4.0) * 4.0;
        }

        // the same for virtual memory, unrounded
        private void RecalculateVirtualTotal(LhmMemoryInstanceViewModel memory, LhmHardwareInstance instance, PropertyChangedEventArgs e)
        {
            if (e != null && e.PropertyName != nameof(LhmSensorEntry.Value)) return;

            var used = instance.Sensors.FirstOrDefault(s => s.Name == "Memory Used");
            var available = instance.Sensors.FirstOrDefault(s => s.Name == "Memory Available");
            if (used == null || available == null) return;

            memory.VirtualMemoryTotal = used.Value + available.Value;
        }

        private static void OnEntryValueChanged(SensorGraphViewModel graph, LhmSensorEntry entry, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;
            PushDataPoint(graph, entry);
        }

        private static void PushDataPoint(SensorGraphViewModel graph, LhmSensorEntry entry)
        {
            graph.AddDataPoint(entry.Value, SensorUnitFormatter.Format(entry.Value, entry.SensorType));
        }
    }
}
