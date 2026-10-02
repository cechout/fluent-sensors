using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;


namespace FluentSensors.Common.UI
{
    // --- workaround: SettingsExpander content goes blank after collapse/expand ---
    // problem: the inner ItemsRepeater stops rendering after repeated collapse and expand (or hide
    // and show) until a layout pass:
    // https://github.com/microsoft/microsoft-ui-xaml/issues/9337
    // fix: force that pass right away instead of waiting for a scroll or resize
    public static class SettingsExpanderRepaintFix
    {
        // once, from the Loaded of the expander
        public static void Attach(SettingsExpander expander)
        {
            // named, so Unloaded can remove it
            EventHandler expandedHandler = (s, e) => Refresh(expander);
            expander.Expanded += expandedHandler;

            // Visibility has no changed event; the token unregisters it
            long visibilityToken = expander.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, dp) =>
            {
                if (expander.Visibility == Visibility.Visible) Refresh(expander);
            });

            // both live on the expander and capture it; kept, every Loaded adds one that holds the whole tree alive
            expander.Unloaded += (s, e) =>
            {
                expander.Expanded -= expandedHandler;
                expander.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, visibilityToken);
            };
        }

        private static void Refresh(SettingsExpander expander)
        {
            var repeater = expander.Tag as ItemsRepeater ?? expander.FindDescendant("PART_ItemsRepeater") as ItemsRepeater;
            if (repeater == null) return;

            expander.Tag = repeater;
            expander.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => repeater.InvalidateMeasure());
        }
    }
}
