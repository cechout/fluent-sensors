using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;

namespace FluentSensors.Features.Performance.Lhm
{
    // one physical core, bundling however many threads it has (1 for cores without SMT, 2+ for cores with it)
    // plus its own Temperature/Clock graphs once matched by LhmCpuInstanceViewModel
    // TemperatureLabel/ClockLabel hold whatever raw label text LHM itself used (e.g. "P-Core", "E-Core") for this
    // core in its Temperature/Clock sensor name
    //
    // Load sensors usually do not carry that distinction by lhm, so this is the only place it can come from, and it
    // is stored as-is rather than interpreted or hardcoded to any particular vendors naming
    // Kept as two separate fields since the two sensor families could in theory disagree; not resolved/displayed
    // anywhere yet, just preserved for later
    public class LhmCpuCoreViewModel : INotifyPropertyChanged
    {
        public bool HasThreads { get; }

        public LhmCpuCoreViewModel(bool hasThreads)
        {
            HasThreads = hasThreads;
            Threads = new ObservableCollection<SensorGraphViewModel>();
        }

        // one entry per "CPU Core #N[ Thread #M]" Load sensor belonging to this physical core
        public ObservableCollection<SensorGraphViewModel> Threads { get; }

        private SensorGraphViewModel _temperature;
        public SensorGraphViewModel Temperature
        {
            get => _temperature;
            set { _temperature = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _clock;
        public SensorGraphViewModel Clock
        {
            get => _clock;
            set { _clock = value; OnPropertyChanged(); }
        }

        private string _temperatureLabel;
        public string TemperatureLabel
        {
            get => _temperatureLabel;
            set { _temperatureLabel = value; OnPropertyChanged(); }
        }

        private string _clockLabel;
        public string ClockLabel
        {
            get => _clockLabel;
            set { _clockLabel = value; OnPropertyChanged(); }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}