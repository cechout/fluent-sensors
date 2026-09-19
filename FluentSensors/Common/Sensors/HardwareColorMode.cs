using Microsoft.UI.Xaml;


namespace FluentSensors.Common.Sensors
{
    // decides whether the hardware category icons are tinted with their category colour or drawn in the ordinary
    // foreground
    //
    // the live switch, not the setting: SettingsService.UseHardwareIconColors owns persistence and the change
    // event and mirrors its value onto the property below, which is what keeps HardwareGroupInfo out of
    // persistence
    // a flip has to be picked up without rebuilding a page, so every consumer listens for
    // SettingsService.HardwareIconColorsChanged and refreshes the brush it drew from here
    //
    // what it reaches: the start pages snapshot tiles, the sensors page and hidden sensors window group headers,
    // the performance start view tiles and the five hardware detail view headers
    // graph colours are not part of this; those are resolved in SensorGraphViewModel straight off the settings
    public static class HardwareColorMode
    {
        public static bool UseIconColors { get; set; } = false;

        // the effective light/dark state the untinted icons resolve against, mirrored in by whichever page just saw
        // its own ActualTheme move
        //
        // the app theme setting cannot drive this: while it sits on Default, windows switches underneath the app
        // and SettingsService.ThemeChanged never fires at all; and when it does fire, it runs before the new theme
        // has been applied, so a refresh hanging off it still resolves against the old one
        // the fallback covers the very first resolution, before any page has loaded
        private static bool? _isDarkTheme;
        public static bool IsDarkTheme
        {
            get => _isDarkTheme ?? Application.Current.RequestedTheme == ApplicationTheme.Dark;
            set => _isDarkTheme = value;
        }
    }
}
