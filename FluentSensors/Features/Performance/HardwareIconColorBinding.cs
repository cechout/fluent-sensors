using Microsoft.UI.Xaml;
using System;

using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance
{
    // keeps a hardware detail views header icon on the current hardware icon color setting and the current theme
    // for as long as the view is on screen
    //
    // GroupIconBrush resolves against the setting on every read and has nothing of its own to push when it flips;
    // the refresh is therefore the views generated Bindings.Update, handed in as a callback because that member
    // exists per view type and cannot be reached from here
    //
    // the subscription shape mirrors PerformanceGraphDefaults.BindTimeSpan, guard against a stacked handler
    // included; refreshing on Loaded as well is what catches a flip that happened while the view was off screen
    public static class HardwareIconColorBinding
    {
        public static void Bind(FrameworkElement root, Action refresh)
        {
            bool isSubscribed = false;

            // the untinted brush is a plain brush rather than a theme resource, so a theme switch needs the same
            // refresh the colour setting gets
            Action<string> onThemeChanged = _ => refresh();

            root.Loaded += (s, e) =>
            {
                refresh();

                if (isSubscribed) return;
                isSubscribed = true;
                SettingsService.Instance.HardwareIconColorsChanged += refresh;
                SettingsService.Instance.ThemeChanged += onThemeChanged;
            };

            root.Unloaded += (s, e) =>
            {
                if (!isSubscribed) return;
                isSubscribed = false;
                SettingsService.Instance.HardwareIconColorsChanged -= refresh;
                SettingsService.Instance.ThemeChanged -= onThemeChanged;
            };
        }
    }
}
