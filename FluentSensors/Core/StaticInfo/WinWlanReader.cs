using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;


namespace FluentSensors.Core.StaticInfo
{
    // the connection of one Wi-Fi adapter;
    // the network name is left out on purpose, it would end up on screenshots
    public record WinWlanConnection(
        Guid InterfaceId, // NetworkInterface.Id of the adapter
        uint PhyType, // DOT11_PHY_TYPE, see HardwareInfoFormatter.FormatWlanStandard
        uint AuthAlgorithm, // DOT11_AUTH_ALGORITHM, see HardwareInfoFormatter.FormatWlanSecurity
        uint Channel, // 0 when not reported
        uint FrequencyMhz, // channel center frequency, 0 when the access point is not in the scan list
        uint SignalQualityPercent,
        int? RssiDbm, // null when the access point is not in the scan list
        uint ReceiveRateKbps,
        uint TransmitRateKbps
    );

    // the Wi-Fi connections, from the native wlan api:
    // https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanqueryinterface
    public static partial class WinWlanReader
    {
        // === fields ===

        private const uint ClientVersion = 2; // Windows Vista and newer

        // WLAN_INTERFACE_INFO_LIST: count, index, then one WLAN_INTERFACE_INFO each (guid, description, state)
        private const int InterfaceListHeaderSize = 8;
        private const int InterfaceInfoSize = 532;
        private const int InterfaceStateOffset = 528;
        private const uint InterfaceStateConnected = 1;

        // WLAN_INTF_OPCODE
        private const int OpcodeCurrentConnection = 7;
        private const int OpcodeChannelNumber = 8;

        // WLAN_CONNECTION_ATTRIBUTES: state, mode, profile name (256 chars), then the association and the security
        // attributes
        private const int ConnectionSize = 604;
        private const int ConnectionBssidOffset = 560;
        private const int ConnectionPhyTypeOffset = 568;
        private const int ConnectionSignalQualityOffset = 576;
        private const int ConnectionReceiveRateOffset = 580;
        private const int ConnectionTransmitRateOffset = 584;
        private const int ConnectionAuthAlgorithmOffset = 596;

        // WLAN_BSS_LIST: total size, count, then one WLAN_BSS_ENTRY each
        private const int BssListHeaderSize = 8;
        private const int BssEntrySize = 360;
        private const int BssEntryBssidOffset = 40;
        private const int BssEntryRssiOffset = 56;
        private const int BssEntryFrequencyOffset = 92; // kHz

        private const int BssTypeAny = 3; // dot11_BSS_type_any


        // === public api ===

        // every adapter that is connected; empty without a Wi-Fi adapter or without the WLAN service
        public static List<WinWlanConnection> QueryConnections()
        {
            var result = new List<WinWlanConnection>();
            IntPtr client = IntPtr.Zero;
            IntPtr interfaceList = IntPtr.Zero;

            try
            {
                if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out client) != 0) return result;
                if (WlanEnumInterfaces(client, IntPtr.Zero, out interfaceList) != 0) return result;

                int count = Marshal.ReadInt32(interfaceList);
                for (int i = 0; i < count; i++)
                {
                    IntPtr info = interfaceList + InterfaceListHeaderSize + i * InterfaceInfoSize;
                    if ((uint)Marshal.ReadInt32(info + InterfaceStateOffset) != InterfaceStateConnected) continue;

                    var interfaceId = Marshal.PtrToStructure<Guid>(info);
                    var connection = ReadConnection(client, interfaceId);
                    if (connection != null) result.Add(connection);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // no wlanapi.dll (a server without the wireless feature)
            }
            finally
            {
                if (interfaceList != IntPtr.Zero) WlanFreeMemory(interfaceList);
                if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
            }

            return result;
        }


        // === private helpers ===

        private static WinWlanConnection? ReadConnection(IntPtr client, Guid interfaceId)
        {
            if (WlanQueryInterface(client, ref interfaceId, OpcodeCurrentConnection, IntPtr.Zero, out uint size, out IntPtr data, IntPtr.Zero) != 0) return null;

            try
            {
                if (size < ConnectionSize) return null;

                var bssid = new byte[6];
                Marshal.Copy(data + ConnectionBssidOffset, bssid, 0, 6);

                var (frequencyMhz, rssi) = ReadAccessPoint(client, interfaceId, bssid);

                return new WinWlanConnection(
                    interfaceId,
                    (uint)Marshal.ReadInt32(data + ConnectionPhyTypeOffset),
                    (uint)Marshal.ReadInt32(data + ConnectionAuthAlgorithmOffset),
                    ReadChannel(client, interfaceId),
                    frequencyMhz,
                    (uint)Marshal.ReadInt32(data + ConnectionSignalQualityOffset),
                    rssi,
                    (uint)Marshal.ReadInt32(data + ConnectionReceiveRateOffset),
                    (uint)Marshal.ReadInt32(data + ConnectionTransmitRateOffset));
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }

        private static uint ReadChannel(IntPtr client, Guid interfaceId)
        {
            if (WlanQueryInterface(client, ref interfaceId, OpcodeChannelNumber, IntPtr.Zero, out uint size, out IntPtr data, IntPtr.Zero) != 0) return 0;

            try
            {
                return size >= 4 ? (uint)Marshal.ReadInt32(data) : 0;
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }

        // frequency and signal of the access point the adapter is connected to, from the last scan; the list is asked
        // without a network name, so every access point in range is in it
        private static (uint FrequencyMhz, int? Rssi) ReadAccessPoint(IntPtr client, Guid interfaceId, byte[] bssid)
        {
            if (WlanGetNetworkBssList(client, ref interfaceId, IntPtr.Zero, BssTypeAny, 0, IntPtr.Zero, out IntPtr list) != 0) return (0, null);

            try
            {
                int count = Marshal.ReadInt32(list + 4);
                for (int i = 0; i < count; i++)
                {
                    IntPtr entry = list + BssListHeaderSize + i * BssEntrySize;

                    bool isMatch = true;
                    for (int b = 0; b < 6 && isMatch; b++)
                    {
                        isMatch = Marshal.ReadByte(entry + BssEntryBssidOffset + b) == bssid[b];
                    }
                    if (!isMatch) continue;

                    return ((uint)Marshal.ReadInt32(entry + BssEntryFrequencyOffset) / 1000, Marshal.ReadInt32(entry + BssEntryRssiOffset));
                }
            }
            finally
            {
                WlanFreeMemory(list);
            }

            return (0, null);
        }


        // === win32 api imports ===

        [LibraryImport("wlanapi.dll")]
        private static partial int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

        [LibraryImport("wlanapi.dll")]
        private static partial int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

        [LibraryImport("wlanapi.dll")]
        private static partial int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

        [LibraryImport("wlanapi.dll")]
        private static partial int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceId, int opcode, IntPtr reserved, out uint dataSize, out IntPtr data, IntPtr opcodeValueType);

        [LibraryImport("wlanapi.dll")]
        private static partial int WlanGetNetworkBssList(IntPtr clientHandle, ref Guid interfaceId, IntPtr ssid, int bssType, int securityEnabled, IntPtr reserved, out IntPtr bssList);

        [LibraryImport("wlanapi.dll")]
        private static partial void WlanFreeMemory(IntPtr memory);
    }
}
