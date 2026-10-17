using System;
using System.Globalization;
using System.Text.RegularExpressions;

using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Gpu;

using FluentSensors.Core.StaticInfo;


namespace FluentSensors.Core.Lhm
{
    // integrated or discrete gpu:
    // LHM has one hardware type per vendor and none for the role; Intel is told apart by the identifier, NVIDIA is
    // always discrete, AMD needs Windows
    public static partial class LhmGpuRole
    {
        private const string IntelIntegratedPrefix = "/gpu-intel-integrated/";

        public static bool IsIntegrated(IHardware hardware)
        {
            switch (hardware.HardwareType)
            {
                case HardwareType.GpuIntel:
                    return hardware.Identifier.ToString().StartsWith(IntelIntegratedPrefix, StringComparison.Ordinal);

                // KNOWN UNRELIABLE:
                // one LHM class covers Radeon cards and apu graphics, so the answer is the IsIntegrated flag the amd
                // driver hands DXCore; confirmed on an Intel and NVIDIA hybrid laptop only, not on amd hardware
                // an adapter DXCore does not answer for counts as discrete
                case HardwareType.GpuAmd:
                    return hardware is GenericGpu gpu
                        && TryParsePciIds(gpu.DeviceId, out uint vendorId, out uint deviceId)
                        && WinDxCoreAdapters.IsIntegrated(vendorId, deviceId) == true;

                default:
                    return false;
            }
        }

        // "PCI\VEN_1002&DEV_1638&SUBSYS_..."
        private static bool TryParsePciIds(string pnpDeviceId, out uint vendorId, out uint deviceId)
        {
            vendorId = 0;
            deviceId = 0;
            if (string.IsNullOrEmpty(pnpDeviceId)) return false;

            var match = PciIdPattern().Match(pnpDeviceId);
            return match.Success
                && uint.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vendorId)
                && uint.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out deviceId);
        }

        [GeneratedRegex(@"VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
        private static partial Regex PciIdPattern();
    }
}
