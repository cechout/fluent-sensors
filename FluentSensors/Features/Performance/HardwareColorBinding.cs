using Microsoft.UI.Xaml;
using System;

using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance
{
    // keeps a hardware detail view on the current hardware color setting for as long as it is on screen
    //
    // every graph in those views takes its color from the views own HardwareColor, which reads HardwareColorMode
    // once and has nothing of its own to push when the setting flips; the refresh is therefore the views generated
    // Bindings.Update, handed in as a callback because that member exists per view type and cannot be reached
    // from here
    //
    // the subscription shape mirrors PerformanceGraphDefaults.BindTimeSpan, guard against a stacked handler
    // included; refreshing on Loaded as well is what catches a flip that happened while the view was off screen
    public static class HardwareColorBinding
    {
        public static void Bind(FrameworkElement root, Action refresh)
        {
            bool isSubscribed = false;

            root.Loaded += (s, e) =>
            {
                refresh();

                if (isSubscribed) return;
                isSubscribed = true;
                SettingsService.Instance.HardwareColorsChanged += refresh;
            };

            root.Unloaded += (s, e) =>
            {
                if (!isSubscribed) return;
                isSubscribed = false;
                SettingsService.Instance.HardwareColorsChanged -= refresh;
            };
        }
    }
}
