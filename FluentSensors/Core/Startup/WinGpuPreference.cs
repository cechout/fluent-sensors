using System;
using Microsoft.Win32;

using FluentSensors.Common;
using FluentSensors.Core.StaticInfo;


namespace FluentSensors.Core.Startup
{
    // the graphics preference of this exe, as the windows graphics settings page stores it:
    // on a hybrid laptop the screen hangs on the integrated gpu, which also runs the desktop compositor; an app that
    // renders on the discrete gpu has every frame copied across, and the composition animations of the flyout then miss
    // every second or third refresh (12 to 36 ms steps instead of 6 at 165 Hz, worse under a debugger)
    // power saving is only written while no gpu is chosen (no value, or let windows decide), so a choice of a gpu
    // always wins; it is taken back when the screens no longer hang on the power saving gpu (a monitor on the
    // discrete card)
    // not in the store build: its HKCU writes land in a private hive, and any value there (a delete leaves a REG_NONE
    // marker) hides the real one for good, so a later choice on the settings page would never reach DirectX
    public static class WinGpuPreference
    {
        // === fields ===

        private const string PreferencesKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
        private const string PreferenceName = "GpuPreference";
        private const string PowerSavingValue = "GpuPreference=1;";


        // === public api ===

        // first thing in Main; DirectX reads the preference once per process, and WinUI asks inside Application.Start
        public static void Apply()
        {
            if (AppDistribution.IsPackaged) return;

            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            try
            {
                bool? screensOnPowerSavingGpu = ScreensOnPowerSavingGpu();
                if (screensOnPowerSavingGpu == null) return;

                using var key = Registry.CurrentUser.CreateSubKey(PreferencesKeyPath);
                var current = key.GetValue(exePath) as string;

                if (screensOnPowerSavingGpu == true && IsUnchosen(current))
                {
                    key.SetValue(exePath, WithPowerSaving(current), RegistryValueKind.String);
                }
                else if (screensOnPowerSavingGpu == false && current == PowerSavingValue)
                {
                    key.DeleteValue(exePath, false);
                }
            }
            catch
            {
                // no registry access or no display config; the app runs on whatever gpu windows hands it
            }
        }


        // === private helpers ===

        // the value is a list of name=value; entries, other settings of the page (windowed game optimizations) can sit
        // beside the gpu; let windows decide is stored as 0
        private static bool IsUnchosen(string? value)
        {
            if (value == null) return true;

            foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = entry.Split('=', 2);
                if (parts.Length == 2 && parts[0] == PreferenceName) return parts[1] == "0";
            }
            return true;
        }

        // the other entries kept as they are
        private static string WithPowerSaving(string? value)
        {
            var result = PowerSavingValue;
            if (value == null) return result;

            foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!entry.StartsWith(PreferenceName + "=", StringComparison.Ordinal)) result += entry + ";";
            }
            return result;
        }

        // true when every screen hangs on the integrated gpu of a hybrid system, false on a single gpu or when a screen
        // hangs on another one, null when it cannot tell; (WinDisplayTopology touches no DXGI, which would read the
        // preference for this process on its first factory and keep it, before the value above is written)
        private static bool? ScreensOnPowerSavingGpu()
        {
            var displays = WinDisplayTopology.QueryDisplays();
            if (displays == null || displays.Count == 0) return null;

            foreach (var display in displays)
            {
                var role = WinDisplayTopology.HybridRole(display.AdapterLuid);
                if (role == null) return null;
                if (role != WinHybridRole.Integrated) return false;
            }

            return true;
        }
    }
}
