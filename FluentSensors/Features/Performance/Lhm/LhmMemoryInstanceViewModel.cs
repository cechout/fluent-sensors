using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per detected physical memory group; LHM reports this as a single "Total Memory" instance in
    // practice, but this stays correct if that ever differs on unusual hardware (e.g. NUMA)
    public class LhmMemoryInstanceViewModel : INotifyPropertyChanged
    {
        public string HardwareName { get; }

        public LhmMemoryInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;
        }

        private SensorGraphViewModel _used;
        public SensorGraphViewModel Used
        {
            get => _used;
            set { _used = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _available;
        public SensorGraphViewModel Available
        {
            get => _available;
            set { _available = value; OnPropertyChanged(); }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}