using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Sensors;


namespace FluentSensors.Features.Performance
{
    // one entry in the sidebar / one selectable "page" within the single PerformancePage
    public class PerformanceNavItemViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public PerformanceNavItemViewModel(HardwareGroupKind kind, string groupLabel, string displayName, object target)
        {
            Kind = kind;
            GroupLabel = groupLabel;
            _displayName = displayName;
            Target = target;
        }


        // === bindable properties ===

        public HardwareGroupKind Kind { get; }

        // left side of the detail header, e.g. "CPU", "GPU"; fixed per Kind, never changes
        public string GroupLabel { get; }

        // the specific hardware instance this nav item represents, e.g. one LhmCpuInstanceViewModel or one
        // LhmGpuInstanceViewModel; typed as object since the concrete type differs per Kind
        // PerformancePage picks the matching cached detail view purely by this objects runtime type
        public object Target { get; }

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


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}