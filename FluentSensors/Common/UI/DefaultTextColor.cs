using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using FluentSensors.Persistence.Services;


namespace FluentSensors.Common.UI
{
    // the default text color of the app theme
    //
    // --- workaround: theme brush lookup from code-behind ---
    // problem: Application.Current.Resources[...] from C# returns the light theme value, ignores a window
    // RequestedTheme (it resolves against the OS theme) and never follows a runtime switch:
    // https://github.com/microsoft/microsoft-ui-xaml/issues/7663
    // the same cause broke walking ThemeDictionaries (KeyNotFoundException through every MergedDictionary) and the
    // Style Setter fallback via DependencyProperty.UnsetValue (no switch inside a DataTemplate)
    // fix: no lookup; the two Fluent 2 token values of TextFillColorPrimary, picked by the theme, untouched by OS
    // theme, propagation timing or template depth
    public static class DefaultTextColor
    {
        private static readonly Windows.UI.Color LightColor = Windows.UI.Color.FromArgb(0xE4, 0x00, 0x00, 0x00);
        private static readonly Windows.UI.Color DarkColor = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

        // for callers without an element, by the app theme setting
        public static Brush Resolve()
        {
            bool isDark = SettingsService.Instance.AppTheme switch
            {
                "Light" => false,
                "Dark" => true,
                // "Default" follows the OS theme, like ElementTheme.Default in ApplyTheme
                _ => Application.Current.RequestedTheme == ApplicationTheme.Dark
            };

            return ForTheme(isDark);
        }

        // for callers with an ActualTheme; safer than Resolve in a window that does not follow the
        // setting (see SensorPanelControl)
        public static Brush ForTheme(bool isDark)
        {
            return new SolidColorBrush(isDark ? DarkColor : LightColor);
        }
    }
}
