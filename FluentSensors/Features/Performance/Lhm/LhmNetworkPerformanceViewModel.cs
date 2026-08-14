using System;
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
            ApplyCategoryFallbacks(adapter);
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(adapter, e);
        }

        // falls back to the first candidate if a persisted choice never shows up on this system (removed hardware,
        // imported state from another PC); otherwise the category would stay without an active graph forever
        private static void ApplyCategoryFallbacks(LhmNetworkInstanceViewModel adapter)
        {
            if (adapter.NetworkUtilization == null && adapter.NetworkUtilizationOptions.Count > 0)
            {
                adapter.SetNetworkUtilizationWithoutPersisting(adapter.NetworkUtilizationOptions[0].Resolve());
            }
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

                case "Network Utilization":
                    RegisterCategoryCandidate(adapter, "Utilization", entry,
                        a => a.NetworkUtilization, (a, v) => a.SetNetworkUtilizationWithoutPersisting(v), adapter.NetworkUtilizationOptions);
                    break;

                case "Data Uploaded":
                    RegisterCategoryCandidate(adapter, "Utilization", entry,
                        a => a.NetworkUtilization, (a, v) => a.SetNetworkUtilizationWithoutPersisting(v), adapter.NetworkUtilizationOptions);
                    break;

                case "Data Downloaded":
                    RegisterCategoryCandidate(adapter, "Utilization", entry,
                        a => a.NetworkUtilization, (a, v) => a.SetNetworkUtilizationWithoutPersisting(v), adapter.NetworkUtilizationOptions);
                    break;
            }
        }

        // adds entry as a candidate, and activates it if nothing is active yet and it matches the persisted choice
        // (or nothing was ever persisted, first-found-wins)
        // guard: see LhmStoragePerformanceViewModel.OnSensorDiscovered for why a duplicate-candidate check is needed
        private void RegisterCategoryCandidate(
            LhmNetworkInstanceViewModel adapter,
            string category,
            LhmSensorEntry entry,
            Func<LhmNetworkInstanceViewModel, SensorGraphViewModel> getActive,
            Action<LhmNetworkInstanceViewModel, SensorGraphViewModel> setActiveWithoutPersisting,
            ObservableCollection<SensorSwitchCandidate> options)
        {
            if (options.Any(c => c.SensorId == entry.Id)) return;

            SensorGraphViewModel cached = null;
            SensorGraphViewModel Resolve()
            {
                if (cached != null) return cached;
                cached = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(cached, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(cached, entry, e);
                return cached;
            }

            options.Add(new SensorSwitchCandidate(entry.Id, entry.Name, Resolve));

            if (getActive(adapter) != null) return; // already resolved, this is just an additional alternative

            string persistedId = SensorSwitchStateService.Instance.GetSelectedSensorId(adapter.HardwareName, category);
            if (persistedId == entry.Id || persistedId == null)
            {
                setActiveWithoutPersisting(adapter, Resolve());
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
