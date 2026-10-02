using Microsoft.UI.Xaml;
using System;

using FluentSensors.Common.Sensors;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance
{
    // the detail view icon colour:
    // keeps the header icon on the icon color setting and the theme while the view is on screen
    // GroupIconBrush resolves on every read and pushes nothing, so the refresh is the generated Bindings.Update of the
    // view, passed in since it exists per view type
    // shaped like PerformanceGraphDefaults.BindTimeSpan with its stacking guard; the Loaded refresh
    // catches a flip while off screen
    public static class HardwareIconColorBinding
    {
        public static void Bind(FrameworkElement root, Action refresh)
        {
            bool isSubscribed = false;

            // the untinted brush is no theme resource, so a theme switch refreshes too; by ActualThemeChanged, since
            // SettingsService.ThemeChanged fires before the theme applies
            void onThemeChanged(FrameworkElement sender, object args)
            {
                HardwareColorMode.IsDarkTheme = root.ActualTheme == ElementTheme.Dark;
                refresh();
            }

            root.Loaded += (s, e) =>
            {
                HardwareColorMode.IsDarkTheme = root.ActualTheme == ElementTheme.Dark;
                refresh();

                if (isSubscribed) return;
                isSubscribed = true;
                SettingsService.Instance.HardwareIconColorsChanged += refresh;
                root.ActualThemeChanged += onThemeChanged;
            };

            root.Unloaded += (s, e) =>
            {
                if (!isSubscribed) return;
                isSubscribed = false;
                SettingsService.Instance.HardwareIconColorsChanged -= refresh;
                root.ActualThemeChanged -= onThemeChanged;
            };
        }
    }
}
