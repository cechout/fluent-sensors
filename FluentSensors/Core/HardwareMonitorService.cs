using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;

namespace FluentSensors.Core
{
    // one sensor reading
    public record SensorData(
        string Id, // e.g. "/intelcpu/0/load/1"
        string Name, // e.g. "CPU Package"
        string HardwareName, // e.g. "Intel Core i9-12900H"
        string HardwareType, // e.g. "Cpu", "GpuNvidia", "Memory"
        string SensorType, // e.g. "Power", "Temperature", "Load"
        double Value
    );


    // the hardware monitor:
    // owns the one polling loop every sensor value comes from, plus the LHM discovery behind it; the timing is the
    // non-obvious part, see LoopAsync
    public class HardwareMonitorService
    {
        // === fields ===

        private readonly Computer _computer;

        // every discovered sensor, read each tick
        private readonly List<ISensor> _activeSensors = new();

        private readonly object _sensorLock = new object();
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private readonly HashSet<string> _excludedSensorIds = new();

        // read durations the schedule plans against, and the slack on top (for run-to-run noise)
        private const int ReadDurationSampleCount = 8;
        private const double ReadScheduleMarginMs = 5;

        // max age of the network adapter snapshot, events or not
        private const double NetworkAdapterSnapshotMaxAgeMs = 10000;

        // ring buffer of the last read durations, polling loop only; (the schedule takes the
        // slowest, see PredictReadDurationMs)
        private readonly double[] _readDurationSamples = new double[ReadDurationSampleCount];
        private int _readDurationSampleIndex;

        // cuts a pending wait short when the rate changes, so a new rate applies right away
        private readonly SemaphoreSlim _intervalChangedSignal = new(0, 1);

        // set by the NetworkChange handler; the rebuild runs on the polling loop, see RefreshNetworkAdapters
        private volatile bool _networkAdaptersDirty = true;

        // currently "up" network adapters, see RefreshNetworkAdapters; touched only by the polling loop
        private HashSet<string> _activeNetworkAdapters = new();
        private long _networkAdapterRefreshTimestamp;


        // === singleton instance ===

        private static readonly HardwareMonitorService _instance = new HardwareMonitorService();
        public static HardwareMonitorService Instance => _instance;


        // === constructor ===

        private HardwareMonitorService()
        {
            _computer = new Computer
            {
                // everything off here; the slow enabling runs step by step in the Init...Async methods
                IsCpuEnabled = false,
                IsGpuEnabled = false,
                IsMemoryEnabled = false,
                IsStorageEnabled = false,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,

            };

            _computer.Open();
        }


        // === public api ===

        private int _updateIntervalMs = 500;
        public int UpdateIntervalMs
        {
            get => _updateIntervalMs;
            set
            {
                if (_updateIntervalMs != value)
                {
                    _updateIntervalMs = value;

                    // cuts the current wait short; (a signal nobody waits on only makes the next wait re-check once)
                    if (_intervalChangedSignal.CurrentCount == 0)
                    {
                        _intervalChangedSignal.Release();
                    }

                    UpdateIntervalChanged?.Invoke(_updateIntervalMs);
                }
            }
        }

        // measured cadence between two broadcasts; sits on UpdateIntervalMs while LHM keeps up, rises
        // once a read alone outruns it
        // (read cross-thread by AppStatusService; Interlocked, since a double is not atomic)
        private double _actualUpdateIntervalMs;
        public double ActualUpdateIntervalMs
        {
            get => Interlocked.CompareExchange(ref _actualUpdateIntervalMs, 0, 0);
            private set => Interlocked.Exchange(ref _actualUpdateIntervalMs, value);
        }

        // the last full read (hardware.Update() plus the payload); says whether a rate is reachable at all, which is
        // why the status bar shows it
        // (cross-thread, Interlocked like above)
        private double _lastReadDurationMs;
        public double LastReadDurationMs
        {
            get => Interlocked.CompareExchange(ref _lastReadDurationMs, 0, 0);
            private set => Interlocked.Exchange(ref _lastReadDurationMs, value);
        }

        // init pipeline; enabling a component blocks for a long time, so each step runs on a background thread
        public Task InitMotherboardAsync()
        {
            return Task.Run(() => { _computer.IsMotherboardEnabled = true; });
        }

        public Task InitCpuAsync()
        {
            return Task.Run(() => { _computer.IsCpuEnabled = true; });
        }

        public Task InitGpuAsync()
        {
            return Task.Run(() => { _computer.IsGpuEnabled = true; });
        }

        public Task InitMemoryAndStorageAsync()
        {
            return Task.Run(() =>
            {
                _computer.IsMemoryEnabled = true;
                _computer.IsStorageEnabled = true;
            });
        }

        public Task InitControllerAsync()
        {
            return Task.Run(() => { _computer.IsControllerEnabled = true; });
        }

        public Task InitNetworkAsync()
        {
            return Task.Run(() => { _computer.IsNetworkEnabled = true; });
        }

        // starts the polling loop, once the init pipeline has completed
        public void StartMonitoring()
        {
            // already running
            if (_cts != null) return;

            InitAllSensors();

            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            _cts = new CancellationTokenSource();

            // kept, so StopMonitoring can wait for the loop to finish
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void StopMonitoring()
        {
            if (_cts == null) return;

            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

            _cts.Cancel();

            // waits up to 2s for the loop to exit, in-flight read included, so
            // HardwareDataUpdated does not fire after this
            _loopTask?.Wait(2000);

            _cts = null;
            _loopTask = null;
        }

        public void Cleanup()
        {
            StopMonitoring();
            _computer.Close();
        }

        // exclusion api; the service does not care what excluded means (hidden, disabled), it skips these ids (the skip
        // is currently off, see LoopAsync)
        public void AddExcludedSensor(string sensorId)
        {
            lock (_sensorLock)
            {
                _excludedSensorIds.Add(sensorId);
            }
        }

        public void RemoveExcludedSensor(string sensorId)
        {
            lock (_sensorLock)
            {
                _excludedSensorIds.Remove(sensorId);
            }
        }

        // bulk sync for startup, replaces the current exclusion set in one shot
        public void SetExcludedSensors(IEnumerable<string> sensorIds)
        {
            lock (_sensorLock)
            {
                _excludedSensorIds.Clear();
                foreach (var id in sensorIds)
                {
                    _excludedSensorIds.Add(id);
                }
            }
        }


        // === events ===

        // one event per tick with every sensor reading
        public event Action<List<SensorData>>? HardwareDataUpdated;

        // the polling interval changed; graphs keep their time span with it (point count = span / interval)
        public event Action<int>? UpdateIntervalChanged;


        // === private helpers ===

        // polling loop; hand-written so the broadcast cadence stays off the read duration (every graph shifts one point
        // per broadcast, so uneven spacing shows as uneven scroll speed, and an early tick looks worse than a late one)
        // the configured rate is target and floor at once; one tick at 250ms with a ~100ms read:
        //
        // |.........idle.........|--read--|FIRE
        // 0                     145      250 <- deadline = previous FIRE + interval
        //
        // the deadline counts from the previous broadcast, not from a fixed grid, so an overrun shifts the phase
        // instead of being paid back by an early tick
        // the read is planned to end just before the deadline (slowest recent read plus a margin), so values
        // are a few ms old at every rate
        // waits only ever overshoot (see WaitSinceAsync), so the cadence is the interval plus
        // a few ms and never below it
        private async Task LoopAsync(CancellationToken token)
        {
            RefreshNetworkAdapters();

            // backdated one interval, so the first tick broadcasts right away
            long lastBroadcastTimestamp = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * (UpdateIntervalMs / 1000.0));

            while (!token.IsCancellationRequested)
            {
                // per tick; the settings page can change it any time
                int intervalMs = UpdateIntervalMs;

                // hold the read back so it lands on the deadline instead of running right after the last broadcast
                double readStartMs = Math.Max(0, intervalMs - PredictReadDurationMs() - ReadScheduleMarginMs);
                var outcome = await WaitSinceAsync(lastBroadcastTimestamp, readStartMs, token);

                if (outcome == WaitOutcome.Cancelled) break;

                // a rate change while idle; recompute the schedule
                if (outcome == WaitOutcome.RateChanged) continue;

                long readStartTimestamp = Stopwatch.GetTimestamp();

                // LHM reads the hardware
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                }


                //// TEMP
                //System.Diagnostics.Debug.WriteLine("=== Sensor Dump ===");
                //foreach (var hardware in _computer.Hardware)
                //{
                //    foreach (var sensor in hardware.Sensors)
                //    {
                //        System.Diagnostics.Debug.WriteLine(
                //            $"[{hardware.HardwareType}] {hardware.Name} | {sensor.Name} | Type={sensor.SensorType} | Value={sensor.Value}");
                //    }
                //    foreach (var sub in hardware.SubHardware)
                //    {
                //        sub.Update();
                //        foreach (var sensor in sub.Sensors)
                //        {
                //            System.Diagnostics.Debug.WriteLine(
                //                $"[{sub.HardwareType}] {sub.Name} (sub of {hardware.Name}) | {sensor.Name} | Type={sensor.SensorType} | Value={sensor.Value}");
                //        }
                //    }
                //}

                //// TEMP
                //System.Diagnostics.Debug.WriteLine("=== Storage Dump ===");
                //foreach (var hardware in _computer.Hardware)
                //{
                //    if (hardware.HardwareType != HardwareType.Storage) continue;

                //    System.Diagnostics.Debug.WriteLine($"--- Hardware: '{hardware.Name}' ---");
                //    foreach (var sensor in hardware.Sensors)
                //    {
                //        System.Diagnostics.Debug.WriteLine($"  Sensor: '{sensor.Name}' | Type={sensor.SensorType} | Value={sensor.Value}");
                //    }
                //    foreach (var sub in hardware.SubHardware)
                //    {
                //        sub.Update();
                //        System.Diagnostics.Debug.WriteLine($"  --- SubHardware: '{sub.Name}' ---");
                //        foreach (var sensor in sub.Sensors)
                //        {
                //            System.Diagnostics.Debug.WriteLine($"    Sensor: '{sensor.Name}' | Type={sensor.SensorType} | Value={sensor.Value}");
                //        }
                //    }
                //}


                // the "up" network adapter snapshot this tick filters against, see RefreshNetworkAdapters
                var activeNetworkAdapters = _activeNetworkAdapters;

                // a fresh payload per tick
                var payload = new List<SensorData>();

                lock (_sensorLock)
                {
                    foreach (var sensor in _activeSensors)
                    {
                        string id = sensor.Identifier.ToString();

                        // excluded sensors would be skipped here (no payload entry, no UI or graph update)
                        // TEMP: disabled, so PerformancePage sees every sensor
                        //if (_excludedSensorIds.Contains(id)) continue;

                        // adapters that are not up; (Windows adds filter pseudo adapters next to every real
                        // one, QoS, WFP, Wi-Fi Direct)
                        if (sensor.Hardware.HardwareType == HardwareType.Network &&
                            !activeNetworkAdapters.Contains(sensor.Hardware.Name))
                        {
                            continue;
                        }

                        if (sensor.Value.HasValue) // no value right now
                        {
                            double value = sensor.Value.Value;

                            // some sensors report NaN or Infinity instead of no value
                            if (double.IsNaN(value) || double.IsInfinity(value))
                            {
                                continue;
                            }

                            // LHM reports throughput in bytes/s; MB/s for every consumer
                            if (sensor.SensorType == SensorType.Throughput)
                            {
                                value /= 1_048_576.0;
                            }

                            // some NVMe names are padded with control characters, which
                            // IsNullOrWhiteSpace does not catch
                            string cleanedName = new string(sensor.Hardware.Name.Where(c => !char.IsControl(c)).ToArray()).Trim();

                            // hardware type and LHM identifier, so a group name is never blank
                            string hardwareName = string.IsNullOrWhiteSpace(cleanedName)
                                ? $"{sensor.Hardware.HardwareType} ({sensor.Hardware.Identifier})"
                                : cleanedName;

                            payload.Add(new SensorData(
                                Id: id,
                                Name: sensor.Name,
                                HardwareName: hardwareName,
                                HardwareType: sensor.Hardware.HardwareType.ToString(),
                                SensorType: sensor.SensorType.ToString(),
                                Value: value
                            ));
                        }
                    }
                }

                RecordReadDuration(Stopwatch.GetElapsedTime(readStartTimestamp).TotalMilliseconds);

                // a shutdown requested during the read
                if (token.IsCancellationRequested) break;

                // holds the payload until the deadline, re-evaluated on a rate change; (a read that outran
                // the interval is simply late)
                do
                {
                    outcome = await WaitSinceAsync(lastBroadcastTimestamp, UpdateIntervalMs, token);
                }
                while (outcome == WaitOutcome.RateChanged);

                if (outcome == WaitOutcome.Cancelled) break;

                // the cadence consumers actually see
                long broadcastTimestamp = Stopwatch.GetTimestamp();
                ActualUpdateIntervalMs = Stopwatch.GetElapsedTime(lastBroadcastTimestamp, broadcastTimestamp).TotalMilliseconds;
                lastBroadcastTimestamp = broadcastTimestamp;

                HardwareDataUpdated?.Invoke(payload);

                // the idle gap after a broadcast, where a rebuild costs nothing
                RefreshNetworkAdapters();
            }
        }

        // RateChanged: cut short by a rate change, the target has to be recomputed
        private enum WaitOutcome
        {
            Reached,
            RateChanged,
            Cancelled
        }

        // waits until targetMs since anchorTimestamp, early only for a rate change or a cancel; re-checks the anchor in
        // a loop, so a wait that comes back short never becomes an early broadcast
        private async Task<WaitOutcome> WaitSinceAsync(long anchorTimestamp, double targetMs, CancellationToken token)
        {
            while (true)
            {
                double remainingMs = targetMs - Stopwatch.GetElapsedTime(anchorTimestamp).TotalMilliseconds;
                if (remainingMs <= 0) return WaitOutcome.Reached;

                // rounded up; a fractional remainder would truncate to a zero timeout and spin
                var remaining = TimeSpan.FromMilliseconds(Math.Ceiling(remainingMs));

                try
                {
                    if (await _intervalChangedSignal.WaitAsync(remaining, token))
                    {
                        return WaitOutcome.RateChanged;
                    }
                }
                catch (OperationCanceledException)
                {
                    // StopMonitoring; the loop exits cleanly instead of ending Canceled
                    return WaitOutcome.Cancelled;
                }
            }
        }

        // the slowest recent read; (an average leaves every spike late, a permanent maximum never recovers from one)
        private double PredictReadDurationMs()
        {
            double slowestMs = 0;

            foreach (double sampleMs in _readDurationSamples)
            {
                if (sampleMs > slowestMs) slowestMs = sampleMs;
            }

            return slowestMs;
        }

        private void RecordReadDuration(double durationMs)
        {
            LastReadDurationMs = durationMs;

            _readDurationSamples[_readDurationSampleIndex] = durationMs;
            _readDurationSampleIndex = (_readDurationSampleIndex + 1) % ReadDurationSampleCount;
        }

        // only flags the snapshot; the rebuild runs on the polling loop
        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            _networkAdaptersDirty = true;
        }

        // rebuilds the snapshot of up adapters when an address change flagged it or it got old (for transitions
        // NetworkAddressChanged misses)
        // runs in the idle window; GetAllNetworkInterfaces plus GetIPProperties is a
        // multi-millisecond call whose cost spikes
        private void RefreshNetworkAdapters()
        {
            bool isStale = Stopwatch.GetElapsedTime(_networkAdapterRefreshTimestamp).TotalMilliseconds >= NetworkAdapterSnapshotMaxAgeMs;
            if (!_networkAdaptersDirty && !isStale) return;

            _networkAdaptersDirty = false;
            _networkAdapterRefreshTimestamp = Stopwatch.GetTimestamp();

            // keyed by NetworkInterface.Name, which matches the LHM Hardware.Name
            // Windows reports filter layers (QoS Packet Scheduler, WFP, WiFi filter drivers) and WAN Miniport stubs as
            // up; only a real adapter has an IP address
            _activeNetworkAdapters = new HashSet<string>(
                NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                        nic.GetIPProperties().UnicastAddresses.Count > 0)
                    .Select(nic => nic.Name));
        }

        // flattens the hardware tree into _activeSensors, under _sensorLock against the polling loop
        private void InitAllSensors()
        {
            lock (_sensorLock)
            {
                _activeSensors.Clear();

                foreach (var hardware in _computer.Hardware)
                {
                    DiscoverSensors(hardware);
                }
            }
        }

        private void DiscoverSensors(IHardware hardware)
        {
            foreach (var sensor in hardware.Sensors)
            {
                _activeSensors.Add(sensor);
            }

            // sub-hardware, e.g. the super I/O chips under a motherboard
            foreach (var subHardware in hardware.SubHardware)
            {
                DiscoverSensors(subHardware);
            }
        }
    }
}
