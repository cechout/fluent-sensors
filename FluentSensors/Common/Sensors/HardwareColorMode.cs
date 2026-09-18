namespace FluentSensors.Common.Sensors
{
    // decides whether anything that can be tinted per hardware category actually is, or whether it falls back to
    // the ordinary Windows colours
    //
    // the live switch, not the setting: SettingsService.UseHardwareColors owns persistence and the change event
    // and mirrors its value onto the property below, which keeps the consumers of the colour out of persistence
    // a flip has to be picked up without rebuilding a page, so every consumer listens for
    // SettingsService.HardwareColorsChanged and refreshes what it drew from here
    //
    // what it currently reaches: the start pages snapshot icons and the whole performance page (sidebar mini
    // graph plus every detail view graph)
    // what it does not reach yet, because none of them is tinted at all today: the sensors page group icon, the
    // widget graphs and the taskbar graphs
    public static class HardwareColorMode
    {
        // false renders those places in the normal foreground and lets graphs fall back to the accent or custom
        // colour from the settings
        public static bool UseGroupColors { get; set; } = true;
    }
}
