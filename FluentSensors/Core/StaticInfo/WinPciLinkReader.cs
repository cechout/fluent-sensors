using System;
using System.Runtime.InteropServices;


namespace FluentSensors.Core.StaticInfo
{
    // the PCI Express link of one device: what it negotiated and what it could do at most
    // speed is the generation (1 = 2.5 GT/s, 4 = 16 GT/s), width the number of lanes
    public record WinPciLink(uint CurrentSpeed, uint CurrentWidth, uint MaxSpeed, uint MaxWidth);

    // the link facts of a device, from its PnP device properties:
    // the DEVPKEY_PciDevice keys are declared in pciprop.h of the Windows SDK and have no page of their own; read with
    // https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_devnode_propertyw
    public static partial class WinPciLinkReader
    {
        // === fields ===

        private static readonly Guid PciDeviceCategory = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62"); // DEVPKEY_PciDevice_*
        private const uint PropertyCurrentLinkSpeed = 9;
        private const uint PropertyCurrentLinkWidth = 10;
        private const uint PropertyMaxLinkSpeed = 11;
        private const uint PropertyMaxLinkWidth = 12;

        private const uint PropertyTypeUInt32 = 7; // DEVPROP_TYPE_UINT32

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid Category;
            public uint Id;
        }


        // === public api ===

        // null when the device has no link (an integrated gpu on the cpu) or hides it
        // parentLevels walks up the device tree: a drive is a disk below its controller, which holds the link; a
        // controller behind Intel VMD shows none
        public static WinPciLink? Query(string pnpDeviceId, int parentLevels = 0)
        {
            if (string.IsNullOrEmpty(pnpDeviceId)) return null;

            try
            {
                if (CM_Locate_DevNodeW(out uint device, pnpDeviceId, 0) != 0) return null;

                for (int level = 0; ; level++)
                {
                    var link = ReadLink(device);
                    if (link != null) return link;

                    if (level >= parentLevels || CM_Get_Parent(out device, device, 0) != 0) return null;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return null; // no cfgmgr32
            }
        }


        // === private helpers ===

        private static WinPciLink? ReadLink(uint device)
        {
            uint? currentSpeed = ReadUInt32(device, PropertyCurrentLinkSpeed);
            uint? currentWidth = ReadUInt32(device, PropertyCurrentLinkWidth);
            if (currentSpeed is null or 0 || currentWidth is null or 0) return null;

            return new WinPciLink(
                currentSpeed.Value,
                currentWidth.Value,
                ReadUInt32(device, PropertyMaxLinkSpeed) ?? 0,
                ReadUInt32(device, PropertyMaxLinkWidth) ?? 0);
        }

        private static uint? ReadUInt32(uint device, uint propertyId)
        {
            var key = new PropertyKey { Category = PciDeviceCategory, Id = propertyId };
            var buffer = new byte[4];
            uint size = 4;

            if (CM_Get_DevNode_PropertyW(device, ref key, out uint type, buffer, ref size, 0) != 0) return null;
            if (type != PropertyTypeUInt32 || size != 4) return null;

            return BitConverter.ToUInt32(buffer, 0);
        }


        // === win32 api imports ===

        [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int CM_Locate_DevNodeW(out uint device, string deviceId, uint flags);

        [LibraryImport("cfgmgr32.dll")]
        private static partial int CM_Get_Parent(out uint parent, uint device, uint flags);

        [LibraryImport("cfgmgr32.dll")]
        private static partial int CM_Get_DevNode_PropertyW(uint device, ref PropertyKey key, out uint propertyType, [Out] byte[] buffer, ref uint bufferSize, uint flags);
    }
}
