using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;


namespace FluentSensors.Core.Taskbar
{
    // provides helper routines to interact with Windows 11 shell components and wake dormant taskbar subsystems
    //
    // on Windows 11, Shell_TrayWnd hosts XAML Islands for taskbar elements (like Widgets)
    // if the Widgets feature has not been initialized since boot, Shell_TrayWnd composition structures
    // may reject foreign SetParent calls until the shell host is prompted to initialize
    //
    // reference:
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

        // wakes the Windows 11 taskbar widgets subsystem by briefly toggling the TaskbarDa registry setting and notifying Explorer
        internal static async Task<bool> WakeWidgetsSubsystemAsync()
        {
            try
            {
                // step 1: check if TaskbarDa registry key exists
                using var key = Registry.CurrentUser.OpenSubKey(AdvancedRegistryKeyPath, true);
                if (key != null)
                {
                    object rawValue = key.GetValue(TaskbarDaValueName);
                    int currentValue = rawValue is int val ? val : 0;

                    // toggle TaskbarDa to 1 (show) then back to original value (0) to force Explorer to initialize its XAML host
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
                // fallback to protocol launch if registry access fails
            }

            try
            {
                // step 2: fallback to launching the ms-widgets protocol to wake the background host
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
