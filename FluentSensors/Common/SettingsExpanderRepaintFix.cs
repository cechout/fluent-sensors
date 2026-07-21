using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;


namespace FluentSensors.Common
{
    // WinUI bug: a SettingsExpander's internal ItemsRepeater stops rendering after repeated collapse/expand (or show/hide)
    // cycles and stays blank until something else forces a layout pass
    // see https://github.com/microsoft/microsoft-ui-xaml/issues/9337
    // this forces that pass manually right when its needed, instead of waiting for the user to accidentally trigger one
    // by scrolling or resizing the window
    public static class SettingsExpanderRepaintFix
    {
        // call this once from the SettingsExpander's own Loaded event
        public static void Attach(SettingsExpander expander)
        {
            // named handler instead of an inline lambda so it can actually be removed again in Unloaded below
            EventHandler expandedHandler = (s, e) => Refresh(expander);
            expander.Expanded += expandedHandler;

            // Visibility has no built-in changed event in WinUI, RegisterPropertyChangedCallback is the standard
            // workaround; the returned token is required to unregister it again
            long visibilityToken = expander.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, dp) =>
            {
                if (expander.Visibility == Visibility.Visible) Refresh(expander);
            });

            // both registrations above are stored on the expander itself and capture the expander back - without
            // removing them here, every Loaded cycle (i.e. every reopen that recreates this control) adds another
            // permanent, unremovable subscription that keeps the control and its whole visual tree alive
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