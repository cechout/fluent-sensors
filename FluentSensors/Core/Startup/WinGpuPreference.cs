using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

using FluentSensors.Common;


namespace FluentSensors.Core.Startup
{
    // the graphics preference of this exe, as the windows graphics settings page stores it:
    // on a hybrid laptop the screen hangs on the integrated gpu, which also runs the desktop compositor; an app that
    // renders on the discrete gpu has every frame copied across, and the composition animations of the flyout then miss
    // every second or third refresh (12 to 36 ms steps instead of 6 at 165 Hz, worse under a debugger)
    // power saving is only written while the value is missing, so a choice the user made always wins; it is taken back
    // when the screens no longer hang on the power saving gpu (a desktop with the monitor on the discrete card)
    public static partial class WinGpuPreference
    {
        // === fields ===

        private const string PreferencesKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
        private const string PowerSavingValue = "GpuPreference=1;";

        // display config
        private const uint QdcOnlyActivePaths = 0x2;
        private const int PathInfoSize = 72; // DISPLAYCONFIG_PATH_INFO
        private const int ModeInfoSize = 64; // DISPLAYCONFIG_MODE_INFO

        // adapter type; KMTQAITYPE_ADAPTERTYPE answers a D3DKMT_ADAPTERTYPE bit field
        private const int KmtQaiTypeAdapterType = 15;
        private const uint AdapterTypeHybridIntegrated = 1u << 5;


        // === public api ===

        // first thing in Main; DirectX reads the preference once per process, and WinUI asks inside Application.Start
        public static void Apply()
        {
            // a packaged app writes HKCU into its private hive, which DirectX never reads
            if (AppDistribution.IsPackaged) return;

            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            try
            {
                bool? screensOnPowerSavingGpu = ScreensOnPowerSavingGpu();
                if (screensOnPowerSavingGpu == null) return;

                using var key = Registry.CurrentUser.CreateSubKey(PreferencesKeyPath);
                var current = key.GetValue(exePath) as string;

                if (current == null && screensOnPowerSavingGpu == true)
                {
                    key.SetValue(exePath, PowerSavingValue, RegistryValueKind.String);
                }
                else if (current == PowerSavingValue && screensOnPowerSavingGpu == false)
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

        // true when every screen hangs on the integrated gpu of a hybrid system, false on a single gpu or when a screen
        // hangs on another one, null when it cannot tell
        // the screens come from the display config, which names the adapter that scans out, and the kernel flags each
        // adapter as the integrated or discrete half of a hybrid pair; DXGI would read the preference for this process
        // on its first factory and keep it, before the value below is written
        private static bool? ScreensOnPowerSavingGpu()
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0) return null;

            var paths = new byte[pathCount * PathInfoSize];
            var modes = new byte[modeCount * ModeInfoSize];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return null;
            if (pathCount == 0) return null;

            for (int i = 0; i < pathCount; i++)
            {
                // sourceInfo.adapterId, the first field of DISPLAYCONFIG_PATH_INFO
                var open = new D3DKMT_OPENADAPTERFROMLUID
                {
                    AdapterLuidLowPart = BitConverter.ToUInt32(paths, i * PathInfoSize),
                    AdapterLuidHighPart = BitConverter.ToInt32(paths, i * PathInfoSize + 4)
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
