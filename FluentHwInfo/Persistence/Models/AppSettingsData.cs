using Windows.UI;


namespace FluentHwInfo.Persistence.Models
{
    // plain serializable snapshot of everything in SettingsService that should survive a restart
    // kept separate from SettingsService itself so that class can stay focused on live app state (events, validation)
    // while this stays a simple data container for disk I/O
    public class AppSettingsData
    {
        public string AppTheme { get; set; } = "Default";
        public string BackdropType { get; set; } = "Mica";
        public float TintOpacity { get; set; } = 0.4f;
        public float LuminosityOpacity { get; set; } = 0.2f;
        public bool UseAccentColor { get; set; } = true;
        public Color CustomTintColor { get; set; } = Color.FromArgb(255, 25, 25, 25);
        public bool UseGraphAccentColor { get; set; } = true;
        public Windows.UI.Color GraphCustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public int GraphDataPoints { get; set; } = 110;
        public bool MinimizeToTray { get; set; } = true;
        public bool HideSensorsCompletely { get; set; } = true;

        // lives on HardwareMonitorService at runtime, but conceptually belongs with the rest of the app settings for
        // persistence purposes
        public int UpdateIntervalMs { get; set; } = 500;
    }
}