using System;
using System.Collections.Generic;
using Windows.UI;

using FluentSensors.Persistence.Models;
using FluentSensors.Core;
using FluentSensors.Common.Csv;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;


namespace FluentSensors.Persistence.Services
{
    public class SettingsService
    {
        // === singleton instance ===

        // the defaults every field starts from, so a default is only written in AppSettingsData; (above _instance,
        // static fields initialize in order)
        private static readonly AppSettingsData Defaults = new AppSettingsData();

        // the per edge keys, ScreenEdge names; above _instance too
        private static readonly string[] TaskbarEdgeNames = { "Bottom", "Top", "Left", "Right" };

        private static readonly SettingsService _instance = new SettingsService();
        public static SettingsService Instance => _instance;


        // === constructor ===

        private SettingsService() { }


        // === public api ===

        // properties
        private string _appTheme = Defaults.AppTheme;
        public string AppTheme
        {
            get => _appTheme;
            set
            {
                if (_appTheme != value)
                {
                    _appTheme = value;
                    ThemeChanged?.Invoke(_appTheme);
                    SaveDebounced();
                }
            }
        }

        // --- widget window appearance ---

        private string _backdropType = Defaults.BackdropType;
        public string BackdropType
        {
            get => _backdropType;
            set
            {
                if (_backdropType != value)
                {
                    _backdropType = value;
                    BackdropTypeChanged?.Invoke(_backdropType);
                    SaveDebounced();
                }
            }
        }

        private float _tintOpacity = Defaults.TintOpacity;
        public float TintOpacity
        {
            get => _tintOpacity;
            set
            {
                if (_tintOpacity != value)
                {
                    _tintOpacity = value;
                    OpacityChanged?.Invoke(_tintOpacity, _luminosityOpacity);
                    SaveDebounced();
                }
            }
        }

        private float _luminosityOpacity = Defaults.LuminosityOpacity;
        public float LuminosityOpacity
        {
            get => _luminosityOpacity;
            set
            {
                if (_luminosityOpacity != value)
                {
                    _luminosityOpacity = value;
                    OpacityChanged?.Invoke(_tintOpacity, _luminosityOpacity);
                    SaveDebounced();
                }
            }
        }

        private bool _useAccentColor = Defaults.UseAccentColor;
        public bool UseAccentColor
        {
            get => _useAccentColor;
            set
            {
                if (_useAccentColor != value)
                {
                    _useAccentColor = value;
                    TintColorChanged?.Invoke(_useAccentColor, _customTintColor);
                    SaveDebounced();
                }
            }
        }

        private Color _customTintColor = Defaults.CustomTintColor;
        public Color CustomTintColor
        {
            get => _customTintColor;
            set
            {
                if (_customTintColor != value)
                {
                    _customTintColor = value;
                    TintColorChanged?.Invoke(_useAccentColor, _customTintColor);
                    SaveDebounced();
                }
            }
        }

        // the widget graph line colour
        private GraphColorSource _graphColorSource = AppSettingsData.DefaultGraphColorSource;
        public GraphColorSource GraphColorSource
        {
            get => _graphColorSource;
            set
            {
                if (_graphColorSource != value)
                {
                    _graphColorSource = value;
                    GraphColorChanged?.Invoke(_graphColorSource, _graphCustomColor);
                    SaveDebounced();
                }
            }
        }

        private Windows.UI.Color _graphCustomColor = Defaults.GraphCustomColor;
        public Windows.UI.Color GraphCustomColor
        {
            get => _graphCustomColor;
            set
            {
                if (_graphCustomColor != value)
                {
                    _graphCustomColor = value;
                    GraphColorChanged?.Invoke(_graphColorSource, _graphCustomColor);
                    SaveDebounced();
                }
            }
        }

        // seconds of history on a graph without its own override (the widget)
        private double _graphTimeSpanSeconds = Defaults.GraphTimeSpanSeconds;
        public double GraphTimeSpanSeconds
        {
            get => _graphTimeSpanSeconds;
            set
            {
                if (_graphTimeSpanSeconds != value)
                {
                    _graphTimeSpanSeconds = value;
                    GraphTimeSpanChanged?.Invoke(_graphTimeSpanSeconds);
                    SaveDebounced();
                }
            }
        }

        // stepline or smooth; global, every scope
        private GraphLineStyle _graphLineStyle = Defaults.GraphLineStyle;
        public GraphLineStyle GraphLineStyle
        {
            get => _graphLineStyle;
            set
            {
                if (_graphLineStyle != value)
                {
                    _graphLineStyle = value;
                    GraphLineStyleChanged?.Invoke(_graphLineStyle);
                    SaveDebounced();
                }
            }
        }

        // the area under the line fades towards the bottom; global like GraphLineStyle
        private bool _graphFillFade = Defaults.GraphFillFade;
        public bool GraphFillFade
        {
            get => _graphFillFade;
            set
            {
                if (_graphFillFade != value)
                {
                    _graphFillFade = value;
                    GraphFillFadeChanged?.Invoke(_graphFillFade);
                    SaveDebounced();
                }
            }
        }

        // hardware category icon color; mirrored onto HardwareColorMode (HardwareGroupInfo sits in Common, which must
        // not reach into persistence)
        private bool _useHardwareIconColors = Defaults.UseHardwareIconColors;
        public bool UseHardwareIconColors
        {
            get => _useHardwareIconColors;
            set
            {
                if (_useHardwareIconColors != value)
                {
                    _useHardwareIconColors = value;
                    HardwareColorMode.UseIconColors = value;
                    HardwareIconColorsChanged?.Invoke();
                    SaveDebounced();
                }
            }
        }

        // bytes or bits for sizes (SmallData, Data) and speeds (Throughput); mirrored onto
        // SensorUnitFormatter in Common, like above
        private DataUnitBasis _dataSizeUnitBasis = Defaults.DataSizeUnitBasis;
        public DataUnitBasis DataSizeUnitBasis
        {
            get => _dataSizeUnitBasis;
            set
            {
                if (_dataSizeUnitBasis != value)
                {
                    _dataSizeUnitBasis = value;
                    SensorUnitFormatter.DataSizeBasis = value;
                    DataUnitBasisChanged?.Invoke();
                    SaveDebounced();
                }
            }
        }

        private DataUnitBasis _dataSpeedUnitBasis = Defaults.DataSpeedUnitBasis;
        public DataUnitBasis DataSpeedUnitBasis
        {
            get => _dataSpeedUnitBasis;
            set
            {
                if (_dataSpeedUnitBasis != value)
                {
                    _dataSpeedUnitBasis = value;
                    SensorUnitFormatter.DataSpeedBasis = value;
                    DataUnitBasisChanged?.Invoke();
                    SaveDebounced();
                }
            }
        }


        // --- performance page graphs ---

        // seconds of history on the overview graphs
        private double _performanceGraphTimeSpanSeconds = Defaults.PerformanceGraphTimeSpanSeconds;
        public double PerformanceGraphTimeSpanSeconds
        {
            get => _performanceGraphTimeSpanSeconds;
            set
            {
                if (_performanceGraphTimeSpanSeconds != value)
                {
                    _performanceGraphTimeSpanSeconds = value;
                    PerformanceGraphTimeSpanChanged?.Invoke();
                    SaveDebounced();
                }
            }
        }

        // seconds of history on the dense grids (cpu all-threads, gpu extended); (small and many,
        // they want a shorter window)
        private double _performanceExtendedGraphTimeSpanSeconds = Defaults.PerformanceExtendedGraphTimeSpanSeconds;
        public double PerformanceExtendedGraphTimeSpanSeconds
        {
            get => _performanceExtendedGraphTimeSpanSeconds;
            set
            {
                if (_performanceExtendedGraphTimeSpanSeconds != value)
                {
                    _performanceExtendedGraphTimeSpanSeconds = value;
                    PerformanceGraphTimeSpanChanged?.Invoke();
                    SaveDebounced();
                }
            }
        }


        // --- taskbar widget and flyout appearance ---

        private string _taskbarBackdropType = Defaults.TaskbarBackdropType;
        public string TaskbarBackdropType
        {
            get => _taskbarBackdropType;
            set
            {
                if (_taskbarBackdropType != value)
                {
                    _taskbarBackdropType = value;
                    TaskbarBackdropTypeChanged?.Invoke(_taskbarBackdropType);
                    SaveDebounced();
                }
            }
        }

        private float _taskbarTintOpacity = Defaults.TaskbarTintOpacity;
        public float TaskbarTintOpacity
        {
            get => _taskbarTintOpacity;
            set
            {
                if (_taskbarTintOpacity != value)
                {
                    _taskbarTintOpacity = value;
                    TaskbarOpacityChanged?.Invoke(_taskbarTintOpacity, _taskbarLuminosityOpacity);
                    SaveDebounced();
                }
            }
        }

        private float _taskbarLuminosityOpacity = Defaults.TaskbarLuminosityOpacity;
        public float TaskbarLuminosityOpacity
        {
            get => _taskbarLuminosityOpacity;
            set
            {
                if (_taskbarLuminosityOpacity != value)
                {
                    _taskbarLuminosityOpacity = value;
                    TaskbarOpacityChanged?.Invoke(_taskbarTintOpacity, _taskbarLuminosityOpacity);
                    SaveDebounced();
                }
            }
        }

        private bool _taskbarUseAccentColor = Defaults.TaskbarUseAccentColor;
        public bool TaskbarUseAccentColor
        {
            get => _taskbarUseAccentColor;
            set
            {
                if (_taskbarUseAccentColor != value)
                {
                    _taskbarUseAccentColor = value;
                    TaskbarTintColorChanged?.Invoke(_taskbarUseAccentColor, _taskbarCustomTintColor);
                    SaveDebounced();
                }
            }
        }

        private Color _taskbarCustomTintColor = Defaults.TaskbarCustomTintColor;
        public Color TaskbarCustomTintColor
        {
            get => _taskbarCustomTintColor;
            set
            {
                if (_taskbarCustomTintColor != value)
                {
                    _taskbarCustomTintColor = value;
                    TaskbarTintColorChanged?.Invoke(_taskbarUseAccentColor, _taskbarCustomTintColor);
                    SaveDebounced();
                }
            }
        }

        private GraphColorSource _taskbarGraphColorSource = AppSettingsData.DefaultTaskbarGraphColorSource;
        public GraphColorSource TaskbarGraphColorSource
        {
            get => _taskbarGraphColorSource;
            set
            {
                if (_taskbarGraphColorSource != value)
                {
                    _taskbarGraphColorSource = value;
                    TaskbarGraphColorChanged?.Invoke(_taskbarGraphColorSource, _taskbarGraphCustomColor);
                    SaveDebounced();
                }
            }
        }

        private Windows.UI.Color _taskbarGraphCustomColor = Defaults.TaskbarGraphCustomColor;
        public Windows.UI.Color TaskbarGraphCustomColor
        {
            get => _taskbarGraphCustomColor;
            set
            {
                if (_taskbarGraphCustomColor != value)
                {
                    _taskbarGraphCustomColor = value;
                    TaskbarGraphColorChanged?.Invoke(_taskbarGraphColorSource, _taskbarGraphCustomColor);
                    SaveDebounced();
                }
            }
        }

        // the taskbar widget graphs drop their card tint
        private bool _taskbarUseTransparentGraphBackground = Defaults.TaskbarUseTransparentGraphBackground;
        public bool TaskbarUseTransparentGraphBackground
        {
            get => _taskbarUseTransparentGraphBackground;
            set
            {
                if (_taskbarUseTransparentGraphBackground != value)
                {
                    _taskbarUseTransparentGraphBackground = value;
                    TaskbarGraphBackgroundChanged?.Invoke(_taskbarUseTransparentGraphBackground);
                    SaveDebounced();
                }
            }
        }

        // no dragging the taskbar widget along the taskbar
        private bool _taskbarWidgetPositionLocked = Defaults.TaskbarWidgetPositionLocked;
        public bool TaskbarWidgetPositionLocked
        {
            get => _taskbarWidgetPositionLocked;
            set
            {
                if (_taskbarWidgetPositionLocked != value)
                {
                    _taskbarWidgetPositionLocked = value;
                    TaskbarWidgetPositionLockedChanged?.Invoke(_taskbarWidgetPositionLocked);
                    SaveDebounced();
                }
            }
        }

        // seconds of history on the flyout graphs; shared by every edge
        private double _taskbarFlyoutGraphTimeSpanSeconds = Defaults.TaskbarFlyoutGraphTimeSpanSeconds;
        public double TaskbarFlyoutGraphTimeSpanSeconds
        {
            get => _taskbarFlyoutGraphTimeSpanSeconds;
            set
            {
                if (_taskbarFlyoutGraphTimeSpanSeconds != value)
                {
                    _taskbarFlyoutGraphTimeSpanSeconds = value;
                    TaskbarFlyoutGraphTimeSpanChanged?.Invoke(_taskbarFlyoutGraphTimeSpanSeconds);
                    SaveDebounced();
                }
            }
        }

        // one flyout graph slot in DIP; shared by every edge
        private double _taskbarFlyoutGraphHeightDip = Defaults.TaskbarFlyoutGraphHeightDip;
        public double TaskbarFlyoutGraphHeightDip
        {
            get => _taskbarFlyoutGraphHeightDip;
            set
            {
                if (_taskbarFlyoutGraphHeightDip != value)
                {
                    _taskbarFlyoutGraphHeightDip = value;
                    TaskbarFlyoutGraphHeightChanged?.Invoke(_taskbarFlyoutGraphHeightDip);
                    SaveDebounced();
                }
            }
        }


        // --- per taskbar position ---

        // time range, graph width, flyout alignment and side layout per taskbar edge (TaskbarEdgeSettings); the
        // properties below go through ActiveTaskbarEdge, so consumers follow a taskbar move
        private Dictionary<string, TaskbarEdgeSettings> _taskbarEdges = SeedTaskbarEdges(null, Defaults);
        private string _activeTaskbarEdge = "Bottom";

        private TaskbarEdgeSettings ActiveEdge => _taskbarEdges[_activeTaskbarEdge];

        // the current taskbar edge, a ScreenEdge name; set by the taskbar widget and the settings page, never persisted
        // a switch raises the change event of every value that differs between the two edges, like an edit would
        public string ActiveTaskbarEdge
        {
            get => _activeTaskbarEdge;
            set
            {
                if (_activeTaskbarEdge == value || !_taskbarEdges.ContainsKey(value)) return;

                var previous = ActiveEdge;
                _activeTaskbarEdge = value;
                var current = ActiveEdge;

                ActiveTaskbarEdgeChanged?.Invoke(_activeTaskbarEdge);

                if (previous.GraphTimeSpanSeconds != current.GraphTimeSpanSeconds)
                {
                    TaskbarGraphTimeSpanChanged?.Invoke(current.GraphTimeSpanSeconds);
                }
                if (previous.GraphWidthDip != current.GraphWidthDip)
                {
                    TaskbarGraphWidthChanged?.Invoke(current.GraphWidthDip);
                }
                if (previous.FlyoutAlignment != current.FlyoutAlignment)
                {
                    TaskbarFlyoutAlignmentChanged?.Invoke(current.FlyoutAlignment);
                }
                if (previous.SideGraphDirection != current.SideGraphDirection)
                {
                    TaskbarSideGraphDirectionChanged?.Invoke(current.SideGraphDirection);
                }
                if (previous.SideTitleLines != current.SideTitleLines)
                {
                    TaskbarSideTitleLinesChanged?.Invoke(current.SideTitleLines);
                }
            }
        }

        public double TaskbarGraphTimeSpanSeconds
        {
            get => ActiveEdge.GraphTimeSpanSeconds;
            set
            {
                if (ActiveEdge.GraphTimeSpanSeconds != value)
                {
                    ActiveEdge.GraphTimeSpanSeconds = value;
                    TaskbarGraphTimeSpanChanged?.Invoke(value);
                    SaveDebounced();
                }
            }
        }

        public int TaskbarGraphWidthDip
        {
            get => ActiveEdge.GraphWidthDip;
            set
            {
                if (ActiveEdge.GraphWidthDip != value)
                {
                    ActiveEdge.GraphWidthDip = value;
                    TaskbarGraphWidthChanged?.Invoke(value);
                    SaveDebounced();
                }
            }
        }

        // "Center", "Left" or "Right"
        public string TaskbarFlyoutAlignment
        {
            get => ActiveEdge.FlyoutAlignment;
            set
            {
                if (ActiveEdge.FlyoutAlignment != value)
                {
                    ActiveEdge.FlyoutAlignment = value;
                    TaskbarFlyoutAlignmentChanged?.Invoke(value);
                    SaveDebounced();
                }
            }
        }

        // "RightToLeft", "TopToBottom" or "BottomToTop"
        public string TaskbarSideGraphDirection
        {
            get => ActiveEdge.SideGraphDirection;
            set
            {
                if (ActiveEdge.SideGraphDirection != value)
                {
                    ActiveEdge.SideGraphDirection = value;
                    TaskbarSideGraphDirectionChanged?.Invoke(value);
                    SaveDebounced();
                }
            }
        }

        // lines for the sensor name in a side slot, 1 or 2
        public int TaskbarSideTitleLines
        {
            get => ActiveEdge.SideTitleLines;
            set
            {
                if (ActiveEdge.SideTitleLines != value)
                {
                    ActiveEdge.SideTitleLines = value;
                    TaskbarSideTitleLinesChanged?.Invoke(value);
                    SaveDebounced();
                }
            }
        }

        // every edge from the saved dictionary, or from the legacy single values in an older file
        private static Dictionary<string, TaskbarEdgeSettings> SeedTaskbarEdges(
            Dictionary<string, TaskbarEdgeSettings>? saved, AppSettingsData data)
        {
            var edges = new Dictionary<string, TaskbarEdgeSettings>();

            foreach (var name in TaskbarEdgeNames)
            {
                if (saved != null && saved.TryGetValue(name, out var savedEdge) && savedEdge != null)
                {
                    edges[name] = savedEdge.Clone();
                    continue;
                }

                var seeded = new TaskbarEdgeSettings();
                seeded.GraphTimeSpanSeconds = data.TaskbarGraphTimeSpanSeconds ?? seeded.GraphTimeSpanSeconds;
                seeded.GraphWidthDip = data.TaskbarGraphWidthDip ?? seeded.GraphWidthDip;
                seeded.FlyoutAlignment = data.TaskbarFlyoutAlignment ?? seeded.FlyoutAlignment;
                edges[name] = seeded;
            }

            return edges;
        }

        // a copy for the debounced writer, which serializes on another thread
        private Dictionary<string, TaskbarEdgeSettings> CloneTaskbarEdges()
        {
            var copy = new Dictionary<string, TaskbarEdgeSettings>();
            foreach (var pair in _taskbarEdges)
            {
                copy[pair.Key] = pair.Value.Clone();
            }
            return copy;
        }


        // --- app behavior ---

        private bool _minimizeToTray = Defaults.MinimizeToTray;
        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set
            {
                if (_minimizeToTray != value)
                {
                    _minimizeToTray = value;
                    MinimizeToTrayChanged?.Invoke(_minimizeToTray);
                    SaveDebounced();
                }
            }
        }

        // --- startup settings ---

        // none of the four raises a change event: they are read at startup, and the autostart pair goes
        // straight into the task scheduler

        // mirrors the scheduled task, which is the authority (see WinAutostartService)
        private bool _runOnStartup = Defaults.RunOnStartup;
        public bool RunOnStartup
        {
            get => _runOnStartup;
            set
            {
                if (_runOnStartup != value)
                {
                    _runOnStartup = value;
                    SaveDebounced();
                }
            }
        }

        private bool _delayStartup = Defaults.DelayStartup;
        public bool DelayStartup
        {
            get => _delayStartup;
            set
            {
                if (_delayStartup != value)
                {
                    _delayStartup = value;
                    SaveDebounced();
                }
            }
        }

        private bool _startMinimizedToTray = Defaults.StartMinimizedToTray;
        public bool StartMinimizedToTray
        {
            get => _startMinimizedToTray;
            set
            {
                if (_startMinimizedToTray != value)
                {
                    _startMinimizedToTray = value;
                    SaveDebounced();
                }
            }
        }

        private bool _checkUpdatesOnStartup = Defaults.CheckUpdatesOnStartup;
        public bool CheckUpdatesOnStartup
        {
            get => _checkUpdatesOnStartup;
            set
            {
                if (_checkUpdatesOnStartup != value)
                {
                    _checkUpdatesOnStartup = value;
                    SaveDebounced();
                }
            }
        }

        // no change event; UpdateService asks when it needs it
        private string _skippedUpdateVersion = Defaults.SkippedUpdateVersion;
        public string SkippedUpdateVersion
        {
            get => _skippedUpdateVersion;
            set
            {
                if (_skippedUpdateVersion != value)
                {
                    _skippedUpdateVersion = value;
                    SaveDebounced();
                }
            }
        }

        // no change event; MainWindow reads it once at the splash reveal
        private StartupPage _startupPage = Defaults.StartupPage;
        public StartupPage StartupPage
        {
            get => _startupPage;
            set
            {
                if (_startupPage != value)
                {
                    _startupPage = value;
                    SaveDebounced();
                }
            }
        }

        private bool _hideSensorsCompletely = Defaults.HideSensorsCompletely;
        public bool HideSensorsCompletely
        {
            get => _hideSensorsCompletely;
            set
            {
                if (_hideSensorsCompletely != value)
                {
                    _hideSensorsCompletely = value;
                    HideSensorsCompletelyChanged?.Invoke(_hideSensorsCompletely);
                    SaveDebounced();
                }
            }
        }

        // master switch of the title bar status readout
        private bool _statusReadoutEnabled = Defaults.StatusReadoutEnabled;
        public bool StatusReadoutEnabled
        {
            get => _statusReadoutEnabled;
            set
            {
                if (_statusReadoutEnabled != value)
                {
                    _statusReadoutEnabled = value;
                    StatusReadoutChanged?.Invoke();
                    SaveDebounced();
                }
            }
        }

        // collapsed by the title bar toggle, which only hides what the master switch allows
        private bool _statusReadoutCollapsed = Defaults.StatusReadoutCollapsed;
        public bool StatusReadoutCollapsed
        {
            get => _statusReadoutCollapsed;
            set
            {
                if (_statusReadoutCollapsed != value)
                {
                    _statusReadoutCollapsed = value;
                    StatusReadoutChanged?.Invoke();
                    SaveDebounced();
                }
            }
        }

        // the two status groups and their order
        private bool _statusLhmGroupEnabled = Defaults.StatusLhmGroupEnabled;
        public bool StatusLhmGroupEnabled
        {
            get => _statusLhmGroupEnabled;
            set
            {
                if (_statusLhmGroupEnabled != value)
                {
                    _statusLhmGroupEnabled = value;
                    StatusReadoutChanged?.Invoke();
                    SaveDebounced();
                }
            }
        }

        private bool _statusWindowsGroupEnabled = Defaults.StatusWindowsGroupEnabled;
        public bool StatusWindowsGroupEnabled
        {
            get => _statusWindowsGroupEnabled;
            set
            {
                if (_statusWindowsGroupEnabled != value)
                {
                    _statusWindowsGroupEnabled = value;
                    StatusReadoutChanged?.Invoke();
                    SaveDebounced();
                }
            }
        }

        private StatusGroupOrder _statusGroupOrder = Defaults.StatusGroupOrder;
        public StatusGroupOrder StatusGroupOrder
        {
            get => _statusGroupOrder;
            set
            {
                if (_statusGroupOrder != value)
                {
                    _statusGroupOrder = value;
                    StatusReadoutChanged?.Invoke();
                    SaveDebounced();
                }
            }
        }

        // the recording folder, picked in the logger window; no change event, that window is the only writer and reader
        private string _csvLogFolder = Defaults.CsvLogFolder;
        public string CsvLogFolder
        {
            get => _csvLogFolder;
            set
            {
                if (_csvLogFolder != value)
                {
                    _csvLogFolder = value;
                    SaveDebounced();
                }
            }
        }

        // the separators; no change event, CsvLoggingService snapshots it at the start of a recording
        private CsvNumberFormat _csvNumberFormat = Defaults.CsvNumberFormat;
        public CsvNumberFormat CsvNumberFormat
        {
            get => _csvNumberFormat;
            set
            {
                if (_csvNumberFormat != value)
                {
                    _csvNumberFormat = value;
                    SaveDebounced();
                }
            }
        }

        // decimals per value, a fixed count; snapshotted like the separators
        private int _csvDecimalPlaces = Defaults.CsvDecimalPlaces;
        public int CsvDecimalPlaces
        {
            get => _csvDecimalPlaces;
            set
            {
                if (_csvDecimalPlaces != value)
                {
                    _csvDecimalPlaces = value;
                    SaveDebounced();
                }
            }
        }

        // the unit next to every value; (the header has it anyway)
        private bool _csvIncludeUnits = Defaults.CsvIncludeUnits;
        public bool CsvIncludeUnits
        {
            get => _csvIncludeUnits;
            set
            {
                if (_csvIncludeUnits != value)
                {
                    _csvIncludeUnits = value;
                    SaveDebounced();
                }
            }
        }

        // what a resume writes at the seam; read live, it cannot invalidate a written row
        private CsvPauseSeam _csvPauseSeam = Defaults.CsvPauseSeam;
        public CsvPauseSeam CsvPauseSeam
        {
            get => _csvPauseSeam;
            set
            {
                if (_csvPauseSeam != value)
                {
                    _csvPauseSeam = value;
                    SaveDebounced();
                }
            }
        }

        // the last sensors page profile; read while the page builds, no change event
        private SensorSelectionProfile _lastSensorProfile = Defaults.LastSensorProfile;
        public SensorSelectionProfile LastSensorProfile
        {
            get => _lastSensorProfile;
            set
            {
                if (_lastSensorProfile != value)
                {
                    _lastSensorProfile = value;
                    SaveDebounced();
                }
            }
        }

        // the saved selector, else the legacy accent/custom bool, else (a fresh install) the default
        private static GraphColorSource ResolveGraphColorSource(
            GraphColorSource? saved, bool? legacyUseAccent, GraphColorSource fallback) =>
            saved ?? legacyUseAccent switch
            {
                true => GraphColorSource.Accent,
                false => GraphColorSource.Custom,
                null => fallback
            };

        // persistence
        // straight into the backing fields, no change events and no save; at startup before any listener exists, and
        // after an import right before the restart
        public void LoadFromData(AppSettingsData data)
        {
            _appTheme = data.AppTheme;
            _backdropType = data.BackdropType;
            _tintOpacity = data.TintOpacity;
            _luminosityOpacity = data.LuminosityOpacity;
            _useAccentColor = data.UseAccentColor;
            _customTintColor = data.CustomTintColor;
            _graphColorSource = ResolveGraphColorSource(
                data.GraphColorSource, data.UseGraphAccentColor, AppSettingsData.DefaultGraphColorSource);
            _graphCustomColor = data.GraphCustomColor;
            _graphTimeSpanSeconds = data.GraphTimeSpanSeconds;
            _graphLineStyle = data.GraphLineStyle;
            _graphFillFade = data.GraphFillFade;
            _useHardwareIconColors = data.UseHardwareIconColors;
            HardwareColorMode.UseIconColors = _useHardwareIconColors;
            _dataSizeUnitBasis = data.DataSizeUnitBasis;
            _dataSpeedUnitBasis = data.DataSpeedUnitBasis;
            SensorUnitFormatter.DataSizeBasis = _dataSizeUnitBasis;
            SensorUnitFormatter.DataSpeedBasis = _dataSpeedUnitBasis;
            _performanceGraphTimeSpanSeconds = data.PerformanceGraphTimeSpanSeconds;
            _performanceExtendedGraphTimeSpanSeconds = data.PerformanceExtendedGraphTimeSpanSeconds;

            _taskbarBackdropType = data.TaskbarBackdropType;
            _taskbarTintOpacity = data.TaskbarTintOpacity;
            _taskbarLuminosityOpacity = data.TaskbarLuminosityOpacity;
            _taskbarUseAccentColor = data.TaskbarUseAccentColor;
            _taskbarCustomTintColor = data.TaskbarCustomTintColor;
            _taskbarGraphColorSource = ResolveGraphColorSource(
                data.TaskbarGraphColorSource, data.TaskbarUseGraphAccentColor, AppSettingsData.DefaultTaskbarGraphColorSource);
            _taskbarGraphCustomColor = data.TaskbarGraphCustomColor;
            _taskbarUseTransparentGraphBackground = data.TaskbarUseTransparentGraphBackground;
            _taskbarEdges = SeedTaskbarEdges(data.TaskbarEdges, data);
            _taskbarWidgetPositionLocked = data.TaskbarWidgetPositionLocked;
            _taskbarFlyoutGraphTimeSpanSeconds = data.TaskbarFlyoutGraphTimeSpanSeconds;
            _taskbarFlyoutGraphHeightDip = data.TaskbarFlyoutGraphHeightDip;

            _minimizeToTray = data.MinimizeToTray;
            _runOnStartup = data.RunOnStartup;
            _delayStartup = data.DelayStartup;
            _startMinimizedToTray = data.StartMinimizedToTray;
            _checkUpdatesOnStartup = data.CheckUpdatesOnStartup;
            _startupPage = data.StartupPage;
            _skippedUpdateVersion = data.SkippedUpdateVersion ?? "";
            _hideSensorsCompletely = data.HideSensorsCompletely;
            _statusReadoutEnabled = data.StatusReadoutEnabled;
            _statusReadoutCollapsed = data.StatusReadoutCollapsed;
            _statusLhmGroupEnabled = data.StatusLhmGroupEnabled;
            _statusWindowsGroupEnabled = data.StatusWindowsGroupEnabled;
            _statusGroupOrder = data.StatusGroupOrder;
            _csvLogFolder = data.CsvLogFolder ?? "";
            _csvNumberFormat = data.CsvNumberFormat;
            _csvDecimalPlaces = data.CsvDecimalPlaces;
            _csvIncludeUnits = data.CsvIncludeUnits;
            _csvPauseSeam = data.CsvPauseSeam;
            _lastSensorProfile = data.LastSensorProfile;

            // lives on HardwareMonitorService at runtime, shares this file
            HardwareMonitorService.Instance.UpdateIntervalMs = data.UpdateIntervalMs;
        }

        // the live values as a serializable snapshot
        private AppSettingsData ToData()
        {
            return new AppSettingsData
            {
                AppTheme = _appTheme,
                BackdropType = _backdropType,
                TintOpacity = _tintOpacity,
                LuminosityOpacity = _luminosityOpacity,
                UseAccentColor = _useAccentColor,
                CustomTintColor = _customTintColor,
                GraphColorSource = _graphColorSource,
                GraphCustomColor = _graphCustomColor,
                GraphTimeSpanSeconds = _graphTimeSpanSeconds,
                GraphLineStyle = _graphLineStyle,
                GraphFillFade = _graphFillFade,
                UseHardwareIconColors = _useHardwareIconColors,
                DataSizeUnitBasis = _dataSizeUnitBasis,
                DataSpeedUnitBasis = _dataSpeedUnitBasis,
                PerformanceGraphTimeSpanSeconds = _performanceGraphTimeSpanSeconds,
                PerformanceExtendedGraphTimeSpanSeconds = _performanceExtendedGraphTimeSpanSeconds,

                TaskbarBackdropType = _taskbarBackdropType,
                TaskbarTintOpacity = _taskbarTintOpacity,
                TaskbarLuminosityOpacity = _taskbarLuminosityOpacity,
                TaskbarUseAccentColor = _taskbarUseAccentColor,
                TaskbarCustomTintColor = _taskbarCustomTintColor,
                TaskbarGraphColorSource = _taskbarGraphColorSource,
                TaskbarGraphCustomColor = _taskbarGraphCustomColor,
                TaskbarUseTransparentGraphBackground = _taskbarUseTransparentGraphBackground,
                TaskbarEdges = CloneTaskbarEdges(),
                TaskbarWidgetPositionLocked = _taskbarWidgetPositionLocked,
                TaskbarFlyoutGraphTimeSpanSeconds = _taskbarFlyoutGraphTimeSpanSeconds,
                TaskbarFlyoutGraphHeightDip = _taskbarFlyoutGraphHeightDip,

                MinimizeToTray = _minimizeToTray,
                RunOnStartup = _runOnStartup,
                DelayStartup = _delayStartup,
                StartMinimizedToTray = _startMinimizedToTray,
                CheckUpdatesOnStartup = _checkUpdatesOnStartup,
                StartupPage = _startupPage,
                SkippedUpdateVersion = _skippedUpdateVersion,
                HideSensorsCompletely = _hideSensorsCompletely,
                StatusReadoutEnabled = _statusReadoutEnabled,
                StatusReadoutCollapsed = _statusReadoutCollapsed,
                StatusLhmGroupEnabled = _statusLhmGroupEnabled,
                StatusWindowsGroupEnabled = _statusWindowsGroupEnabled,
                StatusGroupOrder = _statusGroupOrder,
                CsvLogFolder = _csvLogFolder,
                CsvNumberFormat = _csvNumberFormat,
                CsvDecimalPlaces = _csvDecimalPlaces,
                CsvIncludeUnits = _csvIncludeUnits,
                CsvPauseSeam = _csvPauseSeam,
                LastSensorProfile = _lastSensorProfile,
                UpdateIntervalMs = HardwareMonitorService.Instance.UpdateIntervalMs
            };
        }

        // every setter calls this; public for UpdateIntervalMs, which changes on HardwareMonitorService
        public void SaveDebounced()
        {
            PersistenceService.Instance.SaveSettingsDebounced(ToData());
        }

        // an immediate write past the save-on-change guard; Export uses it, so a backup is live even if
        // settings.json was deleted by a reset
        public void SaveImmediate()
        {
            PersistenceService.Instance.SaveSettingsDebounced(ToData());
        }


        // === events ===

        public event Action<string> ThemeChanged;
        public event Action<string> BackdropTypeChanged;
        public event Action<float, float> OpacityChanged;
        public event Action<bool, Color> TintColorChanged;
        public event Action<GraphColorSource, Windows.UI.Color> GraphColorChanged;
        public event Action<double> GraphTimeSpanChanged;
        public event Action<GraphLineStyle> GraphLineStyleChanged;
        public event Action<bool> GraphFillFadeChanged;

        // no value; consumers re-read the switch
        public event Action HardwareIconColorsChanged;

        // no value; both unit settings raise it, consumers re-read from SensorUnitFormatter
        public event Action DataUnitBasisChanged;

        // no value; both performance time spans raise it
        public event Action PerformanceGraphTimeSpanChanged;

        public event Action<string> TaskbarBackdropTypeChanged;
        public event Action<float, float> TaskbarOpacityChanged;
        public event Action<bool, Color> TaskbarTintColorChanged;
        public event Action<GraphColorSource, Windows.UI.Color> TaskbarGraphColorChanged;
        public event Action<double> TaskbarGraphTimeSpanChanged;
        public event Action<int> TaskbarGraphWidthChanged;
        public event Action<bool> TaskbarGraphBackgroundChanged;
        public event Action<string> ActiveTaskbarEdgeChanged;
        public event Action<string> TaskbarSideGraphDirectionChanged;
        public event Action<int> TaskbarSideTitleLinesChanged;
        public event Action<string> TaskbarFlyoutAlignmentChanged;
        public event Action<bool> TaskbarWidgetPositionLockedChanged;
        public event Action<double> TaskbarFlyoutGraphTimeSpanChanged;
        public event Action<double> TaskbarFlyoutGraphHeightChanged;

        public event Action<bool> MinimizeToTrayChanged;
        public event Action<bool> HideSensorsCompletelyChanged;
        // one event for all five status readout settings, read back as a set
        public event Action StatusReadoutChanged;
    }
}
