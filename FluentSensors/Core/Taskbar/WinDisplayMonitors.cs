using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // one active display
    public record WinDisplayMonitor(
        IntPtr Handle, // HMONITOR, valid until the next display change
        string Key, // the monitor device path, stable across restarts and replugging on the same port; the GDI name without one
        string Name, // the EDID name ("DELL U2720Q"), empty for most built-in panels
        bool IsPrimary,
        RectInt32 Bounds // physical screen coordinates
    );

    // the display monitors:
    // every active monitor with a key to save a choice under and a name to show; HMONITOR and the GDI name
    // (\\.\DISPLAY1) are not stable, the monitor device path of the display configuration is
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo
    public static class WinDisplayMonitors
    {
        // === public api ===

        // primary first, then left to right and top to bottom
        public static List<WinDisplayMonitor> GetAll()
        {
            var targets = ReadDisplayTargets();
            var monitors = new List<WinDisplayMonitor>();

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, hdc, rect, data) =>
            {
                var info = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfoW(hMonitor, ref info))
                {
                    targets.TryGetValue(info.szDevice, out var target);
                    monitors.Add(new WinDisplayMonitor(
                        Handle: hMonitor,
                        Key: string.IsNullOrEmpty(target.DevicePath) ? info.szDevice : target.DevicePath,
                        Name: target.FriendlyName ?? string.Empty,
                        IsPrimary: (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        Bounds: new RectInt32(
                            info.rcMonitor.Left,
                            info.rcMonitor.Top,
                            info.rcMonitor.Right - info.rcMonitor.Left,
                            info.rcMonitor.Bottom - info.rcMonitor.Top)));
                }
                return true;
            }, IntPtr.Zero);

            monitors.Sort((a, b) =>
                a.IsPrimary != b.IsPrimary ? (a.IsPrimary ? -1 : 1)
                : a.Bounds.X != b.Bounds.X ? a.Bounds.X.CompareTo(b.Bounds.X)
                : a.Bounds.Y.CompareTo(b.Bounds.Y));
            return monitors;
        }


        // === private helpers ===

        // GDI source name to EDID name and monitor device path, for every active path; empty when the query fails
        private static Dictionary<string, (string? FriendlyName, string? DevicePath)> ReadDisplayTargets()
        {
            var result = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return result;

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;

                for (int i = 0; i < pathCount; i++)
                {
                    var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    source.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    source.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                    source.header.adapterId = paths[i].sourceInfo.adapterId;
                    source.header.id = paths[i].sourceInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

                    var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    target.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                    target.header.adapterId = paths[i].targetInfo.adapterId;
                    target.header.id = paths[i].targetInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref target) != 0) continue;

                    // a cloned display lists one source twice; the first target wins
                    result.TryAdd(source.viewGdiDeviceName, (target.monitorFriendlyDeviceName, target.monitorDevicePath));
                }
            }
            catch (Exception)
            {
                // names and keys fall back to the GDI device name
            }

            return result;
        }


        // === win32 api imports ===
        // DllImport, not LibraryImport; the fixed length strings marshal by value

        private const uint MONITORINFOF_PRIMARY = 1;
        private const uint QDC_ONLY_ACTIVE_PATHS = 2;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public uint cbSize;
            public NativeMethods.RECT rcMonitor;
            public NativeMethods.RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public uint refreshRateNumerator;
            public uint refreshRateDenominator;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        // only sized, the modes are never read
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }
    }
}
