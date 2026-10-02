using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Features.Performance.Lhm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;


namespace FluentSensors.Features.Performance
{
    // one sidebar entry, one selectable view of PerformancePage
    public class PerformanceNavItemViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // re-derives PrimaryGraph on every instance change; the sensor does not exist yet at construction
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

        // "CPU", "GPU"; fixed per Kind
        public string GroupLabel { get; }

        // the mini graph color from HardwareGroupInfo, like the detail views; (the color
        // setting is for widget and taskbar)
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(Kind).Color;

        // the glyph of the detail view headers; a network adapter picks wired or wireless itself
        public string GroupIconGlyph => Target is LhmNetworkInstanceViewModel network
            ? network.IconGlyph
            : HardwareGroupInfo.GetProfile(Kind).IconGlyph;

        // the glyph colour on the start view tiles, per the icon colour setting (the sidebar shows no glyph)
        public SolidColorBrush GroupIconBrush => HardwareGroupInfo.GetIconBrush(Kind);

        // the hardware instance (an LhmCpuInstanceViewModel, an LhmGpuInstanceViewModel); PerformancePage picks
        // the detail view by its type
        public object Target { get; }

        // the at-a-glance graph of the sidebar and the start view (TotalLoad for a CPU);
        // null until LHM finds the sensor
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

        // the sidebar label, the CPU product or GPU model name; set at construction
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

        // the sidebar highlight; set by PerformanceViewModel.SelectedItem only
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


        // === public methods ===

        // GroupIconBrush resolves every time; this makes the tiles ask again
        public void RefreshIconBrush() => OnPropertyChanged(nameof(GroupIconBrush));


        // === event handlers ===

        // on every instance change, not per property (that needs a delegate per Kind); the
        // setter no-ops on the same value
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
