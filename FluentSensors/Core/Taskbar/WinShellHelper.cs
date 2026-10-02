using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;


namespace FluentSensors.Core.Taskbar
{
    // the shell helper:
    // on Windows 11 Shell_TrayWnd hosts XAML Islands (Widgets among them); with Widgets not initialized since boot it
    // may reject a foreign SetParent until the host is woken
    // https://learn.microsoft.com/en-us/windows/apps/develop/widgets/
    internal static class WinShellHelper
    {
        // === win32 api imports ===

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SendNotifyMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam);

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const string AdvancedRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string TaskbarDaValueName = "TaskbarDa";


        // === public methods ===

        // wakes the widgets host by briefly toggling TaskbarDa and notifying Explorer
        internal static async Task<bool> WakeWidgetsSubsystemAsync()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AdvancedRegistryKeyPath, true);
                if (key != null)
                {
                    object rawValue = key.GetValue(TaskbarDaValueName);
                    int currentValue = rawValue is int val ? val : 0;

                    // to 1 (shown) and, if it was off, back to 0; that makes Explorer initialize its XAML host
                    key.SetValue(TaskbarDaValueName, 1, RegistryValueKind.DWord);
                    SendNotifyMessage(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "TraySettings");

                    await Task.Delay(250);

                    if (currentValue == 0)
                    {
                        key.SetValue(TaskbarDaValueName, 0, RegistryValueKind.DWord);
                        SendNotifyMessage(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "TraySettings");
                    }

                    await Task.Delay(200);
                    return true;
                }
            }
            catch
            {
                // the protocol launch below
            }

            try
            {
                // fallback: the ms-widgets protocol wakes the host
                var psi = new ProcessStartInfo
                {
                    FileName = "ms-widgets:",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                await Task.Delay(300);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
