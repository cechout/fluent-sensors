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
                    new(AppTerms.Get("Display_ProductCode"), HardwareInfoFormatter.FormatTextOrDash(info.ProductCode)),
                    new(AppTerms.Get("Display_ManufactureDate"), HardwareInfoFormatter.FormatManufactureDate(info.ManufactureYear, info.ManufactureWeek)),
                    new(AppTerms.Get("Display_Size"), HardwareInfoFormatter.FormatDisplaySize(info.WidthCentimeters, info.HeightCentimeters)),
                    new(AppTerms.Get("Display_NativeResolution"), HardwareInfoFormatter.FormatResolution(info.NativeWidthPixels, info.NativeHeightPixels)),
                    new(AppTerms.Get("Display_PixelDensity"), HardwareInfoFormatter.FormatPixelDensity(
                        info.NativeWidthPixels, info.NativeHeightPixels, info.WidthCentimeters, info.HeightCentimeters))
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
                    new(AppTerms.Get("Display_Scale"), HardwareInfoFormatter.FormatScale(info.ScalePercent)),
                    new(AppTerms.Get("Display_Orientation"), HardwareInfoFormatter.FormatDisplayOrientation(info.Rotation)),
                    new(AppTerms.Get("Display_RefreshRate"), HardwareInfoFormatter.FormatRefreshRate(info.RefreshRateHz)),
                    new(AppTerms.Get("Display_AvailableRefreshRates"), HardwareInfoFormatter.FormatRefreshRates(info.RefreshRatesHz)),
                    new(AppTerms.Get("Display_Connector"), HardwareInfoFormatter.FormatDisplayConnector(info.OutputTechnology)),
                    new(AppTerms.Get("Display_Primary"), HardwareInfoFormatter.FormatYesNo(info.IsPrimary)),
                    new(AppTerms.Get("Display_Gpu"), HardwareInfoFormatter.FormatTextOrDash(gpu))
                };
            }
        }

        // info panel; color depth, HDR and the luminance the display reports
        public IReadOnlyList<HardwareFact> ColorFacts
        {
            get
            {
                var info = MatchStaticInfo();
                if (info == null) return new List<HardwareFact>();

                var facts = new List<HardwareFact>
                {
                    new(AppTerms.Get("Display_ColorDepth"), HardwareInfoFormatter.FormatColorDepth(info.BitsPerColorChannel)),
                    new(AppTerms.Get("Display_Hdr"), HardwareInfoFormatter.FormatHdr(info.IsHdrSupported, info.IsHdrEnabled))
                };

                // only means something while HDR is on
                if (info.SdrWhiteLevelNits.HasValue)
                {
                    facts.Add(new(AppTerms.Get("Display_SdrWhiteLevel"), HardwareInfoFormatter.FormatNits(info.SdrWhiteLevelNits)));
                }

                facts.Add(new(AppTerms.Get("Display_PeakLuminance"), HardwareInfoFormatter.FormatNits(info.MaxLuminanceNits)));
                facts.Add(new(AppTerms.Get("Display_MinLuminance"), HardwareInfoFormatter.FormatNits(info.MinLuminanceNits)));
                facts.Add(new(AppTerms.Get("Display_Gamma"), HardwareInfoFormatter.FormatGamma(info.Gamma)));

                return facts;
            }
        }

        // info panel; what the monitor itself answers over DDC/CI, empty when it answers nothing (most laptop panels)
        // KNOWN UNRELIABLE: see WinDisplayReader.QueryDdcLevels
        public IReadOnlyList<HardwareFact> DdcFacts
        {
            get
            {
                var ddc = MatchStaticInfo()?.Ddc;
                if (ddc == null) return new List<HardwareFact>();

                return new List<HardwareFact>
                {
                    new(AppTerms.Get("Display_PowerMode"), HardwareInfoFormatter.FormatDdcPowerMode(ddc.PowerMode)),
                    new(AppTerms.Get("Display_InputSource"), HardwareInfoFormatter.FormatDdcInputSource(ddc.InputSource)),
                    new(AppTerms.Get("Display_UsageTime"), HardwareInfoFormatter.FormatHours(ddc.UsageHours)),
                    new(AppTerms.Get("Display_Technology"), HardwareInfoFormatter.FormatDdcTechnology(ddc.Technology)),
                    new(AppTerms.Get("Display_FirmwareLevel"), HardwareInfoFormatter.FormatVersionOrDash(ddc.FirmwareLevel)),
                    new(AppTerms.Get("Display_MccsVersion"), HardwareInfoFormatter.FormatVersionOrDash(ddc.MccsVersion))
                };
            }
        }

        public Microsoft.UI.Xaml.Visibility DdcFactsVisibility =>
            MatchStaticInfo()?.Ddc != null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;


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
            OnPropertyChanged(nameof(ColorFacts));
            OnPropertyChanged(nameof(DdcFacts));
            OnPropertyChanged(nameof(DdcFactsVisibility));
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
