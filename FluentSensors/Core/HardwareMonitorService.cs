using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;

using FluentSensors.Core.Lhm;

namespace FluentSensors.Core
{
    // one sensor reading
    public record SensorData(
        string Id, // e.g. "/intelcpu/0/load/1"
        string Name, // e.g. "CPU Package"
        string HardwareName, // e.g. "Intel Core i9-12900H"
        string HardwareType, // e.g. "Cpu", "GpuNvidia", "Memory"
        string SensorType, // e.g. "Power", "Temperature", "Load"
        double Value,
        bool IsIntegratedGpu = false // LHM has no hardware type for it, see LhmGpuRole
    );

    // one piece of LHM hardware that has sensors, read or not; what the engine settings list
    public record LhmHardwareEntry(
        string Name, // the payload name
        string HardwareType,
        bool IsIntegratedGpu
    );


    // the hardware monitor:
    // owns the one polling loop every sensor value comes from, plus the LHM discovery behind it; the timing is the
    // non-obvious part, see LoopAsync
    // LHM is read inside the loop; the Windows values come from WinSensorSource, which reads on its own thread and is
    // only merged here
    public class HardwareMonitorService
    {
        // === fields ===

        private readonly Computer _computer;

        // every sensor LHM has active right now, read each tick; follows SensorAdded and SensorRemoved, since some
        // hardware activates its sensors in the first Update() or drops one at runtime (a battery on mains has no rate)
        private readonly List<ISensor> _activeSensors = new();

        // hardware and sub-hardware the sensor events are subscribed on; under _sensorLock
        private readonly HashSet<IHardware> _watchedHardware = new();

        // the gpus that are integrated; resolved once per hardware, under _sensorLock
        private readonly HashSet<IHardware> _integratedGpus = new();

        private readonly object _sensorLock = new object();
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private readonly HashSet<string> _excludedSensorIds = new();

        // read durations the schedule plans against, and the slack on top (for run-to-run noise)
        private const int ReadDurationSampleCount = 8;
        private const double ReadScheduleMarginMs = 5;

        // appended to the id of a sensor whose identifier an earlier one already has, see LoopAsync
        private const string DuplicateIdSuffix = "#2";

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

        // currently "up" network adapters, see RefreshNetworkAdapters; replaced as a whole by the polling loop
        private HashSet<string> _activeNetworkAdapters = new();
        private long _networkAdapterRefreshTimestamp;

        // hardware that left since the last tick (unplugged, or a network adapter that is no longer up), by the name
        // the payload gives it; under _sensorLock, broadcast right after the first payload without it
        private readonly List<string> _removedHardwareNames = new();

        // sensors LHM deactivated since the last tick, by id; under _sensorLock, broadcast like _removedHardwareNames
        private readonly List<string> _removedSensorIds = new();

        // deactivated sensors still inside the grace time, with the moment LHM dropped them; under _sensorLock
        // (an Intel core VID turns off and on again with every read that comes back empty)
        private readonly Dictionary<ISensor, long> _pendingSensorRemovals = new();
        private const double SensorRemovalGraceMs = 5000;

        // hardware the engine settings switched off, by payload name: never updated and left out of the payload
        // replaced as a whole under _sensorLock, so the loop reads it without one
        private volatile HashSet<string> _disabledHardwareNames = new();

        // the second source, see WinSensorSource
        private readonly WinSensorSource _windowsSource;

        // the Windows sensors and the LHM sensors they replaced in the last payload, by id; polling loop only
        private HashSet<string> _lastWindowsSensorIds = new();
        private HashSet<string> _lastReplacedSensorIds = new();

        // the hardware only Windows has (a display) in the last payload, by name; polling loop only
        private HashSet<string> _lastWindowsHardwareNames = new();


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
                IsBatteryEnabled = false,
                IsPsuEnabled = false,
                IsPowerMonitorEnabled = false,

            };

            _computer.Open();

            _windowsSource = new WinSensorSource(GetStorageHardwareNames);
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

        // the Windows values the engine settings switch on
        public WinSensorSource WindowsSource => _windowsSource;

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

        // the battery, usb power supplies (Corsair, MSI) and the WireView power monitor
        public Task InitPowerAsync()
        {
            return Task.Run(() =>
            {
                _computer.IsBatteryEnabled = true;
                _computer.IsPsuEnabled = true;
                _computer.IsPowerMonitorEnabled = true;
            });
        }

        // starts the polling loop, once the init pipeline has completed
        public void StartMonitoring()
        {
            // already running
            if (_cts != null) return;

            // before the first flatten, which then covers whatever was added in between
            _computer.HardwareAdded += OnHardwareAdded;
            _computer.HardwareRemoved += OnHardwareRemoved;

            InitAllSensors();

            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            _cts = new CancellationTokenSource();

            // kept, so StopMonitoring can wait for the loop to finish
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));

            _windowsSource.Start();
        }

        // stops the polling loop and the Windows source and detaches every LHM event; returns once the loop has
        // ended, so no payload follows
        public void StopMonitoring()
        {
            if (_cts == null) return;

            _windowsSource.Stop();

            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            _computer.HardwareAdded -= OnHardwareAdded;
            _computer.HardwareRemoved -= OnHardwareRemoved;

            _cts.Cancel();

            // waits up to 2s for the loop to exit, in-flight read included, so
            // HardwareDataUpdated does not fire after this
            _loopTask?.Wait(2000);

            lock (_sensorLock)
            {
                foreach (var hardware in _watchedHardware)
                {
                    hardware.SensorAdded -= OnSensorAdded;
                    hardware.SensorRemoved -= OnSensorRemoved;
                }
                _watchedHardware.Clear();
                _integratedGpus.Clear();
                _pendingSensorRemovals.Clear();
            }

            _cts = null;
            _loopTask = null;
        }

        public void Cleanup()
        {
            StopMonitoring();
            _computer.Close();
        }

        // hardware api; the engine settings switch whole hardware off, by the name the payload gives it
        // every piece of LHM hardware that has sensors, the switched off ones included; network adapters only while up
        public List<LhmHardwareEntry> GetHardwareEntries()
        {
            var result = new List<LhmHardwareEntry>();
            var activeNetworkAdapters = _activeNetworkAdapters;

            foreach (var hardware in _computer.Hardware)
            {
                CollectHardwareEntries(hardware, activeNetworkAdapters, result);
            }

            return result;
        }

        // the startup set, before the loop runs
        public void SetDisabledHardware(IEnumerable<string> hardwareNames)
        {
            lock (_sensorLock)
            {
                _disabledHardwareNames = new HashSet<string>(hardwareNames);
            }
        }

        // applies with the next tick; switched off hardware leaves every page like an unplugged one, switched on it
        // comes back with the next payload
        public void SetHardwareEnabled(string hardwareName, bool isEnabled)
        {
            lock (_sensorLock)
            {
                var names = new HashSet<string>(_disabledHardwareNames);
                bool changed = isEnabled ? names.Remove(hardwareName) : names.Add(hardwareName);
                if (!changed) return;

                _disabledHardwareNames = names;
                if (!isEnabled) _removedHardwareNames.Add(hardwareName);
            }
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

        // hardware that is gone, by its payload name; after the first payload without it, on the polling thread
        public event Action<List<string>>? HardwareRemoved;

        // sensors LHM deactivated, by id; after the first payload without them, on the polling thread
        public event Action<List<string>>? SensorsRemoved;

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

                // LHM reads the hardware; what the engine settings switched off is not read at all
                var disabledHardware = _disabledHardwareNames;
                foreach (var hardware in _computer.Hardware)
                {
                    if (disabledHardware.Count > 0 && IsDisabled(hardware, disabledHardware)) continue;

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
                List<string>? removedHardwareNames = null;
                List<string>? removedSensorIds = null;
                var payloadIds = new HashSet<string>();

                lock (_sensorLock)
                {
                    CommitSensorRemovals();

                    // read again under the lock SetHardwareEnabled writes it in, so a name it queued as removed is
                    // never still in this payload
                    disabledHardware = _disabledHardwareNames;

                    foreach (var sensor in _activeSensors)
                    {
                        string id = sensor.Identifier.ToString();

                        // --- workaround: two sensors with one identifier ---
                        // problem: LHM 0.9.6 gives the NVIDIA "GPU Memory" load the index 3 of the "GPU Bus" load, so
                        // both report /gpu-nvidia/0/load/3 and the later one overwrites the first for every consumer
                        // keyed by id; reproduced on an RTX 3080 Ti, and upstream counts the load indices after 0.9.6
                        // fix: the later sensor gets a suffix; decided before any skip, so it holds from tick to tick
                        if (!payloadIds.Add(id))
                        {
                            id += DuplicateIdSuffix;
                            payloadIds.Add(id);
                        }

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

                        string hardwareName = GetHardwareName(sensor.Hardware);
                        if (disabledHardware.Contains(hardwareName)) continue;

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

                            payload.Add(new SensorData(
                                Id: id,
                                Name: sensor.Name,
                                HardwareName: hardwareName,
                                HardwareType: sensor.Hardware.HardwareType.ToString(),
                                SensorType: sensor.SensorType.ToString(),
                                Value: value,
                                IsIntegratedGpu: _integratedGpus.Contains(sensor.Hardware)
                            ));
                        }
                    }

                    MergeWindowsSensors(payload);

                    // taken with the payload, so the two never disagree; a name that is back in it already (an
                    // adapter that only dropped for a moment) stays
                    if (_removedHardwareNames.Count > 0)
                    {
                        var present = new HashSet<string>(payload.Select(data => data.HardwareName));
                        removedHardwareNames = _removedHardwareNames.Where(name => !present.Contains(name)).Distinct().ToList();
                        _removedHardwareNames.Clear();
                    }

                    // a sensor that is in the payload again already stays
                    if (_removedSensorIds.Count > 0)
                    {
                        var present = new HashSet<string>(payload.Select(data => data.Id));
                        removedSensorIds = _removedSensorIds.Where(id => !present.Contains(id)).Distinct().ToList();
                        _removedSensorIds.Clear();
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

                if (removedSensorIds is { Count: > 0 }) SensorsRemoved?.Invoke(removedSensorIds);

                if (removedHardwareNames is { Count: > 0 }) HardwareRemoved?.Invoke(removedHardwareNames);

                // the idle gap after a broadcast, where a rebuild costs nothing
                RefreshNetworkAdapters();
            }
        }

        // adds the Windows readings whose hardware is in the payload and takes out the LHM sensors they replace; under
        // _sensorLock
        // an id that left since the last tick (a switch turned off, Wi-Fi disconnected, an LHM sensor newly replaced)
        // is queued in _removedSensorIds like a sensor LHM deactivated
        // hardware only Windows has leaves through _removedHardwareNames once none of its readings is left
        private void MergeWindowsSensors(List<SensorData> payload)
        {
            var readings = _windowsSource.Readings;
            if (readings.Count == 0 && _lastWindowsSensorIds.Count == 0 && _lastReplacedSensorIds.Count == 0) return;

            var windowsSensorIds = new HashSet<string>();
            var windowsHardwareNames = new HashSet<string>();
            var replacedSensorIds = new HashSet<string>();

            if (readings.Count > 0)
            {
                var hardwareNames = new HashSet<string>(payload.Select(data => data.HardwareName));

                foreach (var reading in readings)
                {
                    // its hardware is down, unplugged or switched off
                    if (reading.IsOwnHardware) windowsHardwareNames.Add(reading.HardwareName);
                    else if (!hardwareNames.Contains(reading.HardwareName)) continue;

                    if (reading.ReplacedSensorNames != null)
                    {
                        for (int i = payload.Count - 1; i >= 0; i--)
                        {
                            var data = payload[i];
                            if (data.HardwareName != reading.HardwareName || data.SensorType != reading.SensorType) continue;
                            if (Array.IndexOf(reading.ReplacedSensorNames, data.Name) < 0) continue;

                            replacedSensorIds.Add(data.Id);
                            payload.RemoveAt(i);
                        }
                    }

                    windowsSensorIds.Add(reading.Id);
                    payload.Add(new SensorData(
                        Id: reading.Id,
                        Name: reading.Name,
                        HardwareName: reading.HardwareName,
                        HardwareType: reading.HardwareType,
                        SensorType: reading.SensorType,
                        Value: reading.Value
                    ));
                }
            }

            foreach (string id in _lastWindowsSensorIds)
            {
                if (!windowsSensorIds.Contains(id)) _removedSensorIds.Add(id);
            }
            foreach (string id in replacedSensorIds)
            {
                if (!_lastReplacedSensorIds.Contains(id)) _removedSensorIds.Add(id);
            }

            foreach (string name in _lastWindowsHardwareNames)
            {
                if (!windowsHardwareNames.Contains(name)) _removedHardwareNames.Add(name);
            }

            _lastWindowsSensorIds = windowsSensorIds;
            _lastWindowsHardwareNames = windowsHardwareNames;
            _lastReplacedSensorIds = replacedSensorIds;
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
            var upAdapters = new HashSet<string>(
                NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                        nic.GetIPProperties().UnicastAddresses.Count > 0)
                    .Select(nic => nic.Name));

            // an adapter that went down (Wi-Fi off, cable out) leaves the pages like an unplugged one; LHM keeps it
            var wentDown = _activeNetworkAdapters.Where(name => !upAdapters.Contains(name)).ToList();
            if (wentDown.Count > 0)
            {
                lock (_sensorLock)
                {
                    _removedHardwareNames.AddRange(wentDown);
                }
            }

            _activeNetworkAdapters = upAdapters;
        }

        // a drive or a network adapter that was plugged in after the scan; LHM watches both itself and raises this on
        // its own thread, the sensors join the next tick
        private void OnHardwareAdded(IHardware hardware)
        {
            lock (_sensorLock)
            {
                WatchHardware(hardware);

                var sensors = new List<ISensor>();
                DiscoverSensors(hardware, sensors);

                foreach (var sensor in sensors)
                {
                    if (!_activeSensors.Contains(sensor)) _activeSensors.Add(sensor);
                }
            }
        }

        // unplugged; LHM closed the hardware, its sensors would only repeat their last value
        private void OnHardwareRemoved(IHardware hardware)
        {
            var sensors = new List<ISensor>();
            DiscoverSensors(hardware, sensors);

            var names = new List<string>();
            CollectHardwareNames(hardware, names);

            lock (_sensorLock)
            {
                UnwatchHardware(hardware);
                _activeSensors.RemoveAll(sensors.Contains);
                foreach (var sensor in sensors) _pendingSensorRemovals.Remove(sensor);
                _removedHardwareNames.AddRange(names);
            }
        }

        // a sensor LHM activated after the flatten; raised inside hardware.Update() on the polling thread (before the
        // payload is built), or on the LHM thread that adds hardware
        private void OnSensorAdded(ISensor sensor)
        {
            lock (_sensorLock)
            {
                _pendingSensorRemovals.Remove(sensor);
                if (!_activeSensors.Contains(sensor)) _activeSensors.Add(sensor);
            }
        }

        // only noted; the sensor stays until it has been off for the grace time, see CommitSensorRemovals
        private void OnSensorRemoved(ISensor sensor)
        {
            lock (_sensorLock)
            {
                if (_activeSensors.Contains(sensor)) _pendingSensorRemovals[sensor] = Stopwatch.GetTimestamp();
            }
        }

        // drops the sensors that stayed deactivated for the grace time; under _sensorLock
        private void CommitSensorRemovals()
        {
            if (_pendingSensorRemovals.Count == 0) return;

            List<ISensor>? expired = null;
            foreach (var pending in _pendingSensorRemovals)
            {
                if (Stopwatch.GetElapsedTime(pending.Value).TotalMilliseconds < SensorRemovalGraceMs) continue;
                (expired ??= new List<ISensor>()).Add(pending.Key);
            }
            if (expired == null) return;

            foreach (var sensor in expired)
            {
                _pendingSensorRemovals.Remove(sensor);
                if (_activeSensors.Remove(sensor)) _removedSensorIds.Add(sensor.Identifier.ToString());
            }
        }

        // subscribes the sensor events of a piece of hardware and everything under it; under _sensorLock
        private void WatchHardware(IHardware hardware)
        {
            if (_watchedHardware.Add(hardware))
            {
                hardware.SensorAdded += OnSensorAdded;
                hardware.SensorRemoved += OnSensorRemoved;

                if (LhmGpuRole.IsIntegrated(hardware)) _integratedGpus.Add(hardware);
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                WatchHardware(subHardware);
            }
        }

        private void UnwatchHardware(IHardware hardware)
        {
            if (_watchedHardware.Remove(hardware))
            {
                hardware.SensorAdded -= OnSensorAdded;
                hardware.SensorRemoved -= OnSensorRemoved;
                _integratedGpus.Remove(hardware);
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                UnwatchHardware(subHardware);
            }
        }

        // switched off with everything under it; a parent is still updated while one of its children is on
        private static bool IsDisabled(IHardware hardware, HashSet<string> disabledHardware)
        {
            if (!disabledHardware.Contains(GetHardwareName(hardware))) return false;

            foreach (var subHardware in hardware.SubHardware)
            {
                if (!IsDisabled(subHardware, disabledHardware)) return false;
            }

            return true;
        }

        private static void CollectHardwareEntries(IHardware hardware, HashSet<string> activeNetworkAdapters, List<LhmHardwareEntry> target)
        {
            // hardware without sensors of its own never shows as a group (a motherboard is only the parent of its chips)
            bool isListed = hardware.Sensors.Length > 0 &&
                (hardware.HardwareType != HardwareType.Network || activeNetworkAdapters.Contains(hardware.Name));

            string name = GetHardwareName(hardware);
            if (isListed && !target.Any(entry => entry.Name == name))
            {
                target.Add(new LhmHardwareEntry(name, hardware.HardwareType.ToString(), LhmGpuRole.IsIntegrated(hardware)));
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                CollectHardwareEntries(subHardware, activeNetworkAdapters, target);
            }
        }

        // the LHM drives by payload name, for the Windows source; on its thread
        private IReadOnlyList<string> GetStorageHardwareNames()
        {
            return _computer.Hardware
                .Where(hardware => hardware.HardwareType == HardwareType.Storage)
                .Select(GetHardwareName)
                .ToList();
        }

        private static void CollectHardwareNames(IHardware hardware, List<string> target)
        {
            target.Add(GetHardwareName(hardware));

            foreach (var subHardware in hardware.SubHardware)
            {
                CollectHardwareNames(subHardware, target);
            }
        }

        // the name a piece of hardware goes by in the payload
        private static string GetHardwareName(IHardware hardware)
        {
            // some NVMe names are padded with control characters, which IsNullOrWhiteSpace does not catch
            string cleanedName = new string(hardware.Name.Where(c => !char.IsControl(c)).ToArray()).Trim();

            // hardware type and LHM identifier, so a group name is never blank
            return string.IsNullOrWhiteSpace(cleanedName)
                ? $"{hardware.HardwareType} ({hardware.Identifier})"
                : cleanedName;
        }

        // flattens the hardware tree into _activeSensors, under _sensorLock against the polling loop
        private void InitAllSensors()
        {
            lock (_sensorLock)
            {
                _activeSensors.Clear();

                // watched first, so a sensor activated between the two is not lost
                foreach (var hardware in _computer.Hardware)
                {
                    WatchHardware(hardware);
                    DiscoverSensors(hardware, _activeSensors);
                }
            }
        }

        private static void DiscoverSensors(IHardware hardware, List<ISensor> target)
        {
            foreach (var sensor in hardware.Sensors)
            {
                target.Add(sensor);
            }

            // sub-hardware, e.g. the super I/O chips under a motherboard
            foreach (var subHardware in hardware.SubHardware)
            {
                DiscoverSensors(subHardware, target);
            }
        }
    }
}
