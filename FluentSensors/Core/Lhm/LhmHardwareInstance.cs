using System.Collections.ObjectModel;
using FluentSensors.Common.Sensors;


namespace FluentSensors.Core.Lhm
{
    // one physical/logical hardware instance (e.g. one specific GPU) and every sensor LHM ever reported for it,
    // completely unfiltered; no threshold, no hide/show, no curated subset
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