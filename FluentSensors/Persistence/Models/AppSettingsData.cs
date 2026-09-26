using Windows.UI;

using FluentSensors.Common.Csv;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;


namespace FluentSensors.Persistence.Models
{
    // plain serializable snapshot of everything in SettingsService that should survive a restart
    // kept separate from SettingsService itself so that class can stay focused on live app state (events, validation)
    // while this stays a simple data container for disk I/O
    //
    // the initial values below are the defaults as well: a fresh install, a key missing from an older file and every
    // reset start from them, and SettingsService seeds its own fields from here, so a default only ever changes here
    public class AppSettingsData
    {
        // --- general settings ---

        // app theme
        public string AppTheme { get; set; } = "Default";

        // hardware icon colors
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
        public bool StatusLhmGroupEnabled { get; set; } = false;
        public bool StatusWindowsGroupEnabled { get; set; } = true;
        public StatusGroupOrder StatusGroupOrder { get; set; } = StatusGroupOrder.LhmFirst;

        // minimize to system tray
        public bool MinimizeToTray { get; set; } = false;

        // startup:
        // RunOnStartup and DelayStartup mirror the scheduled task; the task itself is the truth, these two only
        // remember what the settings page should show while it is being built
        public bool RunOnStartup { get; set; } = false;
        public bool DelayStartup { get; set; } = false;
        public bool StartMinimizedToTray { get; set; } = true;
        // which page a launch lands on; read once at startup, so changing it takes effect on the next start
        public StartupPage StartupPage { get; set; } = StartupPage.Start;
        // on by default; a user who does not want the app reaching out on every launch turns it off here
        public bool CheckUpdatesOnStartup { get; set; } = true;

        // update interval
        // lives on HardwareMonitorService at runtime, but conceptually belongs with the rest of the app settings for
        // persistence purposes
        public int UpdateIntervalMs { get; set; } = 500;


        // --- performance page appearance ---

        // graph:
        // Standard covers the overview blocks, Extended the denser grids (cpu all-threads, gpu extended)
        public double PerformanceGraphTimeSpanSeconds { get; set; } = 90;
        public double PerformanceExtendedGraphTimeSpanSeconds { get; set; } = 30;


        // --- widget window appearance ---

        // background material:
        public string BackdropType { get; set; } = "Mica";
        public float TintOpacity { get; set; } = 0.4f;
        public float LuminosityOpacity { get; set; } = 0.2f;
        public bool UseAccentColor { get; set; } = true;
        public Color CustomTintColor { get; set; } = Color.FromArgb(255, 25, 25, 25);

        // graph:
        // the color source default is a constant of its own, because the saved selector has to start out null: a
        // file without one either predates the tri-state selector, and LoadFromData reads the legacy bool below for
        // it, or there is no file yet and the default applies
        public const GraphColorSource DefaultGraphColorSource = Common.Sensors.GraphColorSource.Hardware;
        public GraphColorSource? GraphColorSource { get; set; } = null;
        public Windows.UI.Color GraphCustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public double GraphTimeSpanSeconds { get; set; } = 60;


        // --- taskbar widget & flyout appearance ---

        // lock taskbar widget position
        // when true, the taskbar widget cannot be dragged along the taskbar
        public bool TaskbarWidgetPositionLocked { get; set; } = false;

        // graph:
        // the color source default works like the widget one above
        public const GraphColorSource DefaultTaskbarGraphColorSource = Common.Sensors.GraphColorSource.Hardware;
        public GraphColorSource? TaskbarGraphColorSource { get; set; } = null;
        public Windows.UI.Color TaskbarGraphCustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public bool TaskbarUseTransparentGraphBackground { get; set; } = true;
        public double TaskbarGraphTimeSpanSeconds { get; set; } = 35;
        public int TaskbarGraphWidthDip { get; set; } = 100;

        // flyout alignment
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
        public bool StatusReadoutCollapsed { get; set; } = true;

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


        // --- window default sizes ---
        // constants rather than settings, so none of this is written to settings.json
        // a window opens at these when it has no saved size yet or its state was reset; the widget and the flyout
        // work their height out from them on every open, since it follows the pinned sensors
        // all in DIP; the minimum sizes stay with each window in its own file

        // main window:
        // minimum 600 x 400, see MainWindow.xaml.cs
        public const double MainWindowDefaultWidthDip = 600;
        public const double MainWindowDefaultHeightDip = 770;

        // hidden sensors window:
        // minimum 280 x 200, see HiddenSensorsWindow.xaml.cs
        public const double HiddenSensorsWindowDefaultWidthDip = 340;
        public const double HiddenSensorsWindowDefaultHeightDip = 510;

        // widget window:
        // the height is one panel per pinned sensor plus the title bar
        // minimum 220 wide, and 90 per pinned sensor plus the title bar high, see WidgetWindow.xaml.cs
        public const double WidgetWindowDefaultWidthDip = 240;
        public const double WidgetWindowDefaultPanelHeightDip = 100;

        // csv logger window:
        // only the width, the height always follows the content
        // minimum 225 wide, see CsvLoggerWindow.xaml.cs
        public const double CsvLoggerWindowDefaultWidthDip = 225;

        // taskbar flyout:
        // the width is fixed, the height is one graph per pinned sensor plus the bottom bar
        // no minimum, it cannot be resized by hand
        public const double TaskbarFlyoutWidthDip = 250;
        public const double TaskbarFlyoutGraphHeightDip = 100;


        // --- legacy, read once on load and never written back ---

        // both graph color selectors were a plain accent/custom switch before Hardware became a third option
        // kept so an existing settings file does not silently drop a custom color the user picked
        // null means the file never had them, which leaves a fresh install on the defaults above
        public bool? UseGraphAccentColor { get; set; } = null;
        public bool? TaskbarUseGraphAccentColor { get; set; } = null;
    }
}
