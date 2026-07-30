using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per detected drive
    // A plain data holder; all sensor discovery/parsing lives in LhmStoragePerformanceViewModel instead
    public class LhmStorageInstanceViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public LhmStorageInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;
        }


        // === bindable properties ===

        public string HardwareName { get; }

        private SensorGraphViewModel _totalActivity;
        public SensorGraphViewModel TotalActivity
        {
            get => _totalActivity;
            set { _totalActivity = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _writeRate;
        public SensorGraphViewModel WriteRate
        {
            get => _writeRate;
            set { _writeRate = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _readRate;
        public SensorGraphViewModel ReadRate
        {
            get => _readRate;
            set { _readRate = value; OnPropertyChanged(); }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}