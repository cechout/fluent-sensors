using System;
using Windows.UI;

namespace FluentHwInfo.Services
{
    public class SettingsService
    {
        // SettingsService as a singleton
        private static readonly SettingsService _instance = new SettingsService();
        public static SettingsService Instance => _instance;
        private SettingsService() { }

        // events
        public event Action<string> ThemeChanged;
        public event Action<string> BackdropTypeChanged;
        public event Action<float, float> OpacityChanged;
        public event Action<bool, Color> TintColorChanged;
        public event Action<bool, Windows.UI.Color> GraphColorChanged;

        // fields
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
                }
            }
        }

        private Color _customTintColor = Color.FromArgb(255, 128, 128, 128);
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
                }
            }
        }
    }
}