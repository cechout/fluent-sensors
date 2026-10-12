using System.Collections.Generic;
using System.ComponentModel;

using FluentSensors.Core.StaticInfo;


namespace FluentSensors.Features.Performance.Lhm
{
    // one module card of the memory info panel:
    // the facts Windows has for every module, and the SPD facts LHM reads from the module itself (timings, thermal
    // limits), which only a module with an SPD chip on the bus answers; a soldered one keeps "-"
    public class LhmMemoryModuleViewModel : INotifyPropertyChanged
    {
        public LhmMemoryModuleViewModel(WinMemoryModuleInfo info)
        {
            Info = info;
        }

        public WinMemoryModuleInfo Info { get; }

        // swapped whole by LhmMemoryPerformanceViewModel when a module arrives; empty while no module reports any
        private IReadOnlyList<HardwareFact> _spdFacts = new List<HardwareFact>();
        public IReadOnlyList<HardwareFact> SpdFacts
        {
            get => _spdFacts;
            set
            {
                _spdFacts = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpdFacts)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
