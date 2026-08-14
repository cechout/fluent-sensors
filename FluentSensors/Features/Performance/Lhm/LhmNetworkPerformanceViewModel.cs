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

        // runs once after the initial sensor batch, per category: if nothing was ever persisted and one candidate
        // is explicitly flagged IsDefault, that one wins over whichever candidate happened to be discovered first;
        // if nothing is active at all yet (e.g. a persisted choice never showed up), falls back to the first
        // candidate present
        private static void ApplyCategoryFallbacks(LhmNetworkInstanceViewModel adapter)
        {
            ActivateDefault(adapter.HardwareName, "Utilization", adapter.NetworkUtilizationOptions, () => adapter.NetworkUtilization, adapter.SetNetworkUtilizationWithoutPersisting);
        }

        private static void ActivateDefault(
            string hardwareName, string category, ObservableCollection<SensorSwitchCandidate> options,
            Func<SensorGraphViewModel> getActive, Action<SensorGraphViewModel> setActiveWithoutPersisting)
        {
            if (options.Count == 0) return;

            if (SensorSwitchStateService.Instance.GetSelectedSensorId(hardwareName, category) == null)
            {
                var flaggedDefault = options.FirstOrDefault(c => c.IsDefault);
                if (flaggedDefault != null)
                {
                    var active = getActive();
                    bool activeIsAlreadyDefault = active != null && options.Any(c => c.SensorId == active.SensorId && c.IsDefault);
                    if (!activeIsAlreadyDefault)
                    {
                        setActiveWithoutPersisting(flaggedDefault.Resolve());
                        return;
                    }
                }
            }

            if (getActive() == null) setActiveWithoutPersisting(options[0].Resolve());
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
                        a => a.NetworkUtilization, (a, v) => a.SetNetworkUtilizationWithoutPersisting(v), adapter.NetworkUtilizationOptions, isDefault: true);
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
        // (or nothing was ever persisted, first-found-wins for now; ApplyCategoryFallbacks corrects to the flagged
        // default afterward if one exists and discovery order picked something else)
        // guard: see LhmStoragePerformanceViewModel.OnSensorDiscovered for why a duplicate-candidate check is needed
        private void RegisterCategoryCandidate(
            LhmNetworkInstanceViewModel adapter,
            string category,
            LhmSensorEntry entry,
            Func<LhmNetworkInstanceViewModel, SensorGraphViewModel> getActive,
            Action<LhmNetworkInstanceViewModel, SensorGraphViewModel> setActiveWithoutPersisting,
            ObservableCollection<SensorSwitchCandidate> options,
            bool isDefault = false,
            Func<double?> yMaxOverride = null)
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

            options.Add(new SensorSwitchCandidate(entry.Id, entry.Name, Resolve, isDefault, yMaxOverride));

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
