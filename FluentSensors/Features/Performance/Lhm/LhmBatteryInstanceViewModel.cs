using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Localization;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // one battery:
    // a data holder, LhmBatteryPerformanceViewModel parses; a graph is null while LHM has its sensor deactivated (no
    // rate without a reading from the firmware)
    public class LhmBatteryInstanceViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public LhmBatteryInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;

            WinStaticInfoService.Instance.BatteriesChanged += OnStaticInfoChanged; // until Detach

            DetailOptions = new ObservableCollection<SensorSwitchCandidate>();
            Facts = new ObservableCollection<HardwareFact>();
        }


        // === bindable properties ===

        public string HardwareName { get; }

        private SensorGraphViewModel _chargeLevel;
        public SensorGraphViewModel ChargeLevel
        {
            get => _chargeLevel;
            set { _chargeLevel = value; OnPropertyChanged(); }
        }

        // charge or discharge power:
        // LHM reports the amount and names the direction ("Charge Rate", "Discharge Rate")
        private SensorGraphViewModel _rate;
        public SensorGraphViewModel Rate
        {
            get => _rate;
            set { _rate = value; OnPropertyChanged(); }
        }

        // voltage, current, remaining capacity, remaining time or temperature
        private SensorGraphViewModel _detail;
        public SensorGraphViewModel Detail
        {
            get => _detail;
            set
            {
                if (_detail == value) return;
                _detail = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Detail", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> DetailOptions { get; }

        internal void SetDetailWithoutPersisting(SensorGraphViewModel value)
        {
            _detail = value;
            OnPropertyChanged(nameof(Detail));
        }

        // --- info panel ---

        // the readings that only move with the age of the battery:
        // - designed capacity
        // - full capacity
        // - degradation
        public ObservableCollection<HardwareFact> Facts { get; }

        // what the firmware says about the pack and LHM has no sensor for:
        // - manufacturer
        // - chemistry
        // - manufacture date, when the firmware reports one
        public IReadOnlyList<HardwareFact> StaticFacts
        {
            get
            {
                var info = MatchStaticInfo();
                if (info == null) return new List<HardwareFact>();

                var facts = new List<HardwareFact>
                {
                    new(AppTerms.Get("Battery_Manufacturer"), info.Manufacturer),
                    new(AppTerms.Get("Battery_Chemistry"), HardwareInfoFormatter.FormatBatteryChemistry(info.Chemistry))
                };

                // most firmware reports none; the line only shows with one
                if (info.ManufactureDate.Length > 0) facts.Add(new(AppTerms.Get("Battery_ManufactureDate"), info.ManufactureDate));

                return facts;
            }
        }

        // sits with the LHM readings about the age of the pack
        public string CycleCountText => HardwareInfoFormatter.FormatCount(MatchStaticInfo()?.CycleCount);


        // === public methods ===

        // the hardware left and this instance with it
        public void Detach()
        {
            WinStaticInfoService.Instance.BatteriesChanged -= OnStaticInfoChanged;
        }


        // === event handlers ===

        // Windows was asked again (the cycle count moved, a pack was swapped)
        private void OnStaticInfoChanged()
        {
            OnPropertyChanged(nameof(StaticFacts));
            OnPropertyChanged(nameof(CycleCountText));
        }


        // === private helpers ===

        // matched by name, LHM lists a battery under the device name of its driver; a single battery needs no match
        private WinBatteryInfo MatchStaticInfo()
        {
            var batteries = WinStaticInfoService.Instance.Batteries;
            return batteries.FirstOrDefault(b => b.Name == HardwareName) ?? (batteries.Count == 1 ? batteries[0] : null);
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
