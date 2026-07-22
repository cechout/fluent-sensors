using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per detected drive
    public class LhmStorageInstanceViewModel : INotifyPropertyChanged
    {
        public string HardwareName { get; }

        public LhmStorageInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;
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