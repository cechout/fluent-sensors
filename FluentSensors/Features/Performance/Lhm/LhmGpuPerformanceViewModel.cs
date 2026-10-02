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
        // the preferred start sensor of the two vendor-dependent categories, best first; never a filter, every sensor
        // of the type is offered (vendors name package power "GPU Package", "GPU Power", "GPU PPT")
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

        // once after the first sensor batch, per category: without a saved choice the best pick beats discovery order
        // (Temperature and Power by name, MemoryUsed by IsDefault); with nothing active, the first candidate
        private static void ApplyCategoryFallbacks(LhmGpuInstanceViewModel gpu)
        {
            ActivateDefault(gpu.HardwareName, "Temperature", gpu.TemperatureOptions, () => gpu.Temperature, gpu.SetTemperatureWithoutPersisting, TemperaturePreference);
            ActivateDefault(gpu.HardwareName, "Power", gpu.PackagePowerOptions, () => gpu.PackagePower, gpu.SetPackagePowerWithoutPersisting, PowerPreference);
            ActivateDefault(gpu.HardwareName, "ExtendedPower", gpu.ExtendedPackagePowerOptions, () => gpu.ExtendedPackagePower, gpu.SetExtendedPackagePowerWithoutPersisting, PowerPreference);
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

        // walks the preference list, so an earlier discovered lower rank never wins
        private static SensorSwitchCandidate FindPreferred(ObservableCollection<SensorSwitchCandidate> options, string[] preferredNames)
        {
            foreach (string name in preferredNames)
            {
                var match = options.FirstOrDefault(c => c.DisplayName == name);
                if (match != null) return match;
            }
            return null;
        }

        // fills the D3D engine slots once: the saved choice, else the next unclaimed candidate; a slot can stay empty
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

        // matches on (Name, SensorType); "GPU Core" comes as Load, Clock, Temperature and Voltage
        private void OnSensorDiscovered(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry)
        {
            switch (entry.Name, entry.SensorType)
            {
                // one graph for the overview and one for the Extended view, see ExtendedCoreLoad
                case ("GPU Core", "Load"):
                    gpu.CoreLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.CoreLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.CoreLoad, entry, e);
                    gpu.ExtendedCoreLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.ExtendedCoreLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.ExtendedCoreLoad, entry, e);
                    break;

                case ("GPU Core", "Clock"):
                    gpu.CoreClock = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.CoreClock, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.CoreClock, entry, e);
                    break;

                // both stay in the Extended Core group and are offered to the overview Temperature slot too, with a
                // graph of their own (see ExtendedCoreLoad)
                case ("GPU Core", "Temperature"):
                    gpu.CoreTemperature = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.CoreTemperature, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.CoreTemperature, entry, e);
                    RegisterCategoryCandidate(gpu, "Temperature", entry,
                        g => g.Temperature, (g, v) => g.SetTemperatureWithoutPersisting(v), gpu.TemperatureOptions);
                    break;

                case ("GPU Hot Spot", "Temperature"):
                    gpu.HotSpotTemperature = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.HotSpotTemperature, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.HotSpotTemperature, entry, e);
                    RegisterCategoryCandidate(gpu, "Temperature", entry,
                        g => g.Temperature, (g, v) => g.SetTemperatureWithoutPersisting(v), gpu.TemperatureOptions);
                    break;

                // wattage and core voltage share the Power slot; switching is a unit change, the flyout names both
                case ("GPU Core Voltage", "Voltage"):
                    RegisterPowerCandidate(gpu, entry);
                    break;

                // the driver and the D3D figure for VRAM in use, alternatives in the MemoryUsed slot (like CPU
                // Package and Platform power)
                case ("GPU Memory Used", "SmallData"):
                    RegisterCategoryCandidate(gpu, "MemoryUsed", entry,
                        g => g.MemoryUsed, (g, v) => g.SetMemoryUsedWithoutPersisting(v), gpu.MemoryUsedOptions, isDefault: true);
                    break;

                case ("D3D Dedicated Memory Used", "Data"):
                    RegisterCategoryCandidate(gpu, "MemoryUsed", entry,
                        g => g.MemoryUsed, (g, v) => g.SetMemoryUsedWithoutPersisting(v), gpu.MemoryUsedOptions);
                    break;

                // system RAM borrowed by the GPU, not VRAM; no MemoryUsed alternative
                case ("D3D Shared Memory Used", "SmallData"):
                    gpu.D3dSharedMemoryUsed = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.D3dSharedMemoryUsed, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.D3dSharedMemoryUsed, entry, e);
                    break;

                // the inverse of GPU Memory Used, in the same slot
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

                // Windows GPU Engine counters; no fixed set, an instance appears once something uses the engine
                case (var name, "Load") when name.StartsWith("D3D"):
                    RegisterD3dCandidate(gpu, entry);
                    break;

                // every other temperature and power reading joins its overview switch list, whatever the vendor calls
                // it (the named cases above match first)
                case (_, "Temperature"):
                    RegisterCategoryCandidate(gpu, "Temperature", entry,
                        g => g.Temperature, (g, v) => g.SetTemperatureWithoutPersisting(v), gpu.TemperatureOptions);
                    break;

                case (_, "Power"):
                    RegisterPowerCandidate(gpu, entry);
                    break;
            }
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

            options.Add(new SensorSwitchCandidate(entry.Id, entry.Name, Resolve, isDefault, yMaxOverride));

            if (getActive(gpu) != null) return; // an additional alternative

            string persistedId = SensorSwitchStateService.Instance.GetSelectedSensorId(gpu.HardwareName, category);
            if (persistedId == entry.Id || persistedId == null)
            {
                setActiveWithoutPersisting(gpu, Resolve());
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

        // adds to the shared pool only; ApplyD3dEngineDefaults places them
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
