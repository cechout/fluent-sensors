namespace FluentSensors.Persistence.Models
{
    // a global keyboard shortcut, in the terms RegisterHotKey takes
    public class KeyboardShortcut
    {
        public uint Modifiers { get; set; } // WinHotkeyService.Mod* flags, at least one
        public uint VirtualKey { get; set; } // a Windows virtual key code, never a modifier

        public bool SameAs(KeyboardShortcut? other) =>
            other != null && other.Modifiers == Modifiers && other.VirtualKey == VirtualKey;
    }
}
