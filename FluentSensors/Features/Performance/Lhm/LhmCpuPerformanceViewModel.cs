using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // cpu discovery:
    // one LhmCpuInstanceViewModel per CPU in LhmHardwareTreeService, every raw LHM sensor parsed into its property; the
    // instance stays a data holder
    public class LhmCpuPerformanceViewModel
    {
        // Load "CPU Core #<n>" or "CPU Core #<n> Thread #<m>"; "CPU Core Max" and "CPU Total" lack the "#" and drop out
        private static readonly Regex LoadCorePattern = new Regex(@"^CPU Core #(\d+)( Thread #\d+)?$", RegexOptions.Compiled);

        // a per-core Temperature or Clock name ("P-Core #3", "E-Core #12"), nothing after the digits; so "P-Core #1
        // Distance to TjMax" drops out
        private static readonly Regex CoreLabelPattern = new Regex(@"^(.+) #\d+$", RegexOptions.Compiled);

        // the preferred start sensor per category, best first; never a filter, every non-per-core
        // sensor of the type is offered
        // (LHM names one reading per vendor: Intel "Core Max" and "CPU Package", AMD "Core (Tctl/Tdie)" and "Package")
        private static readonly string[] LoadPreference = { "CPU Total" };
        private static readonly string[] TemperaturePreference =
        {
            "Core Max", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)",
            "CCDs Max (Tdie)", "CCDs Average (Tdie)", "Core Average", "CPU Package"
        };
        private static readonly string[] PowerPreference = { "CPU Package", "Package", "CPU PPT", "CPU Platform" };


        // === constructor ===

        public LhmCpuPerformanceViewModel()
        {
            Cpus = new ObservableCollection<LhmCpuInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (instance.Kind == HardwareGroupKind.Cpu) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        public ObservableCollection<LhmCpuInstanceViewModel> Cpus { get; }


        // === event handlers ===

        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                if (instance.Kind == HardwareGroupKind.Cpu) AttachToInstance(instance);
            }
        }


        // === private helpers ===

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            var cpu = new LhmCpuInstanceViewModel(instance.HardwareName);
            Cpus.Add(cpu);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(cpu, entry);
            }
            ApplyCategoryFallbacks(cpu);
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(cpu, e);
        }

        // once after the first sensor batch, per category: without a saved choice the best ranked candidate
        // beats discovery order; with nothing active (a saved choice that never showed up, an unknown vendor
        // naming), the first candidate
        private static void ApplyCategoryFallbacks(LhmCpuInstanceViewModel cpu)
        {
            ActivateDefault(cpu.HardwareName, "Load", cpu.TotalLoadOptions, () => cpu.TotalLoad, cpu.SetTotalLoadWithoutPersisting, LoadPreference);
            ActivateDefault(cpu.HardwareName, "Temperature", cpu.MaxTemperatureOptions, () => cpu.MaxTemperature, cpu.SetMaxTemperatureWithoutPersisting, TemperaturePreference);
            ActivateDefault(cpu.HardwareName, "Power", cpu.PackagePowerOptions, () => cpu.PackagePower, cpu.SetPackagePowerWithoutPersisting, PowerPreference);
        }

        private static void ActivateDefault(
            string hardwareName, string category, ObservableCollection<SensorSwitchCandidate> options,
            Func<SensorGraphViewModel> getActive, Action<SensorGraphViewModel> setActiveWithoutPersisting,
            string[] preferredNames)
        {
            if (options.Count == 0) return;

            if (SensorSwitchStateService.Instance.GetSelectedSensorId(hardwareName, category) == null)
            {
                var preferred = FindPreferred(options, preferredNames);
                if (preferred != null)
                {
                    var active = getActive();
                    if (active == null || active.SensorId != preferred.SensorId)
                    {
                        setActiveWithoutPersisting(preferred.Resolve());
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

        private void OnInstanceSensorsChanged(LhmCpuInstanceViewModel cpu, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(cpu, entry);
            }
        }

        private void OnSensorDiscovered(LhmCpuInstanceViewModel cpu, LhmSensorEntry entry)
        {
            // every per-core and per-thread name carries a "#<n>" for every vendor, no package-wide one does; the
            // overview tiles take the rest of their type
            bool isPerCoreName = entry.Name.Contains('#');

            if (entry.SensorType == "Load")
            {
                if (!isPerCoreName)
                {
                    RegisterCategoryCandidate(cpu, "Load", entry,
                        c => c.TotalLoad, (c, g) => c.SetTotalLoadWithoutPersisting(g), cpu.TotalLoadOptions);
                    return;
                }

                var loadMatch = LoadCorePattern.Match(entry.Name);
                if (loadMatch.Success)
                {
                    string coreNumber = loadMatch.Groups[1].Value;
                    bool hasThreads = loadMatch.Groups[2].Success; // " Thread #M" present

                    var core = cpu.GetOrCreateCore(coreNumber, hasThreads);

                    var threadGraph = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    core.Threads.Add(threadGraph);
                    PushDataPoint(threadGraph, entry);
                    cpu.RecomputeLoadAverage(hasThreads); // first All Threads point
                    entry.PropertyChanged += (s, e) =>
                    {
                        OnEntryValueChanged(threadGraph, entry, e);
                        if (e.PropertyName == nameof(LhmSensorEntry.Value)) cpu.RecomputeLoadAverage(hasThreads);
                    };
                }
            }
            else if (entry.SensorType == "Temperature")
            {
                if (!isPerCoreName)
                {
                    RegisterCategoryCandidate(cpu, "Temperature", entry,
                        c => c.MaxTemperature, (c, g) => c.SetMaxTemperatureWithoutPersisting(g), cpu.MaxTemperatureOptions);
                }
                else
                {
                    var labelMatch = CoreLabelPattern.Match(entry.Name);
                    if (labelMatch.Success)
                    {
                        string label = labelMatch.Groups[1].Value;
                        var graph = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                        var core = cpu.MatchNextTemperature(graph, label);
                        PushDataPoint(graph, entry);
                        if (core != null) cpu.RecomputeTemperatureAverage(core.HasThreads); // first All Threads point
                        entry.PropertyChanged += (s, e) =>
                        {
                            OnEntryValueChanged(graph, entry, e);
                            if (core != null && e.PropertyName == nameof(LhmSensorEntry.Value)) cpu.RecomputeTemperatureAverage(core.HasThreads);
                        };
                    }
                }
            }
            else if (entry.SensorType == "Clock")
            {
                // "Bus Speed" has no " #<n>" and drops out
                var labelMatch = CoreLabelPattern.Match(entry.Name);
                if (labelMatch.Success)
                {
                    string label = labelMatch.Groups[1].Value;
                    var graph = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    var core = cpu.MatchNextClock(graph, label);
                    PushDataPoint(graph, entry);
                    if (core != null) cpu.RecomputeClockAverage(core.HasThreads); // first All Threads point
                    entry.PropertyChanged += (s, e) =>
                    {
                        OnEntryValueChanged(graph, entry, e);
                        if (core != null && e.PropertyName == nameof(LhmSensorEntry.Value)) cpu.RecomputeClockAverage(core.HasThreads);
                    };
                }
            }
            else if (entry.SensorType == "Power")
            {
                if (!isPerCoreName)
                {
                    RegisterCategoryCandidate(cpu, "Power", entry,
                        c => c.PackagePower, (c, g) => c.SetPackagePowerWithoutPersisting(g), cpu.PackagePowerOptions);
                }
            }
        }

        // adds a candidate, active when nothing is yet and it is the saved choice (or none was saved, first found;
        // ApplyCategoryFallbacks corrects to the preferred one)
        private void RegisterCategoryCandidate(
            LhmCpuInstanceViewModel cpu,
            string category,
            LhmSensorEntry entry,
            Func<LhmCpuInstanceViewModel, SensorGraphViewModel> getActive,
            Action<LhmCpuInstanceViewModel, SensorGraphViewModel> setActiveWithoutPersisting,
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

            if (getActive(cpu) != null) return;

            string persistedId = SensorSwitchStateService.Instance.GetSelectedSensorId(cpu.HardwareName, category);
            if (persistedId == entry.Id || persistedId == null)
            {
                setActiveWithoutPersisting(cpu, Resolve());
            }
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
