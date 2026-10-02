using System.Collections.ObjectModel;
using FluentSensors.Common.Sensors;


namespace FluentSensors.Core.Lhm
{
    // one hardware instance (one GPU) with every sensor LHM reported for it, unfiltered
    public class LhmHardwareInstance
    {
        public string HardwareName { get; }
        public HardwareGroupKind Kind { get; }
        public ObservableCollection<LhmSensorEntry> Sensors { get; }

        public LhmHardwareInstance(string hardwareName, HardwareGroupKind kind)
        {
            HardwareName = hardwareName;
            Kind = kind;
            Sensors = new ObservableCollection<LhmSensorEntry>();
        }
    }
}
