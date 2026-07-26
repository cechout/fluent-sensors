using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmCpuPerformanceViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public LhmCpuPerformanceViewModel()
        {
            Cores = new ObservableCollection<SensorGraphViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            // process whatever the tree already discovered before we subscribed, then track further Cpu instances live
            // (in practice there is exactly one, but this stays correct for a theoretical multi-socket system too)
            foreach (var instance in tree.HardwareGroups)
            {
                if (instance.Kind == HardwareGroupKind.Cpu) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        // the CPUs product name (e.g. "12th Gen Intel Core i9-12900H"), captured once from the first matching
        // instance; used by PerformanceViewModel to populate the CPU nav items DisplayName
        private string _hardwareName;
        public string HardwareName
        {
            get => _hardwareName;
            private set { _hardwareName = value; OnPropertyChanged(); }
        }

        // overall CPU load; created lazily once the first "CPU Total" sensor is discovered
        private SensorGraphViewModel _totalLoad;
        public SensorGraphViewModel TotalLoad
        {
            get => _totalLoad;
            private set { _totalLoad = value; OnPropertyChanged(); }
        }

        // one graph per "CPU Core #N[ Thread #M]" sensor
        public ObservableCollection<SensorGraphViewModel> Cores { get; }

        // controls which of the two views is currently shown
        // (single overall graph or grid of all cores/threads)
        private bool _isShowingAllThreads;
        public bool IsShowingAllThreads
        {
            get => _isShowingAllThreads;
            set
            {
                if (_isShowingAllThreads != value)
                {
                    _isShowingAllThreads = value;
                    OnPropertyChanged();
                }
            }
        }


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
            if (HardwareName == null) HardwareName = instance.HardwareName;

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(entry);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(e);
        }

        private void OnInstanceSensorsChanged(NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(entry);
            }
        }

        private void OnSensorDiscovered(LhmSensorEntry entry)
        {
            if (entry.SensorType != "Load") return;

            if (entry.Name == "CPU Total")
            {
                TotalLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(TotalLoad, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(TotalLoad, entry, e);
            }
            else if (entry.Name.StartsWith("CPU Core #"))
            {
                var core = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                Cores.Add(core);
                PushDataPoint(core, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(core, entry, e);
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


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}