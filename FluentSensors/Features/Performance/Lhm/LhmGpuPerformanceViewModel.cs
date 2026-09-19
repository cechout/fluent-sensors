using System;
using System.Collections.Generic;
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
    // discovers every GPU instance (dGPU + iGPU both count separately) from LhmHardwareTreeService and creates
    // one LhmGpuInstanceViewModel per instance; parses each raw LHM sensor into the right property on the right
    // instance
    // the instance itself stays a dumb data holder
    public class LhmGpuPerformanceViewModel
    {
        // preferred sensor for the two vendor-dependent categories, best first; this only decides which candidate
        // starts out active, it never filters the list: every sensor of the categorys type is offered
        // vendors disagree on the package power name in particular (NVIDIA and AMD "GPU Package", Intel iGPU
        // "GPU Power", AMD board power "GPU PPT"), so matching on a fixed name list left whole vendors with an
        // empty power tile
        private static readonly string[] TemperaturePreference = { "GPU Core", "GPU Hot Spot" };
        private static readonly string[] PowerPreference = { "GPU Package", "GPU Power", "GPU PPT", "GPU Total", "GPU Core" };


        // === constructor ===

        public LhmGpuPerformanceViewModel()
        {
            Gpus = new ObservableCollection<LhmGpuInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (instance.Kind == HardwareGroupKind.Gpu) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        public ObservableCollection<LhmGpuInstanceViewModel> Gpus { get; }


        // === event handlers ===

        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                if (instance.Kind == HardwareGroupKind.Gpu) AttachToInstance(instance);
            }
        }


        // === private helpers ===

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            var gpu = new LhmGpuInstanceViewModel(instance.HardwareName);
            Gpus.Add(gpu);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(gpu, entry);
            }
            ApplyCategoryFallbacks(gpu);
            ApplyD3dEngineDefaults(gpu);
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(gpu, e);
        }

        // runs once after the initial sensor batch, per category: if nothing was ever persisted, the best pick wins
        // over whichever candidate happened to be discovered first; Temperature/Power rank by name preference,
        // MemoryUsed still by its IsDefault flag because its two candidates are a fixed, vendor-independent pair
        // if nothing is active at all yet (e.g. a persisted choice never showed up, or this vendor names every
        // sensor of the category differently), falls back to the first candidate present
        private static void ApplyCategoryFallbacks(LhmGpuInstanceViewModel gpu)
        {
            ActivateDefault(gpu.HardwareName, "Temperature", gpu.TemperatureOptions, () => gpu.Temperature, gpu.SetTemperatureWithoutPersisting, TemperaturePreference);
            ActivateDefault(gpu.HardwareName, "Power", gpu.PackagePowerOptions, () => gpu.PackagePower, gpu.SetPackagePowerWithoutPersisting, PowerPreference);
            ActivateDefault(gpu.HardwareName, "MemoryUsed", gpu.MemoryUsedOptions, () => gpu.MemoryUsed, gpu.SetMemoryUsedWithoutPersisting, null);
        }

        private static void ActivateDefault(
            string hardwareName, string category, ObservableCollection<SensorSwitchCandidate> options,
            Func<SensorGraphViewModel> getActive, Action<SensorGraphViewModel> setActiveWithoutPersisting,
            string[] preferredNames)
        {
            if (options.Count == 0) return;

            if (SensorSwitchStateService.Instance.GetSelectedSensorId(hardwareName, category) == null)
            {
                var best = preferredNames != null
                    ? FindPreferred(options, preferredNames)
                    : options.FirstOrDefault(c => c.IsDefault);

                if (best != null)
                {
                    var active = getActive();
                    if (active == null || active.SensorId != best.SensorId)
                    {
                        setActiveWithoutPersisting(best.Resolve());
                        return;
                    }
                }
            }

            if (getActive() == null) setActiveWithoutPersisting(options[0].Resolve());
        }

        // walks the preference list, not the candidate list, so a lower ranked name that happened to be discovered
        // first never beats a better ranked one discovered later
        private static SensorSwitchCandidate FindPreferred(ObservableCollection<SensorSwitchCandidate> options, string[] preferredNames)
        {
            foreach (string name in preferredNames)
            {
                var match = options.FirstOrDefault(c => c.DisplayName == name);
                if (match != null) return match;
            }
            return null;
        }

        // fills the fixed D3D engine slots once, after the initial sensor batch: persisted choice if present,
        // otherwise the next not-yet-claimed candidate in discovery order; a slot stays empty if fewer D3D sensors
        // exist than slots
        private static void ApplyD3dEngineDefaults(LhmGpuInstanceViewModel gpu)
        {
            var claimed = new HashSet<string>();
            for (int slot = 0; slot < LhmGpuInstanceViewModel.D3dEngineSlotCount; slot++)
            {
                string persistedId = SensorSwitchStateService.Instance.GetSelectedSensorId(gpu.HardwareName, LhmGpuInstanceViewModel.D3dEngineCategory(slot));

                var match = persistedId != null
                    ? gpu.D3dEngineOptions.FirstOrDefault(c => c.SensorId == persistedId)
                    : gpu.D3dEngineOptions.FirstOrDefault(c => !claimed.Contains(c.SensorId));

                if (match == null) continue;
                claimed.Add(match.SensorId);
                gpu.SetD3dEngineSlotWithoutPersisting(slot, match.Resolve());
            }
        }

        private void OnInstanceSensorsChanged(LhmGpuInstanceViewModel gpu, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(gpu, entry);
            }
        }

        // matches on (Name, SensorType) rather than Name alone: "GPU Core" is reported as Load, Clock, Temperature
        // and Voltage with the exact same name, so Name-only matching would let one silently overwrite another
        private void OnSensorDiscovered(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry)
        {
            switch (entry.Name, entry.SensorType)
            {
                case ("GPU Core", "Load"):
                    gpu.CoreLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.CoreLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.CoreLoad, entry, e);
                    break;

                case ("GPU Core", "Clock"):
                    gpu.CoreClock = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.CoreClock, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.CoreClock, entry, e);
                    break;

                // both readings stay permanently visible in the Extended views Core group; RegisterEagerCategoryCandidate
                // additionally offers each of them as a switch candidate for the overviews single Temperature slot
                case ("GPU Core", "Temperature"):
                    gpu.CoreTemperature = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.CoreTemperature, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.CoreTemperature, entry, e);
                    RegisterEagerCategoryCandidate(gpu, "Temperature", gpu.CoreTemperature,
                        g => g.Temperature, (g, v) => g.SetTemperatureWithoutPersisting(v), gpu.TemperatureOptions);
                    break;

                case ("GPU Hot Spot", "Temperature"):
                    gpu.HotSpotTemperature = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.HotSpotTemperature, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.HotSpotTemperature, entry, e);
                    RegisterEagerCategoryCandidate(gpu, "Temperature", gpu.HotSpotTemperature,
                        g => g.Temperature, (g, v) => g.SetTemperatureWithoutPersisting(v), gpu.TemperatureOptions);
                    break;

                // wattage and core voltage share the one Power slot, so switching between them is a plain unit
                // change; the switch flyout still shows both by their own sensor names
                case ("GPU Core Voltage", "Voltage"):
                    RegisterCategoryCandidate(gpu, "Power", entry,
                        g => g.PackagePower, (g, v) => g.SetPackagePowerWithoutPersisting(v), gpu.PackagePowerOptions);
                    break;

                // native driver reading and Windows own D3D-reported figure for the same thing (VRAM in use);
                // treated as alternatives for the one MemoryUsed slot, same idea as CPU Package/Platform power
                case ("GPU Memory Used", "SmallData"):
                    RegisterCategoryCandidate(gpu, "MemoryUsed", entry,
                        g => g.MemoryUsed, (g, v) => g.SetMemoryUsedWithoutPersisting(v), gpu.MemoryUsedOptions, isDefault: true);
                    break;

                case ("D3D Dedicated Memory Used", "Data"):
                    RegisterCategoryCandidate(gpu, "MemoryUsed", entry,
                        g => g.MemoryUsed, (g, v) => g.SetMemoryUsedWithoutPersisting(v), gpu.MemoryUsedOptions);
                    break;

                // system RAM borrowed by the GPU, not VRAM; a different figure from the MemoryUsed candidates above,
                // not an alternative for the same slot
                case ("D3D Shared Memory Used", "SmallData"):
                    gpu.D3dSharedMemoryUsed = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.D3dSharedMemoryUsed, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.D3dSharedMemoryUsed, entry, e);
                    break;

                // the inverse of GPU Memory Used, added to the same switchable slot rather than shown separately
                case ("GPU Memory Free", "SmallData"):
                    RegisterCategoryCandidate(gpu, "MemoryUsed", entry,
                        g => g.MemoryUsed, (g, v) => g.SetMemoryUsedWithoutPersisting(v), gpu.MemoryUsedOptions, isDefault: true);
                    break;

                case ("GPU Memory Total", "SmallData"):
                    gpu.MemoryTotal = entry.Value;
                    entry.PropertyChanged += (s, e) => OnMemoryTotalChanged(gpu, entry, e);
                    break;

                case ("GPU Memory", "Clock"):
                    gpu.MemoryClock = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.MemoryClock, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.MemoryClock, entry, e);
                    break;

                case ("GPU Memory Controller", "Load"):
                    gpu.MemoryControllerLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.MemoryControllerLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.MemoryControllerLoad, entry, e);
                    break;

                case ("GPU PCIe Rx", "Throughput"):
                    gpu.PcieRx = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.PcieRx, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.PcieRx, entry, e);
                    break;

                case ("GPU PCIe Tx", "Throughput"):
                    gpu.PcieTx = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.PcieTx, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.PcieTx, entry, e);
                    break;

                case ("GPU Bus", "Load"):
                    gpu.BusLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.BusLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.BusLoad, entry, e);
                    break;

                case ("GPU Video Engine", "Load"):
                    gpu.VideoEngineLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.VideoEngineLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.VideoEngineLoad, entry, e);
                    break;

                // Windows GPU Engine performance counters; unlike every case above, the exact set and names are not
                // fixed, Windows creates a counter instance per engine type only once something actually uses it
                case (var name, "Load") when name.StartsWith("D3D"):
                    RegisterD3dCandidate(gpu, entry);
                    break;

                // every remaining temperature and power reading of this GPU, whatever the vendor calls it, joins the
                // switch list of the matching overview slot; the named Temperature cases above still match first,
                // they additionally keep their permanent home in the Extended view
                case (_, "Temperature"):
                    RegisterCategoryCandidate(gpu, "Temperature", entry,
                        g => g.Temperature, (g, v) => g.SetTemperatureWithoutPersisting(v), gpu.TemperatureOptions);
                    break;

                case (_, "Power"):
                    RegisterCategoryCandidate(gpu, "Power", entry,
                        g => g.PackagePower, (g, v) => g.SetPackagePowerWithoutPersisting(v), gpu.PackagePowerOptions);
                    break;
            }
        }

        // adds entry as a candidate, and activates it if nothing is active yet and it matches the persisted choice
        // (or nothing was ever persisted, first-found-wins for now; ApplyCategoryFallbacks corrects to the flagged
        // default afterward if one exists and discovery order picked something else)
        private void RegisterCategoryCandidate(
            LhmGpuInstanceViewModel gpu,
            string category,
            LhmSensorEntry entry,
            Func<LhmGpuInstanceViewModel, SensorGraphViewModel> getActive,
            Action<LhmGpuInstanceViewModel, SensorGraphViewModel> setActiveWithoutPersisting,
            ObservableCollection<SensorSwitchCandidate> options,
            bool isDefault = false,
            Func<double?> yMaxOverride = null)
        {
            SensorGraphViewModel cached = null;
            SensorGraphViewModel Resolve()
            {
                if (cached != null) return cached;
                cached = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(cached, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(cached, entry, e);
                return cached;
            }

            options.Add(new SensorSwitchCandidate(entry.Id, entry.Name, Resolve, isDefault, yMaxOverride));

            if (getActive(gpu) != null) return; // already resolved, this is just an additional alternative

            string persistedId = SensorSwitchStateService.Instance.GetSelectedSensorId(gpu.HardwareName, category);
            if (persistedId == entry.Id || persistedId == null)
            {
                setActiveWithoutPersisting(gpu, Resolve());
            }
        }

        // same activation logic as RegisterCategoryCandidate, but for a graph that already exists because it also
        // has its own permanent home elsewhere (see the two Temperature cases above); nothing to lazily build here
        private static void RegisterEagerCategoryCandidate(
            LhmGpuInstanceViewModel gpu,
            string category,
            SensorGraphViewModel graph,
            Func<LhmGpuInstanceViewModel, SensorGraphViewModel> getActive,
            Action<LhmGpuInstanceViewModel, SensorGraphViewModel> setActiveWithoutPersisting,
            ObservableCollection<SensorSwitchCandidate> options,
            bool isDefault = false,
            Func<double?> yMaxOverride = null)
        {
            options.Add(new SensorSwitchCandidate(graph.SensorId, graph.SensorName, () => graph, isDefault, yMaxOverride));

            if (getActive(gpu) != null) return;

            string persistedId = SensorSwitchStateService.Instance.GetSelectedSensorId(gpu.HardwareName, category);
            if (persistedId == graph.SensorId || persistedId == null)
            {
                setActiveWithoutPersisting(gpu, graph);
            }
        }

        // only ever adds to the shared pool; which slot (if any) ends up showing this candidate by default is
        // decided once for all of them together, by ApplyD3dEngineDefaults after the initial sensor batch
        private void RegisterD3dCandidate(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry)
        {
            SensorGraphViewModel cached = null;
            SensorGraphViewModel Resolve()
            {
                if (cached != null) return cached;
                cached = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(cached, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(cached, entry, e);
                return cached;
            }

            gpu.D3dEngineOptions.Add(new SensorSwitchCandidate(entry.Id, entry.Name, Resolve));
        }

        private static void OnEntryValueChanged(SensorGraphViewModel graph, LhmSensorEntry entry, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;
            PushDataPoint(graph, entry);
        }

        private static void OnMemoryTotalChanged(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;
            gpu.MemoryTotal = entry.Value;
        }

        private static void PushDataPoint(SensorGraphViewModel graph, LhmSensorEntry entry)
        {
            graph.AddDataPoint(entry.Value, SensorUnitFormatter.Format(entry.Value, entry.SensorType));
        }
    }
}
