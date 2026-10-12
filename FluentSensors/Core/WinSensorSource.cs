using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

using FluentSensors.Common.Sensors;
using FluentSensors.Core.StaticInfo;


namespace FluentSensors.Core
{
    // one value Windows measured, shaped like an LHM reading
    public record WinSensorReading(
        string Id, // under SensorSourceMarker.WindowsIdRoot, e.g. "/win/storage/0/temperature/0"
        string Name, // ours, english and never translated, like an LHM name
        string HardwareName, // the LHM hardware it joins, by its payload name, or the name of its own hardware
        string HardwareType,
        string SensorType,
        double Value,
        string[]? ReplacedSensorNames = null, // the LHM sensors of that hardware and type it stands in for
        bool IsOwnHardware = false // hardware LHM does not have (a display); it exists as long as it has a reading
    );


    // the windows sensor source:
    // the second source beside LHM; reads what Windows knows better or alone and hands HardwareMonitorService its last
    // values, which merges them into the payload of every tick
    // WMI and the wlan api are far slower than an LHM read, so this runs on its own thread and cadence and never
    // holds up the polling loop
    // everything is off until the engine settings switch it on
    public class WinSensorSource
    {
        // === fields ===

        // cadence in ms; a drive temperature moves slowly and its WMI round trip is the expensive one
        // Wi-Fi and the displays share the fast one
        private const int WlanIntervalMs = 2000;
        private const double StorageIntervalMs = 6000;

        // DDC/CI goes over the monitor cable and takes tens of milliseconds per value; a level only moves by hand
        private const double DdcIntervalMs = 4000;

        // the LHM names a Windows drive temperature replaces: the NVMe one and the SATA / USB one
        private static readonly string[] ReplacedStorageTemperatureNames = { "Composite Temperature", "Temperature" };

        // kbit/s to the MiB/s every Throughput sensor is in, see HardwareMonitorService
        private const double KilobitsToMebibytes = 1000.0 / 8 / 1_048_576.0;

        // the LHM storage hardware by payload name, asked each pass
        private readonly Func<IReadOnlyList<string>> _getStorageHardwareNames;

        // the last values, replaced as a whole; read by the polling loop
        private volatile IReadOnlyList<WinSensorReading> _readings = Array.Empty<WinSensorReading>();

        // cuts the wait short when a switch changes, so it applies right away
        private readonly SemaphoreSlim _settingsChangedSignal = new(0, 1);

        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        // the last DDC/CI levels per display key, and when they were read; loop thread only
        private Dictionary<string, Dictionary<byte, double>> _ddcLevels = new();
        private long _ddcTimestamp;

        // one disk as Windows lists it
        private record PhysicalDiskTemperature(string Number, string FriendlyName, double Celsius);


        // === constructor ===

        public WinSensorSource(Func<IReadOnlyList<string>> getStorageHardwareNames)
        {
            _getStorageHardwareNames = getStorageHardwareNames;
        }


        // === public api ===

        // the drive temperature of Windows instead of the LHM one
        private volatile bool _isStorageTemperatureEnabled;
        public bool IsStorageTemperatureEnabled
        {
            get => _isStorageTemperatureEnabled;
            set
            {
                if (_isStorageTemperatureEnabled == value) return;
                _isStorageTemperatureEnabled = value;
                SignalSettingsChanged();
            }
        }

        // signal strength, RSSI and link rates of a Wi-Fi adapter
        private volatile bool _isWlanEnabled;
        public bool IsWlanEnabled
        {
            get => _isWlanEnabled;
            set
            {
                if (_isWlanEnabled == value) return;
                _isWlanEnabled = value;
                SignalSettingsChanged();
            }
        }

        // the displays as hardware of their own, with brightness and refresh rate
        private volatile bool _isDisplayEnabled;
        public bool IsDisplayEnabled
        {
            get => _isDisplayEnabled;
            set
            {
                if (_isDisplayEnabled == value) return;
                _isDisplayEnabled = value;
                SignalSettingsChanged();
            }
        }

        // empty while everything is off
        public IReadOnlyList<WinSensorReading> Readings => _readings;

        public void Start()
        {
            if (_cts != null) return;

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (_cts == null) return;

            _cts.Cancel();

            // a WMI query in flight is given the same time the polling loop gets
            _loopTask?.Wait(2000);

            _readings = Array.Empty<WinSensorReading>();
            _cts = null;
            _loopTask = null;
        }


        // === private helpers ===

        private void SignalSettingsChanged()
        {
            if (_settingsChangedSignal.CurrentCount == 0)
            {
                _settingsChangedSignal.Release();
            }
        }

        private async Task LoopAsync(CancellationToken token)
        {
            var storageReadings = new List<WinSensorReading>();
            var wlanReadings = new List<WinSensorReading>();
            bool wasStorageEnabled = false;
            long storageTimestamp = 0;

            while (!token.IsCancellationRequested)
            {
                bool isStorageEnabled = _isStorageTemperatureEnabled;
                bool isWlanEnabled = _isWlanEnabled;
                bool isDisplayEnabled = _isDisplayEnabled;

                if (!isStorageEnabled)
                {
                    storageReadings = new List<WinSensorReading>();
                }
                else if (!wasStorageEnabled || Stopwatch.GetElapsedTime(storageTimestamp).TotalMilliseconds >= StorageIntervalMs)
                {
                    storageReadings = ReadStorageTemperatures();
                    storageTimestamp = Stopwatch.GetTimestamp();
                }
                wasStorageEnabled = isStorageEnabled;

                wlanReadings = isWlanEnabled ? ReadWlan() : new List<WinSensorReading>();

                var displayReadings = isDisplayEnabled ? ReadDisplays() : new List<WinSensorReading>();

                _readings = storageReadings.Concat(wlanReadings).Concat(displayReadings).ToList();

                try
                {
                    // nothing to read: sleeps until a switch turns on
                    int waitMs = isStorageEnabled || isWlanEnabled || isDisplayEnabled ? WlanIntervalMs : Timeout.Infinite;
                    await _settingsChangedSignal.WaitAsync(waitMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // the temperature of every disk Windows has a reliability counter for, on the LHM drive of the same name
        // MSFT_StorageReliabilityCounter is the Storport view of SMART; LHM reads 0 for some NVMe drives behind Intel VMD
        private List<WinSensorReading> ReadStorageTemperatures()
        {
            var result = new List<WinSensorReading>();

            var hardwareNames = _getStorageHardwareNames();
            if (hardwareNames.Count == 0) return result;

            var disks = new List<PhysicalDiskTemperature>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk");
                foreach (ManagementObject item in searcher.Get())
                {
                    string number = item["DeviceId"]?.ToString()?.Trim() ?? "";
                    string friendlyName = item["FriendlyName"]?.ToString()?.Trim() ?? "";
                    if (number.Length == 0 || friendlyName.Length == 0) continue;

                    try
                    {
                        // GetRelated() walks MSFT_PhysicalDiskToStorageReliabilityCounter without naming it
                        foreach (ManagementObject reliability in item.GetRelated("MSFT_StorageReliabilityCounter"))
                        {
                            // null when the driver reports none
                            if (reliability["Temperature"] is { } temperature)
                            {
                                disks.Add(new PhysicalDiskTemperature(number, friendlyName, Convert.ToDouble(temperature)));
                            }
                            break; // one per physical disk
                        }
                    }
                    catch
                    {
                        // no reliability counter for this disk (some drivers expose none)
                    }
                }
            }
            catch
            {
                // no Storage namespace, or WMI is not answering; the LHM temperature stays
                return result;
            }

            // the same best-effort match the info panel uses, so the panel and the sensor show one drive
            var usedNumbers = new HashSet<string>();
            foreach (string hardwareName in hardwareNames)
            {
                var disk = HardwareNameMatcher.FindBestMatch(hardwareName, disks, d => d.FriendlyName);
                if (disk == null || !usedNumbers.Add(disk.Number)) continue;

                result.Add(new WinSensorReading(
                    Id: $"{SensorSourceMarker.WindowsIdRoot}storage/{disk.Number}/temperature/0",
                    Name: "Temperature",
                    HardwareName: hardwareName,
                    HardwareType: "Storage",
                    SensorType: "Temperature",
                    Value: disk.Celsius,
                    ReplacedSensorNames: ReplacedStorageTemperatureNames));
            }

            return result;
        }

        // refresh rate of every active display, and its levels: the brightness of a laptop panel from Windows, the
        // brightness, contrast and speaker volume of a monitor over DDC/CI
        private List<WinSensorReading> ReadDisplays()
        {
            var result = new List<WinSensorReading>();

            try
            {
                var displays = WinDisplayReader.QueryDisplays(includeDetails: false);
                if (displays.Count == 0) return result;

                var brightness = ReadBrightness();
                bool isDdcDue = Stopwatch.GetElapsedTime(_ddcTimestamp).TotalMilliseconds >= DdcIntervalMs;
                var ddcLevels = isDdcDue ? new Dictionary<string, Dictionary<byte, double>>() : _ddcLevels;

                foreach (var display in displays)
                {
                    string idRoot = $"{SensorSourceMarker.WindowsIdRoot}display/{display.Key}";
                    bool hasBrightness = TryMatchBrightness(display, brightness, out double percent);

                    // a panel Windows dims itself is not asked over DDC/CI
                    if (!hasBrightness)
                    {
                        if (isDdcDue) ddcLevels[display.Key] = WinDisplayReader.QueryDdcLevels(displays, display);

                        if (ddcLevels.TryGetValue(display.Key, out var levels))
                        {
                            hasBrightness = levels.TryGetValue(WinDisplayReader.VcpBrightness, out percent);

                            if (levels.TryGetValue(WinDisplayReader.VcpContrast, out double contrast))
                            {
                                result.Add(new WinSensorReading(
                                    $"{idRoot}/level/1", "Contrast", display.Name, "Display", "Level", contrast, IsOwnHardware: true));
                            }
                            if (levels.TryGetValue(WinDisplayReader.VcpVolume, out double volume))
                            {
                                result.Add(new WinSensorReading(
                                    $"{idRoot}/level/2", "Volume", display.Name, "Display", "Level", volume, IsOwnHardware: true));
                            }
                        }
                    }

                    if (hasBrightness)
                    {
                        result.Add(new WinSensorReading(
                            $"{idRoot}/level/0", "Brightness", display.Name, "Display", "Level", percent, IsOwnHardware: true));
                    }

                    if (display.RefreshRateHz > 0)
                    {
                        result.Add(new WinSensorReading(
                            $"{idRoot}/frequency/0", "Refresh Rate", display.Name, "Display", "Frequency", display.RefreshRateHz,
                            IsOwnHardware: true));
                    }
                }

                if (isDdcDue)
                {
                    _ddcLevels = ddcLevels;
                    _ddcTimestamp = Stopwatch.GetTimestamp();
                }
            }
            catch
            {
                // the display config changed mid query; nothing this pass
            }

            return result;
        }

        // percent per monitor, by the upper case InstanceName of root\wmi; empty on a desktop, the class has no
        // instance there and the query throws
        private static Dictionary<string, double> ReadBrightness()
        {
            var result = new Dictionary<string, double>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\wmi", "SELECT InstanceName, CurrentBrightness, Active FROM WmiMonitorBrightness");
                foreach (ManagementObject item in searcher.Get())
                {
                    if (item["Active"] is false) continue;

                    string instanceName = item["InstanceName"]?.ToString()?.ToUpperInvariant() ?? "";
                    if (instanceName.Length > 0 && item["CurrentBrightness"] is { } value)
                    {
                        result[instanceName] = Convert.ToDouble(value);
                    }
                }
            }
            catch
            {
                // not supported: no panel whose backlight Windows controls
            }

            return result;
        }

        // by the device path; a single value without one belongs to the laptop panel
        private static bool TryMatchBrightness(WinDisplayInfo display, Dictionary<string, double> brightness, out double percent)
        {
            if (display.InstancePath.Length > 0)
            {
                foreach (var pair in brightness)
                {
                    if (!pair.Key.StartsWith(display.InstancePath, StringComparison.Ordinal)) continue;

                    percent = pair.Value;
                    return true;
                }
            }

            if (display.IsBuiltIn && brightness.Count == 1)
            {
                percent = brightness.Values.First();
                return true;
            }

            percent = 0;
            return false;
        }

        // signal and link rates of every connected Wi-Fi adapter, on the LHM adapter of the same interface
        private static List<WinSensorReading> ReadWlan()
        {
            var result = new List<WinSensorReading>();

            try
            {
                var connections = WinWlanReader.QueryConnections();
                if (connections.Count == 0) return result;

                var interfaces = NetworkInterface.GetAllNetworkInterfaces();

                foreach (var connection in connections)
                {
                    // LHM names an adapter by NetworkInterface.Name
                    var nic = interfaces.FirstOrDefault(n => Guid.TryParse(n.Id, out Guid id) && id == connection.InterfaceId);
                    if (nic == null) continue;

                    string idRoot = $"{SensorSourceMarker.WindowsIdRoot}nic/{connection.InterfaceId:D}";

                    result.Add(new WinSensorReading(
                        $"{idRoot}/level/0", "Signal Strength", nic.Name, "Network", "Level", connection.SignalQualityPercent));

                    // only while the access point is in the last scan
                    if (connection.RssiDbm is int rssi)
                    {
                        result.Add(new WinSensorReading(
                            $"{idRoot}/signal/0", "RSSI", nic.Name, "Network", "Signal", rssi));
                    }

                    result.Add(new WinSensorReading(
                        $"{idRoot}/throughput/0", "Receive Link Rate", nic.Name, "Network", "Throughput",
                        connection.ReceiveRateKbps * KilobitsToMebibytes));
                    result.Add(new WinSensorReading(
                        $"{idRoot}/throughput/1", "Transmit Link Rate", nic.Name, "Network", "Throughput",
                        connection.TransmitRateKbps * KilobitsToMebibytes));
                }
            }
            catch
            {
                // the wlan service stopped or an adapter left mid query; nothing this pass
            }

            return result;
        }
    }
}
