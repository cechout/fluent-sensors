using System;
using Windows.UI;

namespace FluentHwInfo.Services
{
    public class SettingsService
    {
        // the Singleton pattern (exactly as in HardwareMonitorService)
        private static readonly SettingsService _instance = new SettingsService();
        public static SettingsService Instance => _instance;

        // events
        public event Action<string> ThemeChanged;
        public event Action<string> BackdropTypeChanged;
        public event Action<float, float> OpacityChanged;
        public event Action<bool, Color> TintColorChanged;

        // fields
        private string _appTheme = "Default";
        private string _backdropType = "Mica";
        private float _tintOpacity = 0.4f;
        private float _luminosityOpacity = 0.2f;
        private bool _useAccentColor = true;
        private Color _customTintColor = Color.FromArgb(255, 128, 128, 128);

        private SettingsService() { }

        // properties
        public string AppTheme
        {
            get => _appTheme;
            set
            {
                if (_appTheme != value)
                {
                    _appTheme = value;
                    ThemeChanged?.Invoke(_appTheme);
                }
            }
        }

        public string BackdropType
        {
            get => _backdropType;
            set
            {
                if (_backdropType != value)
                {
                    _backdropType = value;
                    BackdropTypeChanged?.Invoke(_backdropType);
                }
            }
        }

        public float TintOpacity
        {
            get => _tintOpacity;
            set
            {
                if (_tintOpacity != value)
                {
                    _tintOpacity = value;
                    OpacityChanged?.Invoke(_tintOpacity, _luminosityOpacity);
                }
            }
        }

        public float LuminosityOpacity
        {
            get => _luminosityOpacity;
            set
            {
                if (_luminosityOpacity != value)
                {
                    _luminosityOpacity = value;
                    OpacityChanged?.Invoke(_tintOpacity, _luminosityOpacity);
                }
            }
        }

        public bool UseAccentColor
        {
            get => _useAccentColor;
            set
            {
                if (_useAccentColor != value)
                {
                    _useAccentColor = value;
                    TintColorChanged?.Invoke(_useAccentColor, _customTintColor);
                }
            }
        }

        public Color CustomTintColor
        {
            get => _customTintColor;
            set
            {
                if (_customTintColor != value)
                {
                    _customTintColor = value;
                    TintColorChanged?.Invoke(_useAccentColor, _customTintColor);
                }
            }
        }
    }
}