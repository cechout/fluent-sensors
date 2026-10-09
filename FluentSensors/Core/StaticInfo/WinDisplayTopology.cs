using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;


namespace FluentSensors.Core.StaticInfo
{
    // one active display: the adapter that scans it out, its monitor name and refresh rate
    public record WinDisplay(
        long AdapterLuid,
        string FriendlyName, // the EDID name ("DELL U2720Q"); "" when the monitor reports none
        bool IsBuiltIn, // the laptop panel
        int RefreshRateHz
    );

    // the half of a hybrid pair an adapter is, as the kernel flags it
    public enum WinHybridRole
    {
        None, // a single gpu, or one outside a hybrid pair
        Integrated, // drives the panel, the discrete one renders through it
        Discrete
    }

    // which gpu drives which display:
    // the display config names the adapter that scans each display out; DXGI is no help here, it hands the outputs of
    // a hybrid laptop to whichever gpu it prefers for the asking process
    // cheap (well below a millisecond), and it reads no gpu preference, so it is safe before WinUI starts
    public static partial class WinDisplayTopology
    {
        // === fields ===

        private const uint QdcOnlyActivePaths = 0x2;
        private const int PathInfoSize = 72; // DISPLAYCONFIG_PATH_INFO
        private const int ModeInfoSize = 64; // DISPLAYCONFIG_MODE_INFO
        private const int PathSourceAdapterOffset = 0; // sourceInfo.adapterId
        private const int PathTargetAdapterOffset = 20; // targetInfo.adapterId, then targetInfo.id
        private const int PathTargetTechnologyOffset = 36; // targetInfo.outputTechnology
        private const int PathTargetRefreshRateOffset = 48; // targetInfo.refreshRate

        // DISPLAYCONFIG_TARGET_DEVICE_NAME: the header, flags, technology, edid ids, connector, then the names
        private const int TargetNameType = 2; // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
        private const int TargetNameSize = 420;
        private const int TargetNameFriendlyNameOffset = 36;
        private const int TargetNameFriendlyNameChars = 64;

        // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY values of a panel inside the device
        private const uint OutputTechnologyInternal = 0x80000000;
        private const uint OutputTechnologyDisplayPortEmbedded = 11;
        private const uint OutputTechnologyUdiEmbedded = 13;

        // adapter type; KMTQAITYPE_ADAPTERTYPE answers a D3DKMT_ADAPTERTYPE bit field
        private const int KmtQaiTypeAdapterType = 15;
        private const uint AdapterTypeHybridDiscrete = 1u << 4;
        private const uint AdapterTypeHybridIntegrated = 1u << 5;


        // === public api ===

        // null when the display config cannot be read
        public static List<WinDisplay>? QueryDisplays()
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0) return null;

            var paths = new byte[pathCount * PathInfoSize];
            var modes = new byte[modeCount * ModeInfoSize];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return null;

            var result = new List<WinDisplay>();
            for (int i = 0; i < pathCount; i++)
            {
                int path = i * PathInfoSize;
                uint technology = BitConverter.ToUInt32(paths, path + PathTargetTechnologyOffset);
                uint numerator = BitConverter.ToUInt32(paths, path + PathTargetRefreshRateOffset);
                uint denominator = BitConverter.ToUInt32(paths, path + PathTargetRefreshRateOffset + 4);

                result.Add(new WinDisplay(
                    ReadLuid(paths, path + PathSourceAdapterOffset),
                    TargetFriendlyName(paths, path + PathTargetAdapterOffset),
                    technology == OutputTechnologyInternal
                        || technology == OutputTechnologyDisplayPortEmbedded
                        || technology == OutputTechnologyUdiEmbedded,
                    denominator == 0 ? 0 : (int)Math.Round((double)numerator / denominator)));
            }
            return result;
        }

        // null when the adapter cannot be opened
        public static WinHybridRole? HybridRole(long adapterLuid)
        {
            uint? adapterType = QueryAdapterType(adapterLuid);
            if (adapterType == null) return null;

            if ((adapterType.Value & AdapterTypeHybridIntegrated) != 0) return WinHybridRole.Integrated;
            if ((adapterType.Value & AdapterTypeHybridDiscrete) != 0) return WinHybridRole.Discrete;
            return WinHybridRole.None;
        }


        // === private helpers ===

        private static long ReadLuid(byte[] buffer, int offset) =>
            (long)BitConverter.ToInt32(buffer, offset + 4) << 32 | BitConverter.ToUInt32(buffer, offset);

        // the target adapter and id sit in the path in the same order the request header wants them
        private static unsafe string TargetFriendlyName(byte[] paths, int targetOffset)
        {
            var request = new byte[TargetNameSize];
            BitConverter.TryWriteBytes(request.AsSpan(0), TargetNameType);
            BitConverter.TryWriteBytes(request.AsSpan(4), TargetNameSize);
            Array.Copy(paths, targetOffset, request, 8, 12);

            fixed (byte* buffer = request)
            {
                if (DisplayConfigGetDeviceInfo(buffer) != 0) return "";
            }

            var name = MemoryMarshal.Cast<byte, char>(request.AsSpan(TargetNameFriendlyNameOffset, TargetNameFriendlyNameChars * 2));
            int end = name.IndexOf('\0');
            return (end < 0 ? name : name[..end]).ToString().Trim();
        }

        private static unsafe uint? QueryAdapterType(long adapterLuid)
        {
            var open = new D3DKMT_OPENADAPTERFROMLUID
            {
                AdapterLuidLowPart = (uint)adapterLuid,
                AdapterLuidHighPart = (int)(adapterLuid >> 32)
            };
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

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdisplayconfigbuffersizes
        [LibraryImport("user32.dll")]
        private static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig
        [LibraryImport("user32.dll")]
        private static partial int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] byte[] pathArray,
            ref uint numModeInfoArrayElements, [Out] byte[] modeInfoArray, IntPtr currentTopologyId);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo
        [LibraryImport("user32.dll")]
        private static unsafe partial int DisplayConfigGetDeviceInfo(byte* requestPacket);

        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/nf-d3dkmthk-d3dkmtopenadapterfromluid
        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID openAdapter);

        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/nf-d3dkmthk-d3dkmtqueryadapterinfo
        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO queryAdapterInfo);

        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/nf-d3dkmthk-d3dkmtcloseadapter
        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER closeAdapter);
    }
}
