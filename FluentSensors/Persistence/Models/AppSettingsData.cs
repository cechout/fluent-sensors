using Windows.UI;


namespace FluentSensors.Persistence.Models
{
    // plain serializable snapshot of everything in SettingsService that should survive a restart
    // kept separate from SettingsService itself so that class can stay focused on live app state (events, validation)
    // while this stays a simple data container for disk I/O
    public class AppSettingsData
    {
        public string AppTheme { get; set; } = "Default";

        // widget window appearance
        public string BackdropType { get; set; } = "Mica";
        public float TintOpacity { get; set; } = 0.4f;
        public float LuminosityOpacity { get; set; } = 0.2f;
        public bool UseAccentColor { get; set; } = true;
        public Color CustomTintColor { get; set; } = Color.FromArgb(255, 25, 25, 25);
        public bool UseGraphAccentColor { get; set; } = true;
        public Windows.UI.Color GraphCustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public double GraphTimeSpanSeconds { get; set; } = 45;

        // taskbar ecosystem (taskbar widget + flyout window) appearance
        public string TaskbarBackdropType { get; set; } = "Acrylic";
        public float TaskbarTintOpacity { get; set; } = 0.4f;
        public float TaskbarLuminosityOpacity { get; set; } = 0.2f;
        public bool TaskbarUseAccentColor { get; set; } = true;
        public Color TaskbarCustomTintColor { get; set; } = Color.FromArgb(255, 25, 25, 25);
        public bool TaskbarUseGraphAccentColor { get; set; } = true;
        public Windows.UI.Color TaskbarGraphCustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public double TaskbarGraphTimeSpanSeconds { get; set; } = 45;
        public int TaskbarGraphWidthDip { get; set; } = 120;
        public bool TaskbarUseTransparentGraphBackground { get; set; } = false;

        // flyout horizontal placement over the taskbar widget: "Center", "Left" or "Right"
        public string TaskbarFlyoutAlignment { get; set; } = "Center";

        // when true, the taskbar widget cannot be dragged along the taskbar
        public bool TaskbarWidgetPositionLocked { get; set; } = false;

        public bool MinimizeToTray { get; set; } = true;
        public bool HideSensorsCompletely { get; set; } = true;
        public bool StatusReadoutEnabled { get; set; } = true;

        // lives on HardwareMonitorService at runtime, but conceptually belongs with the rest of the app settings for
        // persistence purposes
        public int UpdateIntervalMs { get; set; } = 500;
    }
}