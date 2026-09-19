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
    // discovers every CPU instance from LhmHardwareTreeService and creates one LhmCpuInstanceViewModel per
    // instance; parses each raw LHM sensor into the right property on the right instance
    // the instance itself stays a dumb data holder, all "which sensor goes where" logic lives here
    public class LhmCpuPerformanceViewModel
    {
        // matches "CPU Core #<n>" or "CPU Core #<n> Thread #<m>" (Load sensors); "CPU Core Max" and "CPU
        // Total" do not have a "#" right after "Core ", so they are excluded automatically, no special case needed
        private static readonly Regex LoadCorePattern = new Regex(@"^CPU Core #(\d+)( Thread #\d+)?$", RegexOptions.Compiled);

        // matches a per-core Temperature/Clock sensor name like "P-Core #3" or "E-Core #12"
        // a label followed by " #" and digits, with nothing else after
        // this is what excludes sibling sensors that share the same prefix but are not a plain per-core reading, e.g.
        // "P-Core #1 Distance to TjMax" does NOT match (it does not end right after the digits); no hardcoded exclusion
        // list needed
        private static readonly Regex CoreLabelPattern = new Regex(@"^(.+) #\d+$", RegexOptions.Compiled);

        // preferred sensor per switchable category, best first; this only decides which candidate starts out active,
        // it never filters the list: every non-per-core sensor of the categorys type is offered
        // LHM names the same reading differently per vendor (Intel "Core Max" and "CPU Package" power against AMD
        // "Core (Tctl/Tdie)" and "Package"), so matching on a fixed name list left AMD machines with empty tiles
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

        // runs once after the initial sensor batch, per category: if nothing was ever persisted, the best ranked
        // candidate of the preference list wins over whichever candidate happened to be discovered first; if nothing
        // is active at all yet (e.g. a persisted choice never showed up, or this vendor names every sensor of the
        // category differently), falls back to the first candidate present
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
            // every per-core and per-thread reading carries a "#<n>" in its name, whatever the vendor calls the rest
            // of it, and no package-wide reading does; so the three overview tiles take every sensor of their own
            // type without a "#" instead of a fixed set of names, and the core grid below keeps the rest
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
                    bool hasThreads = loadMatch.Groups[2].Success; // only matched if " Thread #M" was present

                    var core = cpu.GetOrCreateCore(coreNumber, hasThreads);

                    var threadGraph = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    core.Threads.Add(threadGraph);
                    PushDataPoint(threadGraph, entry);
                    cpu.RecomputeLoadAverage(hasThreads); // All Threads tile: first reading for this thread
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
                        if (core != null) cpu.RecomputeTemperatureAverage(core.HasThreads); // All Threads tile: first reading for this core
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
                // "Bus Speed" has no " #<n>" suffix at all, so it never matches here; excluded automatically
                var labelMatch = CoreLabelPattern.Match(entry.Name);
                if (labelMatch.Success)
                {
                    string label = labelMatch.Groups[1].Value;
                    var graph = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    var core = cpu.MatchNextClock(graph, label);
                    PushDataPoint(graph, entry);
                    if (core != null) cpu.RecomputeClockAverage(core.HasThreads); // All Threads tile: first reading for this core
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

        // adds entry as a candidate, and activates it if nothing is active yet and it matches the persisted choice
        // (or nothing was ever persisted, first-found-wins for now; ApplyCategoryFallbacks corrects to the preferred
        // candidate afterward if one is present and discovery order picked something else)
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
