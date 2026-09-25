using Windows.UI;

using FluentSensors.Common.Csv;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;


namespace FluentSensors.Persistence.Models
{
    // plain serializable snapshot of everything in SettingsService that should survive a restart
    // kept separate from SettingsService itself so that class can stay focused on live app state (events, validation)
    // while this stays a simple data container for disk I/O
    public class AppSettingsData
    {
        // --- general settings ---

        // app theme:
        public string AppTheme { get; set; } = "Default";

        // hardware icon colors:
        // colors the hardware category icons on the start, sensors and performance pages; drives
        // HardwareColorMode, which is where every consumer reads it
        // graph colors are deliberately not here, they are a per surface choice, see GraphColorSource below
        public bool UseHardwareIconColors { get; set; } = false;

        // graph:
        // stepline or smooth line rendering; global, applies to every graph in the app
        public GraphLineStyle GraphLineStyle { get; set; } = GraphLineStyle.Stepline;

        // fades the area under the line out towards the bottom instead of filling it in one flat tone; global
        public bool GraphFillFade { get; set; } = false;

        // title bar status:
        public bool StatusReadoutEnabled { get; set; } = true;

        // which of the two title bar status groups are shown, and which one comes first
        public bool StatusLhmGroupEnabled { get; set; } = true;
        public bool StatusWindowsGroupEnabled { get; set; } = true;
        public StatusGroupOrder StatusGroupOrder { get; set; } = StatusGroupOrder.LhmFirst;

        // minimize to system tray:
        public bool MinimizeToTray { get; set; } = true;

        // startup:
        // RunOnStartup and DelayStartup mirror the scheduled task; the task itself is the truth, these two only
        // remember what the settings page should show while it is being built
        public bool RunOnStartup { get; set; } = false;
        public bool DelayStartup { get; set; } = false;
        public bool StartMinimizedToTray { get; set; } = false;

        // which page a launch lands on; read once at startup, so changing it takes effect on the next start
        public StartupPage StartupPage { get; set; } = StartupPage.Start;

        // on by default; a user who does not want the app reaching out on every launch turns it off here
        public bool CheckUpdatesOnStartup { get; set; } = true;

        // update interval:
        // lives on HardwareMonitorService at runtime, but conceptually belongs with the rest of the app settings for
        // persistence purposes
        public int UpdateIntervalMs { get; set; } = 500;


        // --- performance page appearance ---

        // graph:
        // Standard covers the overview blocks, Extended the denser grids (cpu all-threads, gpu extended)
        public double PerformanceGraphTimeSpanSeconds { get; set; } = 45;
        public double PerformanceExtendedGraphTimeSpanSeconds { get; set; } = 30;


        // --- widget window appearance ---

        // background material:
        public string BackdropType { get; set; } = "Mica";
        public float TintOpacity { get; set; } = 0.4f;
        public float LuminosityOpacity { get; set; } = 0.2f;
        public bool UseAccentColor { get; set; } = true;
        public Color CustomTintColor { get; set; } = Color.FromArgb(255, 25, 25, 25);

        // graph:
        // null means this file predates the tri-state selector; LoadFromData then reads the legacy bool below
        public GraphColorSource? GraphColorSource { get; set; } = null;
        public Windows.UI.Color GraphCustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public double GraphTimeSpanSeconds { get; set; } = 45;


        // --- taskbar widget & flyout appearance ---

        // lock taskbar widget position:
        // when true, the taskbar widget cannot be dragged along the taskbar
        public bool TaskbarWidgetPositionLocked { get; set; } = false;

        // graph:
        public GraphColorSource? TaskbarGraphColorSource { get; set; } = null;
        public Windows.UI.Color TaskbarGraphCustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public bool TaskbarUseTransparentGraphBackground { get; set; } = false;
        public double TaskbarGraphTimeSpanSeconds { get; set; } = 20;
        public int TaskbarGraphWidthDip { get; set; } = 120;

        // flyout alignment:
        // flyout horizontal placement over the taskbar widget: "Center", "Left" or "Right"
        public string TaskbarFlyoutAlignment { get; set; } = "Center";

        // flyout background material:
        public string TaskbarBackdropType { get; set; } = "Mica";
        public float TaskbarTintOpacity { get; set; } = 0.4f;
        public float TaskbarLuminosityOpacity { get; set; } = 0.2f;
        public bool TaskbarUseAccentColor { get; set; } = true;
        public Color TaskbarCustomTintColor { get; set; } = Color.FromArgb(255, 25, 25, 25);


        // --- csv logging ---

        // csv format:
        // Local by default: the recording is far more likely to be opened in the spreadsheet app on this machine
        // than fed to a script, and an invariant point decimal is silently misread by a localized one
        public CsvNumberFormat CsvNumberFormat { get; set; } = CsvNumberFormat.Local;

        // 3 was the effective maximum before this became configurable, so an existing settings file keeps the
        // precision it recorded with
        public int CsvDecimalPlaces { get; set; } = 3;

        // off by default: a value with a unit is text and no longer charts, and the unit is in the column header
        // either way
        public bool CsvIncludeUnits { get; set; } = false;

        public CsvPauseSeam CsvPauseSeam { get; set; } = CsvPauseSeam.Gap;


        // --- set outside the settings page ---

        // title bar:
        // set by the toggle button in the title bar, which only hides the readout; StatusReadoutEnabled is what
        // switches the whole thing off, that button included
        public bool StatusReadoutCollapsed { get; set; } = false;

        // start page and update dialog:
        // the exact version the user chose to skip, e.g. "1.4.0"; empty means nothing is skipped
        // the start pages update button is the way back out of a skip, without one a skipped release would stay
        // unreachable until the release after it
        public string SkippedUpdateVersion { get; set; } = "";

        // sensors page:
        // which profile the sensors page opens on; the widget profile is what a fresh install starts with
        public SensorSelectionProfile LastSensorProfile { get; set; } = SensorSelectionProfile.WidgetWindow;

        // csv logger window:
        // folder csv recordings are written into; empty means the logger falls back to Documents\FluentSensors,
        // which keeps the default out of the settings file until the user actually picks something
        public string CsvLogFolder { get; set; } = "";

        // no switch anywhere in the ui:
        public bool HideSensorsCompletely { get; set; } = true;


        // --- legacy, read once on load and never written back ---

        // both graph color selectors were a plain accent/custom switch before Hardware became a third option
        // kept so an existing settings file does not silently drop a custom color the user picked
        public bool UseGraphAccentColor { get; set; } = true;
        public bool TaskbarUseGraphAccentColor { get; set; } = true;
    }
}
