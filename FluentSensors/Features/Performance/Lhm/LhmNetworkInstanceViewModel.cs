using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per currently active network adapter
    // (inactive adapters never reach this VM at all, since HardwareMonitorService already excludes them from the payload)
    public class LhmNetworkInstanceViewModel : INotifyPropertyChanged
    {
        public string HardwareName { get; }

        public LhmNetworkInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;
        }

        private SensorGraphViewModel _uploadSpeed;
        public SensorGraphViewModel UploadSpeed
        {
            get => _uploadSpeed;
            set { _uploadSpeed = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _downloadSpeed;
        public SensorGraphViewModel DownloadSpeed
        {
            get => _downloadSpeed;
            set { _downloadSpeed = value; OnPropertyChanged(); }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}