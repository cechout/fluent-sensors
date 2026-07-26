using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per detected CPU; a multi-socket system shows more than one, though in practice this is
    // virtually always exactly one
    public class LhmCpuInstanceViewModel : INotifyPropertyChanged
    {
        public string HardwareName { get; }

        public LhmCpuInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;
            Cores = new ObservableCollection<SensorGraphViewModel>();
        }

        private SensorGraphViewModel _totalLoad;
        public SensorGraphViewModel TotalLoad
        {
            get => _totalLoad;
            set { _totalLoad = value; OnPropertyChanged(); }
        }

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
                    OnPropertyChanged(nameof(OverallVisibility));
                    OnPropertyChanged(nameof(AllThreadsVisibility));
                }
            }
        }

        // one graph per "CPU Core #N[ Thread #M]" sensor
        public ObservableCollection<SensorGraphViewModel> Cores { get; }

        // pre-computed Visibility for the two CPU detail views; avoids function bindings inside the DataTemplate,
        // which x:Bind cannot reliably re-evaluate when a property buried inside the function body changes
        public Visibility OverallVisibility => IsShowingAllThreads ? Visibility.Collapsed : Visibility.Visible;
        public Visibility AllThreadsVisibility => IsShowingAllThreads ? Visibility.Visible : Visibility.Collapsed;


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}