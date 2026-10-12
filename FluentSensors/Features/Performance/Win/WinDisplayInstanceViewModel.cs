using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Localization;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;


namespace FluentSensors.Features.Performance.Win
{
    // one display:
    // a data holder, WinDisplayPerformanceViewModel parses; a graph is null while Windows has no value for it (no
    // brightness for an external monitor)
    public class WinDisplayInstanceViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public WinDisplayInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;

            // until Detach; the gpu list names the adapter of the display
            WinStaticInfoService.Instance.DisplaysChanged += OnStaticInfoChanged;
            WinStaticInfoService.Instance.GpusChanged += OnStaticInfoChanged;
        }


        // === bindable properties ===

        public string HardwareName { get; }

        private SensorGraphViewModel _brightness;
        public SensorGraphViewModel Brightness
        {
            get => _brightness;
            set { _brightness = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _refreshRate;
        public SensorGraphViewModel RefreshRate
        {
            get => _refreshRate;
            set { _refreshRate = value; OnPropertyChanged(); }
        }

        // info panel; the panel itself, from its EDID
        public IReadOnlyList<HardwareFact> GeneralFacts
        {
            get
            {
                var info = MatchStaticInfo();
                if (info == null) return new List<HardwareFact>();

                return new List<HardwareFact>
                {
                    new(AppTerms.Get("Display_Manufacturer"), HardwareInfoFormatter.FormatTextOrDash(info.ManufacturerId)),
                    new(AppTerms.Get("Display_ManufactureYear"), HardwareInfoFormatter.FormatYear(info.ManufactureYear)),
                    new(AppTerms.Get("Display_Size"), HardwareInfoFormatter.FormatDisplaySize(info.WidthCentimeters, info.HeightCentimeters))
                };
            }
        }

        // info panel; how Windows drives it right now
        public IReadOnlyList<HardwareFact> ModeFacts
        {
            get
            {
                var info = MatchStaticInfo();
                if (info == null) return new List<HardwareFact>();

                string gpu = WinStaticInfoService.Instance.Gpus.FirstOrDefault(g => g.AdapterLuid != 0 && g.AdapterLuid == info.AdapterLuid)?.Name;

                return new List<HardwareFact>
                {
                    new(AppTerms.Get("Display_Resolution"), HardwareInfoFormatter.FormatResolution(info.WidthPixels, info.HeightPixels)),
                    new(AppTerms.Get("Display_RefreshRate"), HardwareInfoFormatter.FormatRefreshRate(info.RefreshRateHz)),
                    new(AppTerms.Get("Display_Connector"), HardwareInfoFormatter.FormatDisplayConnector(info.OutputTechnology)),
                    new(AppTerms.Get("Display_ColorDepth"), HardwareInfoFormatter.FormatColorDepth(info.BitsPerColorChannel)),
                    new(AppTerms.Get("Display_Hdr"), HardwareInfoFormatter.FormatHdr(info.IsHdrSupported, info.IsHdrEnabled)),
                    new(AppTerms.Get("Display_Gpu"), HardwareInfoFormatter.FormatTextOrDash(gpu))
                };
            }
        }


        // === public methods ===

        // the hardware left and this instance with it
        public void Detach()
        {
            WinStaticInfoService.Instance.DisplaysChanged -= OnStaticInfoChanged;
            WinStaticInfoService.Instance.GpusChanged -= OnStaticInfoChanged;
        }


        // === event handlers ===

        // Windows was asked again (another mode, HDR switched, a monitor plugged in)
        private void OnStaticInfoChanged()
        {
            OnPropertyChanged(nameof(GeneralFacts));
            OnPropertyChanged(nameof(ModeFacts));
        }


        // === private helpers ===

        // the sensor source and the static info name a display the same way (WinDisplayReader)
        private WinDisplayInfo MatchStaticInfo() =>
            WinStaticInfoService.Instance.Displays.FirstOrDefault(d => d.Name == HardwareName);


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
