using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;


namespace FluentHwInfo.Common
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
            expander.Expanded += (s, e) => Refresh(expander);

            // Visibility has no built-in changed event in WinUI, RegisterPropertyChangedCallback is the standard workaround
            expander.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, dp) =>
            {
                if (expander.Visibility == Visibility.Visible) Refresh(expander);
            });
        }

        private static void Refresh(SettingsExpander expander)
        {
            // a still-Collapsed expander never gets its template applied, so PART_ItemsRepeater
            // might not exist yet on the very first Loaded, look it up lazily here instead of only once
            var repeater = expander.Tag as ItemsRepeater ?? expander.FindDescendant("PART_ItemsRepeater") as ItemsRepeater;
            if (repeater == null) return;

            expander.Tag = repeater; // cache it, the template is definitely applied by now

            // low priority so this runs after the current layout/animation pass, not in the middle of it
            expander.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => repeater.InvalidateMeasure());
        }
    }
}