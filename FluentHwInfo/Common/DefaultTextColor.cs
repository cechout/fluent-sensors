using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using FluentHwInfo.Persistence.Services;


namespace FluentHwInfo.Common
{
    // resolves the app's default text color for the currently selected app theme.
    //
    // Application.Current.Resources["TextFillColorPrimaryBrush"] ignores a window's RequestedTheme
    // override and resolves against the OS theme instead. Application.Current.Resources.ThemeDictionaries
    // also does not reliably expose WinUI's built-in Fluent brushes from C# (confirmed: throws
    // KeyNotFoundException even after walking every MergedDictionary). And even routing through XAML's
    // own Style-Setter ThemeResource fallback (via DependencyProperty.UnsetValue) still didn't react to
    // theme switches correctly for text inside this DataTemplate.
    //
    // instead of depending on any resource dictionary lookup, this hardcodes the two literal Fluent 2
    // design token values for TextFillColorPrimary and picks between them based on the app's own theme
    // setting - nothing here can be affected by OS theme, RequestedTheme propagation timing, or how deep
    // an element sits inside a DataTemplate
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