using System;

using FluentSensors.Core.Taskbar;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.TaskbarWidget
{
    // the flyout shortcut:
    // registered while the taskbar widget is open and a shortcut is set, released otherwise, so a closed widget never
    // holds a key combination another app could use; a press toggles the flyout like a click on the widget
    public static class FlyoutShortcutRegistration
    {
        // === fields ===

        private static bool _isInitialized;
        private static bool _isSuspended;


        // === public api ===

        // the registered shortcut as text ("Ctrl+Alt+S"), null when none is registered
        public static string? RegisteredText
        {
            get
            {
                var shortcut = SettingsService.Instance.TaskbarFlyoutShortcut;
                if (shortcut == null || !WinHotkeyService.Instance.IsRegistered) return null;
                return WinHotkeyService.Format(shortcut.Modifiers, shortcut.VirtualKey);
            }
        }

        // hooked once, from the first taskbar widget, before it reports itself open
        public static void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            TaskbarWidgetWindow.WidgetStateChanged += Update;
            SettingsService.Instance.TaskbarFlyoutShortcutChanged += Update;
            WinHotkeyService.Instance.Pressed += OnPressed;
        }

        // the settings page records a new shortcut; registered, the current one would swallow the keys it waits for
        public static void SetSuspended(bool suspended)
        {
            _isSuspended = suspended;
            Update();
        }


        // === events ===

        // the hint in the flyout title row follows it
        public static event Action? RegistrationChanged;


        // === private helpers ===

        private static void Update()
        {
            var shortcut = SettingsService.Instance.TaskbarFlyoutShortcut;
            bool shouldRegister = shortcut != null && !_isSuspended && TaskbarWidgetWindow.CurrentInstance != null;

            if (shouldRegister)
            {
                WinHotkeyService.Instance.Register(shortcut!.Modifiers, shortcut.VirtualKey);
            }
            else
            {
                WinHotkeyService.Instance.Unregister();
            }

            RegistrationChanged?.Invoke();
        }

        private static void OnPressed()
        {
            var widget = TaskbarWidgetWindow.CurrentInstance;
            if (widget != null) TaskbarFlyoutWindow.Toggle(widget);
        }
    }
}
