using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.Generic;
using Windows.System;
using Windows.UI.Core;

using FluentSensors.Common.UI;
using FluentSensors.Core.Taskbar;
using FluentSensors.Persistence.Models;


namespace FluentSensors.Features.Settings
{
    // the flyout shortcut dialog:
    // records the next key combination and hands it back on Save; the caller suspends the registered shortcut
    // meanwhile, or it would swallow the keys
    public sealed partial class FlyoutShortcutDialog : ContentDialog
    {
        // === fields ===

        // what Save hands back; null clears the shortcut
        public KeyboardShortcut? Shortcut { get; private set; }

        // a combination another app holds; shown with the warning, Save stays off
        private KeyboardShortcut? _rejected;


        // === constructor ===

        public FlyoutShortcutDialog(KeyboardShortcut? current)
        {
            this.InitializeComponent();
            Shortcut = current;

            // its own popup layer, which the window reset never sees
            PointerFocusReset.Attach(this);

            ShowRecorded();
        }


        // === public helpers ===

        // one accent button per key, [Ctrl][Alt][S]; the settings card button builds its keys here too
        public static void FillKeys(Panel panel, IReadOnlyList<string> keys, Style keyStyle)
        {
            panel.Children.Clear();
            foreach (var key in keys)
            {
                panel.Children.Add(new Button { Style = keyStyle, Content = key });
            }
        }


        // === user interaction ===

        // before the focused button, so every key lands here; Escape, Enter and Tab keep their dialog meaning
        private void Dialog_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            uint modifiers = ReadModifiers();
            if (IsDialogKey(e.Key, modifiers)) return;

            e.Handled = true;

            // the held modifiers show up while waiting for the key
            if (IsModifierKey(e.Key))
            {
                ShowKeys(WinHotkeyService.FormatModifiers(modifiers));
                return;
            }

            // Shift alone would take a plain capital letter away from every app
            if ((modifiers & ~WinHotkeyService.ModShift) == 0)
            {
                if (modifiers == 0 && e.Key is VirtualKey.Back or VirtualKey.Delete)
                {
                    Record(null);
                    return;
                }

                ShowWarning("Start with Ctrl, Alt or Win");
                ShowRecorded();
                return;
            }

            var candidate = new KeyboardShortcut { Modifiers = modifiers, VirtualKey = (uint)e.Key };
            if (!WinHotkeyService.Instance.IsAvailable(candidate.Modifiers, candidate.VirtualKey))
            {
                _rejected = candidate;
                IsPrimaryButtonEnabled = false;
                ShowWarning("Already in use");
                ShowRecorded();
                return;
            }

            Record(candidate);
        }

        // the key up would click the focused button (Space) or reach the page
        private void Dialog_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (IsDialogKey(e.Key, ReadModifiers())) return;

            e.Handled = true;

            // modifiers let go without a key: back to what is recorded
            if (IsModifierKey(e.Key)) ShowRecorded();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) => Record(null);


        // === private helpers ===

        private void Record(KeyboardShortcut? shortcut)
        {
            Shortcut = shortcut;
            _rejected = null;
            IsPrimaryButtonEnabled = true;
            WarningInfoBar.IsOpen = false;
            ShowRecorded();
        }

        private void ShowRecorded()
        {
            var shown = _rejected ?? Shortcut;
            ShowKeys(shown != null ? WinHotkeyService.FormatKeys(shown.Modifiers, shown.VirtualKey) : new List<string>());
        }

        // the keys or the placeholder; the area announces the change to a screen reader
        private void ShowKeys(IReadOnlyList<string> keys)
        {
            FillKeys(KeysPanel, keys, (Style)Resources["LargeShortcutKeyStyle"]);
            NoShortcutText.Visibility = keys.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            AutomationProperties.SetName(KeysArea, keys.Count > 0 ? $"Shortcut, {string.Join("+", keys)}" : "No shortcut");
            FrameworkElementAutomationPeer.CreatePeerForElement(KeysArea)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        private void ShowWarning(string message)
        {
            WarningInfoBar.Message = message;
            WarningInfoBar.IsOpen = true;
        }

        // Escape cancels and Enter saves without a modifier; Tab and Shift+Tab move between the buttons
        private static bool IsDialogKey(VirtualKey key, uint modifiers) =>
            (modifiers == 0 && key is VirtualKey.Escape or VirtualKey.Enter)
            || (key == VirtualKey.Tab && (modifiers & ~WinHotkeyService.ModShift) == 0);

        private static uint ReadModifiers()
        {
            uint modifiers = 0;
            if (IsKeyDown(VirtualKey.Control)) modifiers |= WinHotkeyService.ModControl;
            if (IsKeyDown(VirtualKey.Menu)) modifiers |= WinHotkeyService.ModAlt;
            if (IsKeyDown(VirtualKey.Shift)) modifiers |= WinHotkeyService.ModShift;
            if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows)) modifiers |= WinHotkeyService.ModWin;
            return modifiers;
        }

        private static bool IsModifierKey(VirtualKey key) => key is
            VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
            VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
            VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
            VirtualKey.LeftWindows or VirtualKey.RightWindows;

        private static bool IsKeyDown(VirtualKey key) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    }
}
