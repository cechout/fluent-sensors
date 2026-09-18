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
    }
}
