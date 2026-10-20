using System.Collections.Generic;
using Windows.UI;

using FluentSensors.Common.Csv;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;


namespace FluentSensors.Persistence.Models
{
    // the persisted settings:
    // a plain serializable snapshot of SettingsService, which keeps the live state (events, validation);
    // The initializers are the defaults for a fresh install, a missing key and every reset; SettingsService seeds its
    // fields from here, so a default only changes here
    // the sections and their entries follow the settings page top to bottom
    public class AppSettingsData
    {
        // --- general settings ---

        // app theme
        public string AppTheme { get; set; } = "Default";
        public bool UseHardwareIconColors { get; set; } = false;

        public GraphLineStyle GraphLineStyle { get; set; } = GraphLineStyle.Stepline; // graph; global, every graph in the app
        public bool GraphFillFade { get; set; } = false;

        public string AppLanguage { get; set; } = "en-US"; // a tag from AppLanguage.Supported, or Default for the Windows display language
        public bool TechnicalTermsInEnglish { get; set; } = false;

        // data units; bytes or bits, separately for sizes (SmallData, Data) and speeds (Throughput)
        // (only displayed values, never the csv recording)
        public DataUnitBasis DataSizeUnitBasis { get; set; } = DataUnitBasis.Byte;
        public DataUnitBasis DataSpeedUnitBasis { get; set; } = DataUnitBasis.Byte;

        // title bar status
        public bool StatusReadoutEnabled { get; set; } = true;
        public bool StatusLhmGroupEnabled { get; set; } = false;
        public bool StatusWindowsGroupEnabled { get; set; } = true;
        public StatusGroupOrder StatusGroupOrder { get; set; } = StatusGroupOrder.LhmFirst;

        // minimize to system tray
        public bool MinimizeToTray { get; set; } = false;

        // startup; RunOnStartup and DelayStartup mirror the scheduled task, which is the truth
        public bool RunOnStartup { get; set; } = false;
        public bool DelayStartup { get; set; } = false;
        public bool StartMinimizedToTray { get; set; } = true;
        public StartupPage StartupPage { get; set; } = StartupPage.Start; // the landing page; read once at startup
        public bool CheckUpdatesOnStartup { get; set; } = true;


        // --- engine settings ---

        // update interval; (lives on HardwareMonitorService at runtime)
        public int UpdateIntervalMs { get; set; } = 500;

        // LibreHardwareMonitor
        public bool ShowLhmSourceIcon { get; set; } = false; // the source icon behind a sensor name (see SensorSourceIcon)
        public List<string> DisabledLhmHardware { get; set; } = new();

        // Windows
        public bool ShowWindowsSourceIcon { get; set; } = true; // the source icon behind a sensor name
        public bool WinStorageSensorsEnabled { get; set; } = false;
        public bool WinWlanSensorsEnabled { get; set; } = false;
        public bool WinDisplayEnabled { get; set; } = false;


        // --- widget window appearance ---

        // background material; the default is a constant, the saved value starts null (a file without one either
        // predates it and LoadFromData maps the legacy BackdropType, or there is no file yet)
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
        // no card; the time range of a widget window without a saved one
        public double GraphTimeSpanSeconds { get; set; } = 60;


        // --- widgets board ---
        // (store build only)

        public GraphColorSource WidgetBoardGraphColorSource { get; set; } = Common.Sensors.GraphColorSource.Hardware;
        public Windows.UI.Color WidgetBoardGraphCustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public double WidgetBoardGraphTimeSpanSeconds { get; set; } = 60;


        // --- csv logging ---

        // csv logger window background material; all null in a file from before it had its own, LoadFromData then
        // takes the widget values, which the logger shared until then
        public BackdropMaterial? CsvBackgroundMaterial { get; set; } = null;
        public float? CsvTintOpacity { get; set; } = null;
        public float? CsvLuminosityOpacity { get; set; } = null;
        public bool? CsvUseAccentColor { get; set; } = null;
        public Color? CsvCustomTintColor { get; set; } = null;

        public CsvNumberFormat CsvNumberFormat { get; set; } = CsvNumberFormat.Local; // csv format; Local by default
        public int CsvDecimalPlaces { get; set; } = 3; // 3 keeps an older settings file at the precision it recorded with
        public bool CsvIncludeUnits { get; set; } = false; // a value with a unit is text and no longer charts (the header has the unit anyway)
        public CsvPauseSeam CsvPauseSeam { get; set; } = CsvPauseSeam.Gap;


        // --- taskbar widget & flyout appearance ---

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

        // flyout background material; the default works like the widget one
        public const BackdropMaterial DefaultTaskbarBackgroundMaterial = BackdropMaterial.SystemAcrylic;
        public BackdropMaterial? TaskbarBackgroundMaterial { get; set; } = null;
        public float TaskbarTintOpacity { get; set; } = 0.4f;
        public float TaskbarLuminosityOpacity { get; set; } = 0.2f;
        public bool TaskbarUseAccentColor { get; set; } = true;
        public Color TaskbarCustomTintColor { get; set; } = Color.FromArgb(255, 25, 25, 25);


        // --- taskbar widget & flyout behavior ---

        public bool TaskbarWidgetPositionLocked { get; set; } = false; // no dragging the taskbar widget along the taskbar
        public KeyboardShortcut? TaskbarFlyoutShortcut { get; set; } = null; // the global shortcut that opens the flyout


        // --- set outside the settings page ---

        public bool StatusReadoutCollapsed { get; set; } = true; // title bar; the toggle button only hides the readout
        public string SkippedUpdateVersion { get; set; } = "";

        // sensors page; the profile it opens on, and the widget window (0 based) of the widget profile
        public SensorSelectionProfile LastSensorProfile { get; set; } = SensorSelectionProfile.WidgetWindow;
        public int LastWidgetWindowIndex { get; set; } = 0;

        // sensor type colors; the check boxes of the sensors page and the graphs of the performance page
        public bool SensorsUseSensorTypeColors { get; set; } = true;
        public bool PerformanceUseSensorTypeColors { get; set; } = false;

        // performance page graph; Standard for the overview blocks, CpuExtended and GpuExtended for the dense grids
        // (cpu all-threads, gpu extended)
        public double PerformanceGraphTimeSpanSeconds { get; set; } = 90;
        public double PerformanceCpuExtendedGraphTimeSpanSeconds { get; set; } = 45;
        public double PerformanceGpuExtendedGraphTimeSpanSeconds { get; set; } = 60;

        // csv logger window; the recording folder, empty = Documents\FluentSensors
        public string CsvLogFolder { get; set; } = "";

        // (currently no switch in the ui)
        public bool HideSensorsCompletely { get; set; } = true;


        // --- window default sizes ---
        // in DIP; constants, never written to settings.json
        // a window opens at these without a saved size; widget and flyout derive their height from them on every open
        // (the minimum sizes stay in each window)

        // main window; (minimum 600 x 400)
        public const double MainWindowDefaultWidthDip = 640;
        public const double MainWindowDefaultHeightDip = 770;

        // hidden sensors window; (minimum 280 x 200)
        public const double HiddenSensorsWindowDefaultWidthDip = 340;
        public const double HiddenSensorsWindowDefaultHeightDip = 510;

        // widget window; the height is one panel per pinned sensor plus the title bar (minimum 220 wide, 90 per sensor)
        public const double WidgetWindowDefaultWidthDip = 240;
        public const double WidgetWindowDefaultPanelHeightDip = 100;

        // csv logger window; the width only, the height follows the content (minimum 225 wide)
        public const double CsvLoggerWindowDefaultWidthDip = 225;

        // taskbar flyout; fixed width, the height follows the pinned sensors
        // - windows notification center (Win+N) width: 336 dip
        public const double TaskbarFlyoutWidthDip = 300;


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

        // the one switch both pages shared before each had its own; SettingsService copies it onto both once
        public bool? UseSensorTypeColors { get; set; } = null;

        // the three material tags ("Mica", "Acrylic", "None") the four BackdropMaterial values replaced; a new key
        // instead of new tags, so an older version reading this file never misreads one
        public string? BackdropType { get; set; } = null;
        public string? TaskbarBackdropType { get; set; } = null;
    }
}
