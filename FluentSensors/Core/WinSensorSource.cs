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
        string HardwareName, // the LHM hardware it joins, by its payload name
        string HardwareType,
        string SensorType,
        double Value,
        string[]? ReplacedSensorNames = null // the LHM sensors of that hardware and type it stands in for
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
        private const int WlanIntervalMs = 2000;
        private const double StorageIntervalMs = 6000;

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

                _readings = storageReadings.Concat(wlanReadings).ToList();

                try
                {
                    // nothing to read: sleeps until a switch turns on
                    int waitMs = isStorageEnabled || isWlanEnabled ? WlanIntervalMs : Timeout.Infinite;
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
