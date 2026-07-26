using FluentSensors.Core.StaticInfo;


namespace FluentSensors.Diagnostics
{
    // developer diagnostic tool (not part of any feature):
    // dumps everything WinStaticInfoService collected to the Debug output window, to verify the WMI/native queries
    // actually returned plausible data
    // Call this manually from anywhere (e.g. MainWindow constructor, wrapped in Task.Run since first access to
    // WinStaticInfoService.Instance blocks on WMI) whenever the static-info backend needs re-checking
    public static class WinStaticInfoDebugDump
    {
        public static void Dump()
        {
            var info = WinStaticInfoService.Instance;

            System.Diagnostics.Debug.WriteLine("========== WinStaticInfoService Dump ==========");

            // cpu
            System.Diagnostics.Debug.WriteLine("--- CPU ---");
            System.Diagnostics.Debug.WriteLine($"PhysicalCores={info.Cpu.PhysicalCores}");
            System.Diagnostics.Debug.WriteLine($"LogicalProcessors={info.Cpu.LogicalProcessors}");
            System.Diagnostics.Debug.WriteLine($"L2CacheSizeKb={info.Cpu.L2CacheSizeKb}");
            System.Diagnostics.Debug.WriteLine($"L3CacheSizeKb={info.Cpu.L3CacheSizeKb}");
            System.Diagnostics.Debug.WriteLine($"MaxClockSpeedMhz={info.Cpu.MaxClockSpeedMhz}");
            System.Diagnostics.Debug.WriteLine($"SocketDesignation={info.Cpu.SocketDesignation}");
            System.Diagnostics.Debug.WriteLine($"VirtualizationFirmwareEnabled={info.Cpu.VirtualizationFirmwareEnabled} (known unreliable, see WinCpuInfo)");
            System.Diagnostics.Debug.WriteLine($"VirtualizationExtensionsSupported={info.Cpu.VirtualizationExtensionsSupported} (known unreliable, see WinCpuInfo)");

            System.Diagnostics.Debug.WriteLine($"CoreTopology entries: {info.Cpu.CoreTopology.Count}");
            foreach (var core in info.Cpu.CoreTopology)
            {
                string logicalProcs = string.Join(",", core.LogicalProcessorIndices);
                System.Diagnostics.Debug.WriteLine(
                    $"  Core #{core.CoreIndex} | EfficiencyClass={core.EfficiencyClass} | HasSmt={core.HasSmt} | LogicalProcessors=[{logicalProcs}]");
            }

            // gpu
            System.Diagnostics.Debug.WriteLine($"--- GPUs ({info.Gpus.Count}) ---");
            foreach (var gpu in info.Gpus)
            {
                System.Diagnostics.Debug.WriteLine($"  Name='{gpu.Name}' | Driver={gpu.DriverVersion} | PnpId={gpu.PnpDeviceId}");
            }

            // memory
            System.Diagnostics.Debug.WriteLine("--- Memory ---");
            System.Diagnostics.Debug.WriteLine($"TotalSlots={info.Memory.TotalSlots}");
            System.Diagnostics.Debug.WriteLine($"Modules: {info.Memory.Modules.Count}");
            foreach (var module in info.Memory.Modules)
            {
                double capacityGb = module.CapacityBytes / 1_073_741_824.0;
                System.Diagnostics.Debug.WriteLine(
                    $"  {module.DeviceLocator} | Manufacturer='{module.Manufacturer}' | PartNumber='{module.PartNumber}' | " +
                    $"Capacity={capacityGb:0.0} GB | ConfiguredClock={module.ConfiguredClockSpeedMhz} MHz | " +
                    $"SmbiosMemoryType={module.SmbiosMemoryType} | TotalWidth={module.TotalWidthBits} | DataWidth={module.DataWidthBits}");
            }

            // drives
            System.Diagnostics.Debug.WriteLine($"--- Drives ({info.Drives.Count}) ---");
            foreach (var drive in info.Drives)
            {
                double sizeGb = drive.SizeBytes / 1_073_741_824.0;
                System.Diagnostics.Debug.WriteLine(
                    $"  FriendlyName='{drive.FriendlyName}' | Serial='{drive.SerialNumber}' | Firmware='{drive.FirmwareRevision}' | " +
                    $"BusType={drive.BusType} | Size={sizeGb:0.0} GB | PnpId={drive.PnpDeviceId}");
            }

            // network adapters
            System.Diagnostics.Debug.WriteLine($"--- Network Adapters ({info.NetworkAdapters.Count}) ---");
            foreach (var nic in info.NetworkAdapters)
            {
                string ips = string.Join(", ", nic.IpAddresses);
                System.Diagnostics.Debug.WriteLine(
                    $"  Name='{nic.Name}' | Description='{nic.Description}' | MAC={nic.MacAddress} | " +
                    $"Speed={nic.SpeedBitsPerSecond / 1_000_000} Mbps | Type={nic.InterfaceType} | IPs=[{ips}] | Dhcp={nic.DhcpEnabled}");
            }

            // motherboard
            System.Diagnostics.Debug.WriteLine("--- Motherboard ---");
            System.Diagnostics.Debug.WriteLine(
                $"Manufacturer='{info.Motherboard.Manufacturer}' | Product='{info.Motherboard.Product}' | " +
                $"Version='{info.Motherboard.Version}' | BiosVersion='{info.Motherboard.BiosVersion}' | BiosReleaseDate='{info.Motherboard.BiosReleaseDate}'");

            System.Diagnostics.Debug.WriteLine("========== End Dump ==========");
        }
    }
}