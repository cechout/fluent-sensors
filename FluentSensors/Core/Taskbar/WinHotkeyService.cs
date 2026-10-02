using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.System;


namespace FluentSensors.Core.Taskbar
{
    // the global hotkey:
    // one system wide shortcut through RegisterHotKey, received on a message-only window like WinShellStateWatcher;
    // used on the UI thread, WM_HOTKEY arrives through its message pump
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
    public class WinHotkeyService
    {
        // === fields ===

        // RegisterHotKey modifier flags
        public const uint ModAlt = 0x1;
        public const uint ModControl = 0x2;
        public const uint ModShift = 0x4;
        public const uint ModWin = 0x8;

        private const string MessageWindowClassName = "FluentSensorsHotkey";
        private const int HotkeyId = 1;
        private const int ProbeHotkeyId = 2; // IsAvailable

        private IntPtr _messageWindowHwnd;
        private NativeMethods.WndProc? _wndProcDelegate;


        // === singleton instance ===

        private static readonly WinHotkeyService _instance = new WinHotkeyService();
        public static WinHotkeyService Instance => _instance;


        // === constructor ===

        private WinHotkeyService() { }


        // === public api ===

        public bool IsRegistered { get; private set; }

        // replaces the previous shortcut; false when another app holds it or Windows reserves it
        public bool Register(uint modifiers, uint virtualKey)
        {
            Unregister();
            EnsureMessageWindow();

            // MOD_NOREPEAT: a held shortcut fires once
            IsRegistered = NativeMethods.RegisterHotKey(_messageWindowHwnd, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
            return IsRegistered;
        }

        public void Unregister()
        {
            if (!IsRegistered) return;

            NativeMethods.UnregisterHotKey(_messageWindowHwnd, HotkeyId);
            IsRegistered = false;
        }

        // registers and releases at once; (the shortcut registered here counts as taken, so unregister before asking)
        public bool IsAvailable(uint modifiers, uint virtualKey)
        {
            EnsureMessageWindow();

            if (!NativeMethods.RegisterHotKey(_messageWindowHwnd, ProbeHotkeyId, modifiers, virtualKey)) return false;
            NativeMethods.UnregisterHotKey(_messageWindowHwnd, ProbeHotkeyId);
            return true;
        }

        // "Ctrl+Alt+S"
        public static string Format(uint modifiers, uint virtualKey)
        {
            var parts = new List<string>();
            if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
            if ((modifiers & ModAlt) != 0) parts.Add("Alt");
            if ((modifiers & ModShift) != 0) parts.Add("Shift");
            if ((modifiers & ModWin) != 0) parts.Add("Win");
            parts.Add(FormatKey(virtualKey));
            return string.Join("+", parts);
        }


        // === events ===

        // on the UI thread
        public event Action? Pressed;


        // === private helpers ===

        private void EnsureMessageWindow()
        {
            if (_messageWindowHwnd != IntPtr.Zero) return;

            _wndProcDelegate = HandleWindowMessage;

            var wndClass = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = NativeMethods.GetModuleHandleW(null),
                lpszClassName = MessageWindowClassName
            };
            NativeMethods.RegisterClassExW(ref wndClass);

            // HWND_MESSAGE as parent makes it message-only
            _messageWindowHwnd = NativeMethods.CreateWindowExW(
                0, MessageWindowClassName, null, 0,
                0, 0, 0, 0,
                NativeMethods.HWND_MESSAGE, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
        }

        private IntPtr HandleWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == NativeMethods.WM_HOTKEY && wParam == HotkeyId)
            {
                Pressed?.Invoke();
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        // letters and digits as printed, F1 to F24, the number pad, punctuation as the layout prints it, the rest by
        // its VirtualKey name
        private static string FormatKey(uint virtualKey)
        {
            if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)virtualKey).ToString();
            if (virtualKey is >= 0x70 and <= 0x87) return $"F{virtualKey - 0x6F}";
            if (virtualKey is >= 0x60 and <= 0x69) return $"Num {virtualKey - 0x60}";

            // the high bit marks a dead key
            uint character = NativeMethods.MapVirtualKeyW(virtualKey, NativeMethods.MAPVK_VK_TO_CHAR) & 0x7FFF;
            if (character > 0x20) return char.ToUpperInvariant((char)character).ToString();

            var key = (VirtualKey)virtualKey;
            return Enum.IsDefined(key) ? key.ToString() : $"Key {virtualKey}";
        }
    }
}
