using System;
using Windows.UI;

using FluentHwInfo.Persistence.Models;
using FluentHwInfo.Core;


namespace FluentHwInfo.Persistence.Services
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

        private int _graphDataPoints = 110;
        public int GraphDataPoints
        {
            get => _graphDataPoints;
            set
            {
                if (_graphDataPoints != value)
                {
                    _graphDataPoints = value;
                    GraphDataPointsChanged?.Invoke(_graphDataPoints);
                    SaveDebounced();
                }
            }
        }

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
            _graphDataPoints = data.GraphDataPoints;
            _minimizeToTray = data.MinimizeToTray;
            _hideSensorsCompletely = data.HideSensorsCompletely;

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
                GraphDataPoints = _graphDataPoints,
                MinimizeToTray = _minimizeToTray,
                HideSensorsCompletely = _hideSensorsCompletely,
                UpdateIntervalMs = HardwareMonitorService.Instance.UpdateIntervalMs
            };
        }

        // called by every setter above; public so code that changes UpdateIntervalMs directly on HardwareMonitorService
        // (which has no change event of its own) can trigger a save too
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
        public event Action<int> GraphDataPointsChanged;
        public event Action<bool> MinimizeToTrayChanged;
        public event Action<bool> HideSensorsCompletelyChanged;
    }
}