using Microsoft.UI.Xaml;


namespace FluentSensors.Common.Sensors
{
    // the hardware icon colour switch:
    // category colour or plain foreground for the category icons; the live switch,
    // SettingsService.UseHardwareIconColors persists and mirrors it
    // consumers refresh on SettingsService.HardwareIconColorsChanged without a page rebuild: the start page
    // snapshot tiles, the sensors page and hidden sensors group headers, the performance start tiles and
    // the five detail view headers
    // (graph colours come from SensorGraphViewModel)
    public static class HardwareColorMode
    {
        public static bool UseIconColors { get; set; } = false;

        // the applied theme of the untinted icons, mirrored in by the page whose ActualTheme moved
        // not the setting: on Default Windows switches without SettingsService.ThemeChanged, and that fires
        // before the new theme applies
        // the fallback covers the first resolution before any page loaded
        private static bool? _isDarkTheme;
        public static bool IsDarkTheme
        {
            get => _isDarkTheme ?? Application.Current.RequestedTheme == ApplicationTheme.Dark;
            set => _isDarkTheme = value;
        }
    }
}
