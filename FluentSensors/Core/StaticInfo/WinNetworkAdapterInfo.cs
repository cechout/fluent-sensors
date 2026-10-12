using System.Collections.Generic;
using System.Net.NetworkInformation;


namespace FluentSensors.Core.StaticInfo
{
    // the driver of one adapter, from MSFT_NetAdapter
    public record WinNetworkDriverInfo(
        string Version,
        string Date, // "2026-05-20", "" when not reported
        string Provider
    );

    // the Wi-Fi facts that hold for a connection; signal and rates move all the time and are no fact
    public record WinWlanInfo(
        uint PhyType, // DOT11_PHY_TYPE, see HardwareInfoFormatter.FormatWlanStandard
        uint AuthAlgorithm, // DOT11_AUTH_ALGORITHM, see HardwareInfoFormatter.FormatWlanSecurity
        uint Channel,
        uint FrequencyMhz // 0 when the access point was not in the last scan
    );

    public record WinNetworkAdapterInfo(
        string Name,
        string Description,
        string MacAddress,
        long SpeedBitsPerSecond,

        // both directions apart; they differ on Wi-Fi, on a cable both equal SpeedBitsPerSecond
        long ReceiveSpeedBitsPerSecond,
        long TransmitSpeedBitsPerSecond,
        NetworkInterfaceType InterfaceType,
        IReadOnlyList<string> IPv4Addresses,
        IReadOnlyList<string> IPv6Addresses,
        bool DhcpEnabled,
        IReadOnlyList<string> Gateways,
        IReadOnlyList<string> DnsServers,
        IReadOnlyList<string> DhcpServers,
        WinNetworkDriverInfo? Driver,
        WinWlanInfo? Wlan // null for an adapter that is not Wi-Fi
    );
}
