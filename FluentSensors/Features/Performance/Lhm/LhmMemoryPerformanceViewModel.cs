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

        // the module instances in discovery order, and how each of their static sensors writes its fact right now
        private readonly List<LhmHardwareInstance> _moduleInstances = new();
        private readonly Dictionary<string, Action> _factRefreshBySensorId = new();

        // what the thermal limit names share ("Thermal Sensor Critical Low Limit"); the rest tells them apart
        private const string ThermalLimitPrefix = "Thermal Sensor ";
        private const string ThermalLimitSuffix = " Limit";


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

        // every LHM memory group ends up in one RAM view: "Total Memory" for the Used and Available graphs,
        // "Virtual Memory" for the commit charge, every module for its temperature and the module cards
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
                    if (_factRefreshBySensorId.TryGetValue(entry.Id, out var refresh)) refresh();
                };
            }
            else if (entry.SensorType == "Temperature" && !entry.Name.Contains("Resolution"))
            {
                RegisterModuleTemperatureCandidate(memory, entry);
            }
        }

        // "Capacity" is left out on purpose: LHM 0.9.6 reads 32 GB for a 16 GB module, and Windows has the right one
        private static bool IsModuleFact(LhmSensorEntry entry) => IsTiming(entry) || IsThermalLimit(entry);

        private static bool IsTiming(LhmSensorEntry entry) => entry.SensorType == "Timing";

        private static bool IsThermalLimit(LhmSensorEntry entry) =>
            entry.SensorType == "Temperature" && entry.Name.Contains("Limit");

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
        // the thermal limits share one fact, the way the voltages of a module do; a timing keeps the short name of its
        // sensor ("tAA"), the info popup explains them
        // only the timings of PanelTimingNames get a fact; the sensors page has all of them
        private void RebuildModuleFacts(LhmMemoryInstanceViewModel memory)
        {
            var sensors = _moduleInstances.SelectMany(instance => instance.Sensors).ToList();
            var limitNames = sensors.Where(IsThermalLimit).Select(entry => entry.Name).Distinct().ToList();
            var timingNames = sensors.Where(IsTiming).Select(entry => entry.Name).Distinct()
                .Where(name => PanelTimingNames.Contains(ShortTimingName(name)))
                .OrderBy(name => Array.IndexOf(PanelTimingNames, ShortTimingName(name)))
                .ToList();

            var instanceByModule = MatchModules(memory.MemoryModules);
            _factRefreshBySensorId.Clear();

            foreach (var module in memory.MemoryModules)
            {
                instanceByModule.TryGetValue(module, out var instance);

                var facts = new List<HardwareFact>();
                var timingFacts = new List<HardwareFact>();

                if (limitNames.Count > 0)
                {
                    var limits = limitNames
                        .Select(name => instance?.Sensors.FirstOrDefault(s => s.Name == name && IsThermalLimit(s)))
                        .ToList();

                    var fact = new HardwareFact(FormatThermalLimitLabel(limitNames), FormatThermalLimits(limits));
                    foreach (var entry in limits)
                    {
                        if (entry != null) _factRefreshBySensorId[entry.Id] = () => fact.ValueText = FormatThermalLimits(limits);
                    }
                    facts.Add(fact);
                }

                foreach (string name in timingNames)
                {
                    var entry = instance?.Sensors.FirstOrDefault(s => s.Name == name && IsTiming(s));
                    var fact = new HardwareFact(ShortTimingName(name), entry != null ? FormatValue(entry) : "-");
                    if (entry != null) _factRefreshBySensorId[entry.Id] = () => fact.ValueText = FormatValue(entry);
                    timingFacts.Add(fact);
                }

                module.SpdFacts = facts;
                module.TimingFacts = timingFacts;
            }
        }

        // the timings a module is compared by; LHM reads up to fifteen, the rest only bloats the card:
        // - CL, tRCD, tRP, tRAS, in the order they are quoted
        // - the row cycle
        // - the refresh recovery
        // - the write recovery
        private static readonly string[] PanelTimingNames = { "tAA", "tRCD", "tRP", "tRAS", "tRC", "tRFC1", "tWR" };

        // "Thermal Sensor Limit (Low / High / Critical Low / Critical High)", from the sensor names; DDR4 has three
        private static string FormatThermalLimitLabel(IReadOnlyList<string> names)
        {
            var parts = names.Select(name => name.Replace(ThermalLimitPrefix, "").Replace(ThermalLimitSuffix, ""));
            return $"{ThermalLimitPrefix}{ThermalLimitSuffix.Trim()} ({string.Join(" / ", parts)})";
        }

        private static string FormatThermalLimits(IReadOnlyList<LhmSensorEntry> limits)
        {
            if (limits.All(entry => entry == null)) return "-";
            return string.Join(" / ", limits.Select(entry => entry != null ? FormatValue(entry) : "-"));
        }

        // "tAA (CAS Latency Time)" to "tAA"
        private static string ShortTimingName(string name)
        {
            int index = name.IndexOf(" (", StringComparison.Ordinal);
            return index > 0 ? name[..index] : name;
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

        // the Used and Available graphs, and the rounded total that scales Used
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

        // the commit charge graph; Available only adds to its total
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
