using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using FluentSensors.Persistence.Services;


namespace FluentSensors.Common
{
    // resolves the apps default text color for the currently selected app theme
    //
    // --- workaround: theme brush lookup from code-behind ---
    // problem: resolving theme resources via Application.Current.Resources[...] from C# always returns the light-theme
    // value and ignores a windows RequestedTheme override entirely; it resolves against the
    // OS theme instead, and does not react to theme changes at runtime
    // confirmed upstream: https://github.com/microsoft/microsoft-ui-xaml/issues/7663
    // same root cause also broke two other attempts:
    // walking Application.Current.Resources.ThemeDictionaries directly (throws KeyNotFoundException even after checking
    // every MergedDictionary), and routing through XAMLs own Style-Setter ThemeResource fallback via
    // DependencyProperty.UnsetValue (still didn't react to theme switches for text inside this DataTemplate)
    // fix: do not look up the resource at all; hardcode the two literal Fluent 2 design token values for TextFillColorPrimary
    // and pick between them based on the apps own theme setting; nothing here can
    // be affected by OS theme, RequestedTheme propagation timing, or how deep an element sits inside a DataTemplate
    public static class DefaultTextColor
    {
        private static readonly Windows.UI.Color LightColor = Windows.UI.Color.FromArgb(0xE4, 0x00, 0x00, 0x00);
        private static readonly Windows.UI.Color DarkColor = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

        public static Brush Resolve()
        {
            bool isDark = SettingsService.Instance.AppTheme switch
            {
                "Light" => false,
                "Dark" => true,
                // "Default" follows the OS theme, mirrors ApplyTheme()'s ElementTheme.Default behavior
                _ => Application.Current.RequestedTheme == ApplicationTheme.Dark
            };

            return new SolidColorBrush(isDark ? DarkColor : LightColor);
        }
    }
}