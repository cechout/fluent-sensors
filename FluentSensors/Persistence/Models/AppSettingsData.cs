using System.Collections.Generic;
using Windows.UI;

using FluentSensors.Common.Csv;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;


namespace FluentSensors.Persistence.Models
{
    // the persisted settings:
    // a plain serializable snapshot of SettingsService, which keeps the live state (events, validation)
    // the initializers are the defaults for a fresh install, a missing key and every reset; SettingsService seeds its
    // fields from here, so a default only changes here
    public class AppSettingsData
    {
        // --- general settings ---

        // app theme
        public string AppTheme { get; set; } = "Default";

        // app language; a tag from AppLanguage.Supported, or Default for the Windows display language
        public string AppLanguage { get; set; } = "en-US";
        // the technical terms (AppTerms) in english whatever the app language
        public bool TechnicalTermsInEnglish { get; set; } = false;

        // hardware icon colors; the category icons on the start, sensors and performance pages, read through
        // HardwareColorMode (graph colors are per surface, see GraphColorSource)
        public bool UseHardwareIconColors { get; set; } = false;

        // graph; global, every graph in the app
        public GraphLineStyle GraphLineStyle { get; set; } = GraphLineStyle.Stepline;
        // the area under the line fades towards the bottom instead of one flat tone
        public bool GraphFillFade { get; set; } = false;

        // data units; bytes or bits, separately for sizes (SmallData, Data) and speeds (Throughput); every displayed
        // value, never the csv recording
        public DataUnitBasis DataSizeUnitBasis { get; set; } = DataUnitBasis.Byte;
        public DataUnitBasis DataSpeedUnitBasis { get; set; } = DataUnitBasis.Byte;

        // title bar status
        public bool StatusReadoutEnabled { get; set; } = true;
        // the two groups and their order
        public bool StatusLhmGroupEnabled { get; set; } = false;
        public bool StatusWindowsGroupEnabled { get; set; } = true;
        public StatusGroupOrder StatusGroupOrder { get; set; } = StatusGroupOrder.LhmFirst;

        // minimize to system tray
        public bool MinimizeToTray { get; set; } = false;

        // startup; RunOnStartup and DelayStartup mirror the scheduled task, which is the truth
        public bool RunOnStartup { get; set; } = false;
        public bool DelayStartup { get; set; } = false;
        public bool StartMinimizedToTray { get; set; } = true;
        // the landing page; read once at startup
        public StartupPage StartupPage { get; set; } = StartupPage.Start;
        public bool CheckUpdatesOnStartup { get; set; } = true;

        // update interval; (lives on HardwareMonitorService at runtime)
        public int UpdateIntervalMs { get; set; } = 500;


        // --- performance page appearance ---

        // graph; Standard for the overview blocks, CpuExtended and GpuExtended for the dense grids (cpu all-threads,
        // gpu extended)
        public double PerformanceGraphTimeSpanSeconds { get; set; } = 90;
        public double PerformanceCpuExtendedGraphTimeSpanSeconds { get; set; } = 45;
        public double PerformanceGpuExtendedGraphTimeSpanSeconds { get; set; } = 60;


        // --- widget window appearance ---

        // background material, shared with the csv logger window; the default is a constant, the saved value starts
        // null (a file without one either predates it and LoadFromData maps the legacy BackdropType, or there is no
        // file yet)
        public const BackdropMaterial DefaultBackgroundMaterial = BackdropMaterial.SystemAcrylic;
        public BackdropMaterial? BackgroundMaterial { get; set; } = null;
        public float TintOpacity { get; set; } = 0.4f;
        public float LuminosityOpacity { get; set; } = 0.2f;
        public bool UseAccentColor { get; set; } = true;
        public Color CustomTintColor { get; set; } = Color.FromArgb(255, 25, 25, 25);

        // graph; the color source default is a constant, the saved selector starts null (a file without one either
        // predates it and LoadFromData reads the legacy bool, or there is no file yet)
        public const GraphColorSource DefaultGraphColorSource = Common.Sensors.GraphColorSource.Hardware;
        public GraphColorSource? GraphColorSource { get; set; } = null;
        public Windows.UI.Color GraphCustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public double GraphTimeSpanSeconds { get; set; } = 60;


        // --- taskbar widget & flyout appearance ---

        // no dragging the taskbar widget along the taskbar
        public bool TaskbarWidgetPositionLocked { get; set; } = false;

        // graph; the color source default works like the widget one
        public const GraphColorSource DefaultTaskbarGraphColorSource = Common.Sensors.GraphColorSource.Hardware;
        public GraphColorSource? TaskbarGraphColorSource { get; set; } = null;
        public Windows.UI.Color TaskbarGraphCustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public bool TaskbarUseTransparentGraphBackground { get; set; } = true;

        // per taskbar edge ("Bottom", "Top", "Left", "Right"): time range, graph width, flyout alignment, side layout;
        // defaults in TaskbarEdgeSettings
        // null in an older file, SettingsService seeds every edge from the legacy values below
        public Dictionary<string, TaskbarEdgeSettings>? TaskbarEdges { get; set; } = null;

        // flyout graphs; the same on every edge, the flyout keeps its shape there
        public double TaskbarFlyoutGraphTimeSpanSeconds { get; set; } = 90;
        public double TaskbarFlyoutGraphHeightDip { get; set; } = 100; // one graph slot

        // the global shortcut that opens the flyout; none by default, so no key combination is taken from other apps
        public KeyboardShortcut? TaskbarFlyoutShortcut { get; set; } = null;

        // flyout background material; the default works like the widget one
        public const BackdropMaterial DefaultTaskbarBackgroundMaterial = BackdropMaterial.SystemAcrylic;
        public BackdropMaterial? TaskbarBackgroundMaterial { get; set; } = null;
        public float TaskbarTintOpacity { get; set; } = 0.4f;
        public float TaskbarLuminosityOpacity { get; set; } = 0.2f;
        public bool TaskbarUseAccentColor { get; set; } = true;
        public Color TaskbarCustomTintColor { get; set; } = Color.FromArgb(255, 25, 25, 25);


        // --- csv logging ---

        // csv format; Local by default, a recording mostly opens in a local spreadsheet app, which
        // misreads an invariant decimal point
        public CsvNumberFormat CsvNumberFormat { get; set; } = CsvNumberFormat.Local;
        // 3 keeps an older settings file at the precision it recorded with
        public int CsvDecimalPlaces { get; set; } = 3;
        // off by default; a value with a unit is text and no longer charts (the header has the unit anyway)
        public bool CsvIncludeUnits { get; set; } = false;
        public CsvPauseSeam CsvPauseSeam { get; set; } = CsvPauseSeam.Gap;


        // --- set outside the settings page ---

        // title bar; the toggle button only hides the readout, StatusReadoutEnabled switches it off with the button
        public bool StatusReadoutCollapsed { get; set; } = true;

        // start page and update dialog; the skipped version, e.g. "1.4.0", empty = none (the start
        // page button is the way back)
        public string SkippedUpdateVersion { get; set; } = "";

        // sensors page; the profile it opens on, and the widget window (0 based) of the widget profile
        public SensorSelectionProfile LastSensorProfile { get; set; } = SensorSelectionProfile.WidgetWindow;
        public int LastWidgetWindowIndex { get; set; } = 0;

        // csv logger window; the recording folder, empty = Documents\FluentSensors
        public string CsvLogFolder { get; set; } = "";

        // no switch in the ui
        public bool HideSensorsCompletely { get; set; } = true;


        // --- window default sizes ---
        // in DIP; constants, never written to settings.json
        // a window opens at these without a saved size; widget and flyout derive their height from them on every open
        // (the minimum sizes stay in each window)

        // main window; (minimum 600 x 400)
        public const double MainWindowDefaultWidthDip = 600;
        public const double MainWindowDefaultHeightDip = 770;

        // hidden sensors window; (minimum 280 x 200)
        public const double HiddenSensorsWindowDefaultWidthDip = 340;
        public const double HiddenSensorsWindowDefaultHeightDip = 510;

        // widget window; the height is one panel per pinned sensor plus the title bar (minimum 220 wide, 90 per sensor)
        public const double WidgetWindowDefaultWidthDip = 240;
        public const double WidgetWindowDefaultPanelHeightDip = 100;

        // csv logger window; the width only, the height follows the content (minimum 225 wide)
        public const double CsvLoggerWindowDefaultWidthDip = 225;

        // taskbar flyout; fixed width, the height follows the pinned sensors and TaskbarFlyoutGraphHeightDip (not
        // resizable)
        public const double TaskbarFlyoutWidthDip = 280;


        // --- legacy, read once on load and never written back ---

        // the accent/custom switch the graph color selectors replaced, so an older file keeps its
        // custom color; null = never had it
        public bool? UseGraphAccentColor { get; set; } = null;
        public bool? TaskbarUseGraphAccentColor { get; set; } = null;

        // the single taskbar values TaskbarEdges replaced; SettingsService copies them onto all four edges once
        public double? TaskbarGraphTimeSpanSeconds { get; set; } = null;
        public int? TaskbarGraphWidthDip { get; set; } = null;
        public string? TaskbarFlyoutAlignment { get; set; } = null;

        // the shared extended range the cpu and gpu ranges replaced; SettingsService copies it onto both once
        public double? PerformanceExtendedGraphTimeSpanSeconds { get; set; } = null;

        // the three material tags ("Mica", "Acrylic", "None") the four BackdropMaterial values replaced; a new key
        // instead of new tags, so an older version reading this file never misreads one
        public string? BackdropType { get; set; } = null;
        public string? TaskbarBackdropType { get; set; } = null;
    }
}
