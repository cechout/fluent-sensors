using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common;

namespace FluentSensors.Features.Performance
{
    // one entry in the sidebar / one selectable "page" within the single PerformancePage
    // Cpu and Ram entries are created once and live for the apps lifetime; Gpu/Storage/Network entries are created reactively as
    // their underlying hardware collections discover new instances (see PerformanceViewModel)
    public class PerformanceNavItemViewModel : INotifyPropertyChanged
    {
        public HardwareGroupKind Kind { get; }

        // left side of the detail header, e.g. "CPU", "GPU"; fixed per Kind, never changes
        public string GroupLabel { get; }

        // the object the detail block for this Kind binds against; e.g. LhmCpuPerformanceViewModel for Cpu,
        // or a single LhmGpuInstanceViewModel for a Gpu entry
        // Typed as object since the concrete type differs per Kind; each detail block in PerformancePage.xaml only reads from
        // the Target matching its own Kind
        public object Target { get; }

        public PerformanceNavItemViewModel(HardwareGroupKind kind, string groupLabel, string displayName, object target)
        {
            Kind = kind;
            GroupLabel = groupLabel;
            _displayName = displayName;
            Target = target;
        }

        // sidebar label / right side of the detail header, e.g. the CPUs product name or a GPUs model name;
        // bindable because Cpu/Ram start with a null placeholder and get filled in once the first payload for that hardware
        // arrives (see PerformanceViewModels HardwareName propagation)
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

        // drives sidebar highlighting; set exclusively by PerformanceViewModel.SelectedItem's setter
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


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}