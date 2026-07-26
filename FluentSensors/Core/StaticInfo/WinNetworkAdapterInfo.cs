using System.Collections.Generic;
using System.Net.NetworkInformation;


namespace FluentSensors.Core.StaticInfo
{
    public record WinNetworkAdapterInfo(
        string Name,
        string Description,
        string MacAddress,
        long SpeedBitsPerSecond,
        NetworkInterfaceType InterfaceType,
        IReadOnlyList<string> IpAddresses,
        bool DhcpEnabled
    );
}