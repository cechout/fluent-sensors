namespace FluentSensors.Common.Sensors
{
    // decides whether anything that can be tinted per hardware category actually is, or whether it falls back to
    // the ordinary Windows colours
    //
    // deliberately a plain switch for now, flipped by editing this file; the settings entry that will drive it
    // lives in its own branch and only has to set this one property
    // two things are still missing for that: persistence, and a change notification, because every consumer binds
    // its colour OneTime today and would need a rebuilt page to pick a flip up at runtime
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
