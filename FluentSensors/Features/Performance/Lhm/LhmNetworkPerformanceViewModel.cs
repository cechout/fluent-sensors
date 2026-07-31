using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;


namespace FluentSensors.Features.Performance.Lhm
{
    // discovers every active network adapter from LhmHardwareTreeService and creates one
    // LhmNetworkInstanceViewModel per adapter; parses each raw LHM sensor into the right property on the right
    // instance
    // the instance itself stays a dumb data holder
    public class LhmNetworkPerformanceViewModel
    {
        // === constructor ===

        public LhmNetworkPerformanceViewModel()
        {
            Adapters = new ObservableCollection<LhmNetworkInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (instance.Kind == HardwareGroupKind.Network) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        public ObservableCollection<LhmNetworkInstanceViewModel> Adapters { get; }


        // === event handlers ===

        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                if (instance.Kind == HardwareGroupKind.Network) AttachToInstance(instance);
            }
        }


        // === private helpers ===

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            var adapter = new LhmNetworkInstanceViewModel(instance.HardwareName);
            Adapters.Add(adapter);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(adapter, entry);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(adapter, e);
        }

        private void OnInstanceSensorsChanged(LhmNetworkInstanceViewModel adapter, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(adapter, entry);
            }
        }

        private void OnSensorDiscovered(LhmNetworkInstanceViewModel adapter, LhmSensorEntry entry)
        {
            switch (entry.Name)
            {
                case "Upload Speed":
                    // guard: see LhmStoragePerformanceViewModel.OnSensorDiscovered for the full explanation
                    adapter.UploadSpeed ??= new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(adapter.UploadSpeed, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(adapter.UploadSpeed, entry, e);
                    break;

                case "Download Speed":
                    adapter.DownloadSpeed ??= new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(adapter.DownloadSpeed, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(adapter.DownloadSpeed, entry, e);
                    break;
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