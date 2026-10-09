using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

using FluentSensors.Common;


namespace FluentSensors.Core.Startup
{
    // one active screen: the adapter that scans it out and its refresh rate
    public record WinScreenPath(long AdapterLuid, int RefreshRateHz);

    // the graphics preference of this exe, as the windows graphics settings page stores it:
    // on a hybrid laptop the screen hangs on the integrated gpu, which also runs the desktop compositor; an app that
    // renders on the discrete gpu has every frame copied across, and the composition animations of the flyout then miss
    // every second or third refresh (12 to 36 ms steps instead of 6 at 165 Hz, worse under a debugger)
    // power saving is only written while no gpu is chosen (no value, or let windows decide), so a choice of a gpu
    // always wins; it is taken back when the screens no longer hang on the power saving gpu (a monitor on the
    // discrete card)
    // not in the store build: its HKCU writes land in a private hive, and any value there (a delete leaves a REG_NONE
    // marker) hides the real one for good, so a later choice on the settings page would never reach DirectX
    public static partial class WinGpuPreference
    {
        // === fields ===

        private const string PreferencesKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
        private const string PreferenceName = "GpuPreference";
        private const string PowerSavingValue = "GpuPreference=1;";

        // display config
        private const uint QdcOnlyActivePaths = 0x2;
        private const int PathInfoSize = 72; // DISPLAYCONFIG_PATH_INFO
        private const int ModeInfoSize = 64; // DISPLAYCONFIG_MODE_INFO
        private const int PathSourceAdapterOffset = 0; // sourceInfo.adapterId
        private const int PathTargetRefreshRateOffset = 48; // targetInfo.refreshRate

        // adapter type; KMTQAITYPE_ADAPTERTYPE answers a D3DKMT_ADAPTERTYPE bit field
        private const int KmtQaiTypeAdapterType = 15;
        private const uint AdapterTypeHybridIntegrated = 1u << 5;


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


        // the active screens from the display config, which names the adapter that scans out; DXGI hands the outputs
        // of a hybrid laptop to whichever gpu it prefers for this process
        public static List<WinScreenPath>? ScreenAdapters()
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0) return null;

            var paths = new byte[pathCount * PathInfoSize];
            var modes = new byte[modeCount * ModeInfoSize];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return null;

            var result = new List<WinScreenPath>();
            for (int i = 0; i < pathCount; i++)
            {
                int path = i * PathInfoSize;
                long adapter = (long)BitConverter.ToInt32(paths, path + PathSourceAdapterOffset + 4) << 32
                    | BitConverter.ToUInt32(paths, path + PathSourceAdapterOffset);
                uint numerator = BitConverter.ToUInt32(paths, path + PathTargetRefreshRateOffset);
                uint denominator = BitConverter.ToUInt32(paths, path + PathTargetRefreshRateOffset + 4);

                result.Add(new WinScreenPath(adapter, denominator == 0 ? 0 : (int)Math.Round((double)numerator / denominator)));
            }
            return result;
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
        // hangs on another one, null when it cannot tell
        // the kernel flags each adapter as the integrated or discrete half of a hybrid pair; DXGI would read the
        // preference for this process on its first factory and keep it, before the value below is written
        private static bool? ScreensOnPowerSavingGpu()
        {
            var screens = ScreenAdapters();
            if (screens == null || screens.Count == 0) return null;

            foreach (var screen in screens)
            {
                var open = new D3DKMT_OPENADAPTERFROMLUID
                {
                    AdapterLuidLowPart = (uint)screen.AdapterLuid,
                    AdapterLuidHighPart = (int)(screen.AdapterLuid >> 32)
                };

                uint? adapterType = QueryAdapterType(ref open);
                if (adapterType == null) return null;
                if ((adapterType.Value & AdapterTypeHybridIntegrated) == 0) return false;
            }

            return true;
        }

        private static unsafe uint? QueryAdapterType(ref D3DKMT_OPENADAPTERFROMLUID open)
        {
            if (D3DKMTOpenAdapterFromLuid(ref open) != 0) return null;

            try
            {
                uint adapterType = 0;
                var query = new D3DKMT_QUERYADAPTERINFO
                {
                    hAdapter = open.hAdapter,
                    Type = KmtQaiTypeAdapterType,
                    pPrivateDriverData = (IntPtr)(&adapterType),
                    PrivateDriverDataSize = sizeof(uint)
                };
                return D3DKMTQueryAdapterInfo(ref query) == 0 ? adapterType : null;
            }
            finally
            {
                var close = new D3DKMT_CLOSEADAPTER { hAdapter = open.hAdapter };
                D3DKMTCloseAdapter(ref close);
            }
        }


        // === native ===

        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_openadapterfromluid
        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_OPENADAPTERFROMLUID
        {
            public uint AdapterLuidLowPart;
            public int AdapterLuidHighPart;
            public uint hAdapter;
        }

        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_queryadapterinfo
        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_QUERYADAPTERINFO
        {
            public uint hAdapter;
            public int Type;
            public IntPtr pPrivateDriverData;
            public uint PrivateDriverDataSize;
        }

        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_closeadapter
        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_CLOSEADAPTER
        {
            public uint hAdapter;
        }

        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/nf-d3dkmthk-d3dkmtopenadapterfromluid
        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID openAdapter);

        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/nf-d3dkmthk-d3dkmtqueryadapterinfo
        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO queryAdapterInfo);

        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/nf-d3dkmthk-d3dkmtcloseadapter
        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER closeAdapter);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdisplayconfigbuffersizes
        [LibraryImport("user32.dll")]
        private static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig
        [LibraryImport("user32.dll")]
        private static partial int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] byte[] pathArray,
            ref uint numModeInfoArrayElements, [Out] byte[] modeInfoArray, IntPtr currentTopologyId);
    }
}
