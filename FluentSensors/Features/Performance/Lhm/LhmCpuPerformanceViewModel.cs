using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmCpuPerformanceViewModel
    {
        // matches "CPU Core #<n>" or "CPU Core #<n> Thread #<m>" (Load sensors); "CPU Core Max" and "CPU
        // Total" do not have a "#" right after "Core ", so they are excluded automatically, no special case needed
        private static readonly Regex LoadCorePattern = new Regex(@"^CPU Core #(\d+)( Thread #\d+)?$", RegexOptions.Compiled);

        // matches a per-core Temperature/Clock sensor name like "P-Core #3" or "E-Core #12"
        // A label followed by " #" and digits, with nothing else after
        // This is what excludes sibling sensors that share the same prefix but are not a plain per-core reading, e.g.
        // "P-Core #1 Distance to TjMax" does NOT match (it does not end right after the digits); no hardcoded exclusion
        // list needed
        private static readonly Regex CoreLabelPattern = new Regex(@"^(.+) #\d+$", RegexOptions.Compiled);


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
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(cpu, e);
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
            if (entry.SensorType == "Load")
            {
                if (entry.Name == "CPU Total")
                {
                    cpu.TotalLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(cpu.TotalLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(cpu.TotalLoad, entry, e);
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
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(threadGraph, entry, e);
                }
            }
            else if (entry.SensorType == "Temperature")
            {
                if (entry.Name == "Core Average")
                {
                    cpu.AverageTemperature = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(cpu.AverageTemperature, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(cpu.AverageTemperature, entry, e);
                }
                else if (entry.Name == "Core Max")
                {
                    cpu.MaxTemperature = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(cpu.MaxTemperature, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(cpu.MaxTemperature, entry, e);
                }
                else
                {
                    var labelMatch = CoreLabelPattern.Match(entry.Name);
                    if (labelMatch.Success)
                    {
                        string label = labelMatch.Groups[1].Value;
                        var graph = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                        cpu.MatchNextTemperature(graph, label);
                        PushDataPoint(graph, entry);
                        entry.PropertyChanged += (s, e) => OnEntryValueChanged(graph, entry, e);
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
                    cpu.MatchNextClock(graph, label);
                    PushDataPoint(graph, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(graph, entry, e);
                }
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