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
    // gpu discovery:
    // one LhmGpuInstanceViewModel per GPU in LhmHardwareTreeService (dGPU and iGPU apart), every raw LHM sensor parsed
    // into its property; the instance stays a data holder
    public class LhmGpuPerformanceViewModel
    {
        // the preferred start sensor of the three vendor-dependent categories, best first; never a filter, every sensor
        // of the type is offered (vendors name package power "GPU Package", "GPU Power", "GPU PPT")
        private static readonly string[] LoadPreference = { "GPU Core", "D3D 3D" };
        private static readonly string[] TemperaturePreference = { "GPU Core", "GPU Hot Spot" };
        private static readonly string[] PowerPreference = { "GPU Package", "GPU Power", "GPU PPT", "GPU Total", "GPU Core" };


        // === constructor ===

        public LhmGpuPerformanceViewModel()
        {
            Gpus = new ObservableCollection<LhmGpuInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (HardwareGroupInfo.IsGpu(instance.Kind)) AttachToInstance(instance);
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
                if (HardwareGroupInfo.IsGpu(instance.Kind)) AttachToInstance(instance);
            }
        }


        // === private helpers ===

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            var gpu = new LhmGpuInstanceViewModel(instance.HardwareName, instance.Kind);
            Gpus.Add(gpu);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(gpu, entry);
            }
            ApplyCategoryFallbacks(gpu);
            ApplyD3dEngineDefaults(gpu);
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(gpu, e);
        }

        // after every sensor batch, per category: without a saved choice the best pick beats discovery order
        // (Load, Temperature and Power by name, MemoryUsed by IsDefault); with nothing active, the first candidate
        private static void ApplyCategoryFallbacks(LhmGpuInstanceViewModel gpu)
        {
            ActivateDefault(gpu.HardwareName, "Load", gpu.CoreLoadOptions, () => gpu.CoreLoad, gpu.SetCoreLoadWithoutPersisting, LoadPreference);
            ActivateDefault(gpu.HardwareName, "Temperature", gpu.TemperatureOptions, () => gpu.Temperature, gpu.SetTemperatureWithoutPersisting, TemperaturePreference);
            ActivateDefault(gpu.HardwareName, "ExtendedTemperature", gpu.ExtendedTemperatureOptions, () => gpu.ExtendedTemperature, gpu.SetExtendedTemperatureWithoutPersisting, TemperaturePreference);
            ActivateDefault(gpu.HardwareName, "Power", gpu.PackagePowerOptions, () => gpu.PackagePower, gpu.SetPackagePowerWithoutPersisting, PowerPreference);
            ActivateDefault(gpu.HardwareName, "ExtendedPower", gpu.ExtendedPackagePowerOptions, () => gpu.ExtendedPackagePower, gpu.SetExtendedPackagePowerWithoutPersisting, PowerPreference);
            ActivateDefault(gpu.HardwareName, "MemoryUsed", gpu.MemoryUsedOptions, () => gpu.MemoryUsed, gpu.SetMemoryUsedWithoutPersisting, null);
            ActivateDefault(gpu.HardwareName, "SharedMemory", gpu.SharedMemoryOptions, () => gpu.SharedMemory, gpu.SetSharedMemoryWithoutPersisting, null);
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

        // walks the preference list, so an earlier discovered lower rank never wins; a numbered D3D name ("D3D 3D
        // [1]") counts as its plain name
        private static SensorSwitchCandidate FindPreferred(ObservableCollection<SensorSwitchCandidate> options, string[] preferredNames)
        {
            foreach (string name in preferredNames)
            {
                var match = options.FirstOrDefault(c => c.DisplayName == name || c.DisplayName.StartsWith(name + " ["));
                if (match != null) return match;
            }
            return null;
        }

        // fills the empty D3D engine slots: the saved choice, else the next unclaimed candidate; a slot can stay empty
        private static void ApplyD3dEngineDefaults(LhmGpuInstanceViewModel gpu)
        {
            var claimed = new HashSet<string>();
            for (int slot = 0; slot < LhmGpuInstanceViewModel.D3dEngineSlotCount; slot++)
            {
                var filled = gpu.GetD3dEngineSlot(slot);
                if (filled != null) claimed.Add(filled.SensorId);
            }

            for (int slot = 0; slot < LhmGpuInstanceViewModel.D3dEngineSlotCount; slot++)
            {
                if (gpu.GetD3dEngineSlot(slot) != null) continue;

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

            // a sensor that came late can be the first of its category
            ApplyCategoryFallbacks(gpu);
            ApplyD3dEngineDefaults(gpu);
        }

        // matches on (Name, SensorType); "GPU Core" comes as Load, Clock, Temperature and Voltage
        // a sensor that is a switch candidate has its place; the Other group only takes what no group can, which
        // includes the discrete-only places on an integrated gpu (its Extended view does not show them)
        private void OnSensorDiscovered(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry)
        {
            switch (entry.Name, entry.SensorType)
            {
                // the overview Load slot, and one graph of its own for the Extended view, see ExtendedCoreLoad
                case ("GPU Core", "Load"):
                    RegisterCategoryCandidate(gpu, "Load", entry,
                        g => g.CoreLoad, (g, v) => g.SetCoreLoadWithoutPersisting(v), gpu.CoreLoadOptions);
                    if (!gpu.IsDiscrete) break;
                    gpu.ExtendedCoreLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.ExtendedCoreLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.ExtendedCoreLoad, entry, e);
                    break;

                case ("GPU Core", "Clock"):
                    gpu.CoreClock = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.CoreClock, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.CoreClock, entry, e);
                    break;

                // wattage and core voltage share the Power slot; switching is a unit change, the flyout names both
                // (NVIDIA calls it "GPU Core Voltage", amd and Intel "GPU Core")
                case ("GPU Core Voltage", "Voltage"):
                case ("GPU Core", "Voltage"):
                    RegisterPowerCandidate(gpu, entry);
                    break;

                // the driver and the Windows figure for VRAM in use and its inverse, alternatives in the MemoryUsed
                // slot (like CPU Package and Platform power); LHM moves gpu memory from SmallData to Data after
                // 0.9.6, so both match
                case ("GPU Memory Used", "SmallData" or "Data") when gpu.IsDiscrete:
                case ("GPU Memory Free", "SmallData" or "Data") when gpu.IsDiscrete:
                    RegisterCategoryCandidate(gpu, "MemoryUsed", entry,
                        g => g.MemoryUsed, (g, v) => g.SetMemoryUsedWithoutPersisting(v), gpu.MemoryUsedOptions, isDefault: true);
                    break;

                case ("D3D Dedicated Memory Used", "SmallData" or "Data") when gpu.IsDiscrete:
                case ("D3D Dedicated Memory Free", "SmallData" or "Data") when gpu.IsDiscrete:
                    RegisterCategoryCandidate(gpu, "MemoryUsed", entry,
                        g => g.MemoryUsed, (g, v) => g.SetMemoryUsedWithoutPersisting(v), gpu.MemoryUsedOptions);
                    break;

                // not charted; the y-axis maximum of the MemoryUsed slot
                case ("GPU Memory Total", "SmallData" or "Data") when gpu.IsDiscrete:
                    gpu.HasDriverMemoryTotal = true;
                    gpu.MemoryTotal = entry.Value;
                    entry.PropertyChanged += (s, e) => OnMemoryTotalChanged(gpu, entry, e);
                    break;

                // the same where the driver reports no total (amd through D3D only)
                case ("D3D Dedicated Memory Total", "SmallData" or "Data") when gpu.IsDiscrete:
                    if (!gpu.HasDriverMemoryTotal) gpu.MemoryTotal = entry.Value;
                    entry.PropertyChanged += (s, e) =>
                    {
                        if (!gpu.HasDriverMemoryTotal) OnMemoryTotalChanged(gpu, entry, e);
                    };
                    break;

                // system RAM lent to the GPU, not VRAM; a slot of its own, used and its inverse
                case ("D3D Shared Memory Used", "SmallData" or "Data"):
                    gpu.IsSharedMemoryInGigabytes = entry.SensorType == "Data";
                    RegisterCategoryCandidate(gpu, "SharedMemory", entry,
                        g => g.SharedMemory, (g, v) => g.SetSharedMemoryWithoutPersisting(v), gpu.SharedMemoryOptions, isDefault: true);
                    break;

                case ("D3D Shared Memory Free", "SmallData" or "Data"):
                    RegisterCategoryCandidate(gpu, "SharedMemory", entry,
                        g => g.SharedMemory, (g, v) => g.SetSharedMemoryWithoutPersisting(v), gpu.SharedMemoryOptions);
                    break;

                // not charted; the y-axis maximum of the SharedMemory slot (NVIDIA reports none, see SharedMemoryTotal)
                case ("D3D Shared Memory Total", "SmallData" or "Data"):
                    gpu.ReportedSharedMemoryTotal = entry.Value;
                    entry.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(LhmSensorEntry.Value)) gpu.ReportedSharedMemoryTotal = entry.Value;
                    };
                    break;

                case ("GPU Memory", "Clock") when gpu.IsDiscrete:
                    gpu.MemoryClock = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.MemoryClock, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.MemoryClock, entry, e);
                    break;

                case ("GPU Memory Controller", "Load") when gpu.IsDiscrete:
                    gpu.MemoryControllerLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.MemoryControllerLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.MemoryControllerLoad, entry, e);
                    break;

                // how full the vram is, in percent
                case ("GPU Memory", "Load") when gpu.IsDiscrete:
                    gpu.MemoryLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.MemoryLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.MemoryLoad, entry, e);
                    break;

                case ("GPU PCIe Rx", "Throughput") when gpu.IsDiscrete:
                    gpu.PcieRx = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.PcieRx, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.PcieRx, entry, e);
                    break;

                case ("GPU PCIe Tx", "Throughput") when gpu.IsDiscrete:
                    gpu.PcieTx = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.PcieTx, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.PcieTx, entry, e);
                    break;

                case ("GPU Bus", "Load") when gpu.IsDiscrete:
                    gpu.BusLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.BusLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.BusLoad, entry, e);
                    break;

                case ("GPU Video Engine", "Load") when gpu.IsDiscrete:
                    gpu.VideoEngineLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.VideoEngineLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.VideoEngineLoad, entry, e);
                    break;

                // Windows GPU Engine counters, one per adapter node; the names are driver text ("D3D 3D",
                // "D3D Video Decode", "D3D <friendly name>"), so the set is open
                case (var name, "Load") when name.StartsWith("D3D"):
                    RegisterD3dEngine(gpu, entry);
                    break;

                // every temperature and power reading joins its two switch lists, whatever the vendor calls it
                case (_, "Temperature"):
                    RegisterTemperatureCandidate(gpu, entry);
                    break;

                case (_, "Power"):
                    RegisterPowerCandidate(gpu, entry);
                    break;

                default:
                    AddOtherSensor(gpu, entry);
                    break;
            }
        }

        private void AddOtherSensor(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry)
        {
            var graph = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
            PushDataPoint(graph, entry);
            entry.PropertyChanged += (s, e) => OnEntryValueChanged(graph, entry, e);
            gpu.AddOtherSensor(graph);
        }

        // adds a candidate, active if nothing is yet and it is the saved choice (or nothing is saved, first found;
        // ApplyCategoryFallbacks corrects that)
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

            AddCategoryCandidate(gpu, category, new SensorSwitchCandidate(entry.Id, entry.Name, Resolve, isDefault, yMaxOverride),
                getActive, setActiveWithoutPersisting, options);
        }

        private static void AddCategoryCandidate(
            LhmGpuInstanceViewModel gpu,
            string category,
            SensorSwitchCandidate candidate,
            Func<LhmGpuInstanceViewModel, SensorGraphViewModel> getActive,
            Action<LhmGpuInstanceViewModel, SensorGraphViewModel> setActiveWithoutPersisting,
            ObservableCollection<SensorSwitchCandidate> options)
        {
            options.Add(candidate);

            if (getActive(gpu) != null) return; // an additional alternative

            string persistedId = SensorSwitchStateService.Instance.GetSelectedSensorId(gpu.HardwareName, category);
            if (persistedId == candidate.SensorId || persistedId == null)
            {
                setActiveWithoutPersisting(gpu, candidate.Resolve());
            }
        }

        // every power reading joins both Power slots, each with its own graph (see ExtendedCoreLoad)
        private void RegisterPowerCandidate(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry)
        {
            RegisterCategoryCandidate(gpu, "Power", entry,
                g => g.PackagePower, (g, v) => g.SetPackagePowerWithoutPersisting(v), gpu.PackagePowerOptions);
            RegisterCategoryCandidate(gpu, "ExtendedPower", entry,
                g => g.ExtendedPackagePower, (g, v) => g.SetExtendedPackagePowerWithoutPersisting(v), gpu.ExtendedPackagePowerOptions);
        }

        // the same for every temperature reading and the two Temperature slots
        private void RegisterTemperatureCandidate(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry)
        {
            RegisterCategoryCandidate(gpu, "Temperature", entry,
                g => g.Temperature, (g, v) => g.SetTemperatureWithoutPersisting(v), gpu.TemperatureOptions);
            RegisterCategoryCandidate(gpu, "ExtendedTemperature", entry,
                g => g.ExtendedTemperature, (g, v) => g.SetExtendedTemperatureWithoutPersisting(v), gpu.ExtendedTemperatureOptions);
        }

        // a D3D node into the engine pool (ApplyD3dEngineDefaults places it) and into the overview Load slot, each
        // with its own graph
        // a driver gives several nodes one name ("D3D Copy" seven times); those get a number in the switch lists and
        // graph titles, the sensor itself keeps its name
        private void RegisterD3dEngine(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry)
        {
            string displayName = entry.Name;
            var graphs = new List<SensorGraphViewModel>();

            SensorGraphViewModel CreateGraph()
            {
                var graph = new SensorGraphViewModel(entry.Id, displayName, entry.SensorType);
                PushDataPoint(graph, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(graph, entry, e);
                graphs.Add(graph);
                return graph;
            }

            SensorGraphViewModel engineGraph = null;
            SensorGraphViewModel loadGraph = null;
            var engineCandidate = new SensorSwitchCandidate(entry.Id, displayName, () => engineGraph ??= CreateGraph());
            var loadCandidate = new SensorSwitchCandidate(entry.Id, displayName, () => loadGraph ??= CreateGraph());

            gpu.D3dEngineOptions.Add(engineCandidate);
            AddCategoryCandidate(gpu, "Load", loadCandidate,
                g => g.CoreLoad, (g, v) => g.SetCoreLoadWithoutPersisting(v), gpu.CoreLoadOptions);

            gpu.TrackD3dDisplayName(entry.Name, name =>
            {
                displayName = name;
                engineCandidate.DisplayName = name;
                loadCandidate.DisplayName = name;
                foreach (var graph in graphs) graph.SensorName = name;
            });
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
