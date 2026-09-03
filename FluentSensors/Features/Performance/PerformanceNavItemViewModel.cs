using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using Microsoft.UI.Xaml;


namespace FluentSensors.Features.Performance
{
    // one entry in the sidebar / one selectable "page" within the single PerformancePage
    public class PerformanceNavItemViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // re-derives PrimaryGraph from Target whenever anything on the instance changes; the sensor this nav item
        // cares about does not exist yet at construction time (hardware discovery runs after the nav item is
        // created), so a one-time capture in the constructor would stay null forever; this keeps it live
        private readonly Func<object, SensorGraphViewModel> _getPrimaryGraph;


        // === constructor ===

        public PerformanceNavItemViewModel(HardwareGroupKind kind, string groupLabel, string displayName, object target,
            Func<object, SensorGraphViewModel> getPrimaryGraph)
        {
            Kind = kind;
            GroupLabel = groupLabel;
            _displayName = displayName;
            Target = target;
            _getPrimaryGraph = getPrimaryGraph;

            PrimaryGraph = _getPrimaryGraph(Target);

            if (Target is INotifyPropertyChanged notifyingTarget)
            {
                notifyingTarget.PropertyChanged += OnTargetPropertyChanged;
            }
        }


        // === bindable properties ===

        public HardwareGroupKind Kind { get; }

        // left side of the detail header, e.g. "CPU", "GPU"; fixed per Kind, never changes
        public string GroupLabel { get; }

        // sidebar mini-graph color; single source of truth in HardwareGroupInfo, same color every detail view uses
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(Kind).Color;

        // hardware type icon; same source and same glyph the detail views own header uses (e.g. CpuDetailView)
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(Kind).IconGlyph;

        // the specific hardware instance this nav item represents, e.g. one LhmCpuInstanceViewModel or one
        // LhmGpuInstanceViewModel; typed as object since the concrete type differs per Kind
        // PerformancePage picks the matching cached detail view purely by this objects runtime type
        public object Target { get; }

        // the one sensor shown as this hardwares "at a glance" utilization graph, e.g. TotalLoad for CPU,
        // DownloadSpeed for a network adapter; used by both the sidebar and the start page
        // Starts null and gets filled in once LHM actually discovers the underlying sensor
        private SensorGraphViewModel _primaryGraph;
        public SensorGraphViewModel PrimaryGraph
        {
            get => _primaryGraph;
            set
            {
                if (_primaryGraph != value)
                {
                    _primaryGraph = value;
                    OnPropertyChanged();
                }
            }
        }

        // sidebar label / right side of the detail header, e.g. the CPUs product name or a GPUs model name;
        // bindable because Cpu/Ram start with a null placeholder and get filled in once the first payload for
        // that hardware arrives (see PerformanceViewModels HardwareName propagation)
        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged();
                }
            }
        }

        // drives sidebar highlighting; set exclusively by PerformanceViewModel.SelectedItems setter
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }


        // === event handlers ===

        // re-derives PrimaryGraph on every change to the underlying instance; not scoped to just the one relevant
        // property (e.g. TotalLoad), since that would need a 6th selector delegate per Kind for little practical
        // gain; PrimaryGraphs own setter already no-ops once the value stops actually changing
        private void OnTargetPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var newGraph = _getPrimaryGraph(Target);
            if (!ReferenceEquals(newGraph, PrimaryGraph))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[NavItem {GroupLabel}] PrimaryGraph SWAPPED: old={PrimaryGraph?.GetHashCode():X}, new={newGraph?.GetHashCode():X}, newDataCount={newGraph?.SensorData?.Count}");
            }
            PrimaryGraph = newGraph;
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
