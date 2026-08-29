using System;
using Windows.UI;

using FluentSensors.Persistence.Models;
using FluentSensors.Core;


namespace FluentSensors.Persistence.Services
{
    public class SettingsService
    {
        // === singleton instance ===

        private static readonly SettingsService _instance = new SettingsService();
        public static SettingsService Instance => _instance;


        // === constructor ===

        private SettingsService() { }


        // === public api ===

        // properties
        private string _appTheme = "Default";
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

        // --- Widget Window Appearance Settings ---

        private string _backdropType = "Mica";
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

        private float _tintOpacity = 0.4f;
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

        private float _luminosityOpacity = 0.2f;
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

        private bool _useAccentColor = true;
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

        private Color _customTintColor = Color.FromArgb(255, 25, 25, 25);
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

        private bool _useGraphAccentColor = true;
        public bool UseGraphAccentColor
        {
            get => _useGraphAccentColor;
            set
            {
                if (_useGraphAccentColor != value)
                {
                    _useGraphAccentColor = value;
                    GraphColorChanged?.Invoke(_useGraphAccentColor, _graphCustomColor);
                    SaveDebounced();
                }
            }
        }

        private Windows.UI.Color _graphCustomColor = Microsoft.UI.Colors.LightBlue;
        public Windows.UI.Color GraphCustomColor
        {
            get => _graphCustomColor;
            set
            {
                if (_graphCustomColor != value)
                {
                    _graphCustomColor = value;
                    GraphColorChanged?.Invoke(_useGraphAccentColor, _graphCustomColor);
                    SaveDebounced();
                }
            }
        }

        // seconds of history shown on a graph that does not have its own fixed override (e.g. the Widget)
        private double _graphTimeSpanSeconds = 45;
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


        // --- Taskbar Ecosystem (Widget + Flyout) Appearance Settings ---

        private string _taskbarBackdropType = "Acrylic";
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

        private float _taskbarTintOpacity = 0.4f;
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

        private float _taskbarLuminosityOpacity = 0.2f;
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

        private bool _taskbarUseAccentColor = true;
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

        private Color _taskbarCustomTintColor = Color.FromArgb(255, 25, 25, 25);
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

        private bool _taskbarUseGraphAccentColor = true;
        public bool TaskbarUseGraphAccentColor
        {
            get => _taskbarUseGraphAccentColor;
            set
            {
                if (_taskbarUseGraphAccentColor != value)
                {
                    _taskbarUseGraphAccentColor = value;
                    TaskbarGraphColorChanged?.Invoke(_taskbarUseGraphAccentColor, _taskbarGraphCustomColor);
                    SaveDebounced();
                }
            }
        }

        private Windows.UI.Color _taskbarGraphCustomColor = Microsoft.UI.Colors.LightBlue;
        public Windows.UI.Color TaskbarGraphCustomColor
        {
            get => _taskbarGraphCustomColor;
            set
            {
                if (_taskbarGraphCustomColor != value)
                {
                    _taskbarGraphCustomColor = value;
                    TaskbarGraphColorChanged?.Invoke(_taskbarUseGraphAccentColor, _taskbarGraphCustomColor);
                    SaveDebounced();
                }
            }
        }

        private double _taskbarGraphTimeSpanSeconds = 45;
        public double TaskbarGraphTimeSpanSeconds
        {
            get => _taskbarGraphTimeSpanSeconds;
            set
            {
                if (_taskbarGraphTimeSpanSeconds != value)
                {
                    _taskbarGraphTimeSpanSeconds = value;
                    TaskbarGraphTimeSpanChanged?.Invoke(_taskbarGraphTimeSpanSeconds);
                    SaveDebounced();
                }
            }
        }

        private int _taskbarGraphWidthDip = 120;
        public int TaskbarGraphWidthDip
        {
            get => _taskbarGraphWidthDip;
            set
            {
                if (_taskbarGraphWidthDip != value)
                {
                    _taskbarGraphWidthDip = value;
                    TaskbarGraphWidthChanged?.Invoke(_taskbarGraphWidthDip);
                    SaveDebounced();
                }
            }
        }


        // --- App Behavior Settings ---

        private bool _minimizeToTray = true;
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

        private bool _hideSensorsCompletely = true;
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

        // whether the title bar status readout (lhm/windows groups) is toggled on, set from MainWindows toggle button
        private bool _statusReadoutEnabled = true;
        public bool StatusReadoutEnabled
        {
            get => _statusReadoutEnabled;
            set
            {
                if (_statusReadoutEnabled != value)
                {
                    _statusReadoutEnabled = value;
                    StatusReadoutEnabledChanged?.Invoke(_statusReadoutEnabled);
                    SaveDebounced();
                }
            }
        }

        // persistence
        // writes every property straight to its backing field, skipping change events and the save trigger; used only
        // once at startup, before any window or listener exists yet
        public void LoadFromData(AppSettingsData data)
        {
            _appTheme = data.AppTheme;
            _backdropType = data.BackdropType;
            _tintOpacity = data.TintOpacity;
            _luminosityOpacity = data.LuminosityOpacity;
            _useAccentColor = data.UseAccentColor;
            _customTintColor = data.CustomTintColor;
            _useGraphAccentColor = data.UseGraphAccentColor;
            _graphCustomColor = data.GraphCustomColor;
            _graphTimeSpanSeconds = data.GraphTimeSpanSeconds;

            _taskbarBackdropType = data.TaskbarBackdropType;
            _taskbarTintOpacity = data.TaskbarTintOpacity;
            _taskbarLuminosityOpacity = data.TaskbarLuminosityOpacity;
            _taskbarUseAccentColor = data.TaskbarUseAccentColor;
            _taskbarCustomTintColor = data.TaskbarCustomTintColor;
            _taskbarUseGraphAccentColor = data.TaskbarUseGraphAccentColor;
            _taskbarGraphCustomColor = data.TaskbarGraphCustomColor;
            _taskbarGraphTimeSpanSeconds = data.TaskbarGraphTimeSpanSeconds;
            _taskbarGraphWidthDip = data.TaskbarGraphWidthDip;

            _minimizeToTray = data.MinimizeToTray;
            _hideSensorsCompletely = data.HideSensorsCompletely;
            _statusReadoutEnabled = data.StatusReadoutEnabled;

            // lives on HardwareMonitorService at runtime, not here, but shares this settings file
            HardwareMonitorService.Instance.UpdateIntervalMs = data.UpdateIntervalMs;
        }

        // snapshots the current live values into a plain serializable object for disk saving
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
                UseGraphAccentColor = _useGraphAccentColor,
                GraphCustomColor = _graphCustomColor,
                GraphTimeSpanSeconds = _graphTimeSpanSeconds,

                TaskbarBackdropType = _taskbarBackdropType,
                TaskbarTintOpacity = _taskbarTintOpacity,
                TaskbarLuminosityOpacity = _taskbarLuminosityOpacity,
                TaskbarUseAccentColor = _taskbarUseAccentColor,
                TaskbarCustomTintColor = _taskbarCustomTintColor,
                TaskbarUseGraphAccentColor = _taskbarUseGraphAccentColor,
                TaskbarGraphCustomColor = _taskbarGraphCustomColor,
                TaskbarGraphTimeSpanSeconds = _taskbarGraphTimeSpanSeconds,
                TaskbarGraphWidthDip = _taskbarGraphWidthDip,

                MinimizeToTray = _minimizeToTray,
                HideSensorsCompletely = _hideSensorsCompletely,
                StatusReadoutEnabled = _statusReadoutEnabled,
                UpdateIntervalMs = HardwareMonitorService.Instance.UpdateIntervalMs
            };
        }

        // called by every setter above; public so code that changes UpdateIntervalMs directly on HardwareMonitorService
        // (its own change event does not trigger a save) can trigger a save too
        public void SaveDebounced()
        {
            PersistenceService.Instance.SaveSettingsDebounced(ToData());
        }

        // forces the current in-memory values to be queued for an immediate write, bypassing the "only save on change" guard
        // in every property setter above
        // used by Export so a backup always reflects the live session state, even if settings.json was deleted (e.g. by a
        // previous reset) and nothing has changed since
        public void SaveImmediate()
        {
            PersistenceService.Instance.SaveSettingsDebounced(ToData());
        }


        // === events ===

        public event Action<string> ThemeChanged;
        public event Action<string> BackdropTypeChanged;
        public event Action<float, float> OpacityChanged;
        public event Action<bool, Color> TintColorChanged;
        public event Action<bool, Windows.UI.Color> GraphColorChanged;
        public event Action<double> GraphTimeSpanChanged;

        public event Action<string> TaskbarBackdropTypeChanged;
        public event Action<float, float> TaskbarOpacityChanged;
        public event Action<bool, Color> TaskbarTintColorChanged;
        public event Action<bool, Windows.UI.Color> TaskbarGraphColorChanged;
        public event Action<double> TaskbarGraphTimeSpanChanged;
        public event Action<int> TaskbarGraphWidthChanged;

        public event Action<bool> MinimizeToTrayChanged;
        public event Action<bool> HideSensorsCompletelyChanged;
        public event Action<bool> StatusReadoutEnabledChanged;
    }
}