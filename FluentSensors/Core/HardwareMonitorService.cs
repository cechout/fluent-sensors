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
    // record container for all the relevant data about one sensor
    public record SensorData(
        string Id, // e.g. "/intelcpu/0/load/1" 
        string Name, // e.g. "CPU Package"
        string HardwareName, // e.g. "Intel Core i9-12900H"
        string HardwareType, // e.g. "Cpu", "GpuNvidia", "Memory"
        string SensorType, // e.g. "Power", "Temperature", "Load"
        double Value // the actual value of the sensor
    );


    // owns the single background polling loop every sensor value in the app comes from, plus the LHM hardware
    // discovery behind it
    // the loop is hand-written rather than timer-driven and its timing is the non-obvious part of this class,
    // see LoopAsync
    public class HardwareMonitorService
    {
        // === fields ===

        private readonly Computer _computer;

        // the dynamic list:
        // it contains all sensors we want to monitor
        // the manual way would be:
        // "private IHardware? _cpuHardware;" 
        // "private ISensor? _cpuPackagePowerSensor;" and so on
        private readonly List<ISensor> _activeSensors = new();

        private readonly object _sensorLock = new object();
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private readonly HashSet<string> _excludedSensorIds = new();

        // how many recent read durations the broadcast schedule plans against, and how much slack it leaves on top
        // of them; the margin absorbs the small run-to-run noise a sample window cannot see coming
        private const int ReadDurationSampleCount = 8;
        private const double ReadScheduleMarginMs = 5;

        // how long the cached network adapter snapshot may be reused before it gets rebuilt regardless of events
        private const double NetworkAdapterSnapshotMaxAgeMs = 10000;

        // ring buffer of the last read durations, touched only by the polling loop
        // the schedule plans against the slowest of them rather than the average: one slow read has to pull the
        // following reads earlier, and a maximum drops back on its own once that read ages out of the window
        private readonly double[] _readDurationSamples = new double[ReadDurationSampleCount];
        private int _readDurationSampleIndex;

        // wakes the polling loop out of a pending wait when the rate changes, so switching from 2000ms to 250ms
        // takes effect right away instead of after the wait it is already sitting in
        private readonly SemaphoreSlim _intervalChangedSignal = new(0, 1);

        // set by the NetworkChange handler, consumed by the polling loop; the rebuild itself deliberately runs on the
        // loop and not on the OS callback thread, see RefreshNetworkAdapters
        private volatile bool _networkAdaptersDirty = true;

        // currently "up" network adapters, see RefreshNetworkAdapters; touched only by the polling loop
        private HashSet<string> _activeNetworkAdapters = new();
        private long _networkAdapterRefreshTimestamp;


        // === singleton instance ===

        // this class is a singleton, because we want to have only one instance of this service that runs in the
        // background and updates the sensor values
        private static readonly HardwareMonitorService _instance = new HardwareMonitorService();
        public static HardwareMonitorService Instance => _instance;


        // === constructor ===

        private HardwareMonitorService()
        {
            _computer = new Computer
            {
                // all hardware components are explicitly disabled here to prevent the UI thread from freezing 
                // the actual initialization is deferred and chunked into the asynchronous pipeline methods (Init...Async)
                // below
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

                    // pull the polling loop out of its current wait so the new rate applies from here instead of once
                    // the old one runs out; a signal nobody is waiting on only makes the next wait re-evaluate once,
                    // which is harmless
                    if (_intervalChangedSignal.CurrentCount == 0)
                    {
                        _intervalChangedSignal.Release();
                    }

                    UpdateIntervalChanged?.Invoke(_updateIntervalMs);
                }
            }
        }

        // measured real cadence between two broadcasts, not just the requested UpdateIntervalMs; the loop below holds
        // every broadcast to at least UpdateIntervalMs, so this sits on the aimed-for value while LHM keeps up and
        // rises above it only once a read alone outruns the interval
        //
        // read cross-thread by AppStatusService; double reads/writes are not guaranteed atomic, Interlocked keeps
        // this lock-free instead of adding a lock for a single number
        private double _actualUpdateIntervalMs;
        public double ActualUpdateIntervalMs
        {
            get => Interlocked.CompareExchange(ref _actualUpdateIntervalMs, 0, 0);
            private set => Interlocked.Exchange(ref _actualUpdateIntervalMs, value);
        }

        // how long the last full sensor read took (hardware.Update() plus payload building)
        // this is the number that says whether a rate is reachable at all: once it approaches UpdateIntervalMs there
        // is no headroom left and the cadence starts slipping, which is why the status bar shows it
        //
        // cross-thread like ActualUpdateIntervalMs above, same reasoning for Interlocked
        private double _lastReadDurationMs;
        public double LastReadDurationMs
        {
            get => Interlocked.CompareExchange(ref _lastReadDurationMs, 0, 0);
            private set => Interlocked.Exchange(ref _lastReadDurationMs, value);
        }

        // asynchronous initialization pipeline:
        // lhm heavily blocks the calling thread when enabling all the hardware components
        // to prevent application freezes, these methods allow any consuming class or caller to trigger the 
        // hardware discovery step-by-step on isolated background threads (via Task.Run)
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

        // monitoring control:
        // starts the background polling loop to read sensor values
        // this method gets called from the outside (e.g. MainWindow); only after the asynchronous initialization pipeline has
        // fully completed of course
        public void StartMonitoring()
        {
            // prevent double execution
            if (_cts != null) return;

            InitAllSensors();

            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            _cts = new CancellationTokenSource();

            // task.run() creates a new thread in the background, and puts explicitly the method
            // LoopAsync on this new thread
            // we keep the reference so StopMonitoring can actually wait for the loop to finish, not just ask it to stop
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void StopMonitoring()
        {
            if (_cts == null) return;

            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

            _cts.Cancel();

            // block until the loop has fully exited (including a possibly already in-flight update), so once this method
            // returns, callers can be 100% sure HardwareDataUpdated will never fire again
            _loopTask?.Wait(2000);

            _cts = null;
            _loopTask = null;
        }

        public void Cleanup()
        {
            StopMonitoring();
            _computer.Close();
        }

        // exclusion API:
        // the service stays blind about the meaning of "excluded" (hidden, disabled, whatever); it just skips these ids
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

        // the master event:
        // instead of having multiple events for each sensor, we can have one event that
        // sends a list of all the sensor data at once
        // the manual way would be:
        // "public event Action<double>? CpuPackagePowerUpdated;"
        // "public event Action<double>? CpuIaPowerUpdated;" and so on
        public event Action<List<SensorData>>? HardwareDataUpdated;

        // fires whenever the polling interval changes at runtime; graphs use
        // this to keep their visible time span correct, since point count depends on both time span and interval
        public event Action<int>? UpdateIntervalChanged;


        // === private helpers ===

        // polling loop
        //
        // the scheduling below is the whole reason this is a hand-written loop and not a plain timer, and it exists to
        // keep the broadcast cadence off the read duration:
        // waiting a full interval after the read made the visible cadence interval + read time, so a 250ms setting
        // broadcast every ~350ms on a machine where a read takes 100ms
        // sizing that wait from the previous read instead made the cadence interval + (this read minus the previous
        // read), which drops below the configured rate every time a read comes back faster than the one before it
        // every graph shifts exactly one point per broadcast (see SensorGraphViewModel.AddDataPoint), so uneven
        // spacing is directly visible as uneven scroll speed, and a tick that lands early looks worse than a late one
        //
        // so the configured rate is the target and the floor at the same time
        // one tick, at a 250ms rate with a ~100ms read:
        //
        //   |.........idle.........|--read--|FIRE
        //   0                     145      250
        //                                   ^ deadline = previous FIRE + interval
        //
        // the deadline is anchored to the previous broadcast and never to an absolute grid, so an overrun shifts the
        // phase instead of getting paid back by a too-early next tick
        // the read is scheduled to end just before the deadline, planned against the slowest of the recent reads plus
        // a small margin; that keeps broadcast values a few ms old at every rate, instead of almost a full interval
        // old at the slow ones the way reading right after the previous broadcast would
        // a read that outruns the interval broadcasts late, and the next deadline counts from that late broadcast
        // waits only ever overshoot and never undershoot (see WaitSinceAsync), so the real cadence is the configured
        // interval plus up to one ~15.6ms Windows scheduler tick, never less than the interval
        private async Task LoopAsync(CancellationToken token)
        {
            RefreshNetworkAdapters();

            // backdated by one interval so the very first tick reads and broadcasts right away, instead of idling
            // through a full interval before the app shows any data at all
            long lastBroadcastTimestamp = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * (UpdateIntervalMs / 1000.0));

            while (!token.IsCancellationRequested)
            {
                // snapshotted per tick, the settings page can change the property at any point
                int intervalMs = UpdateIntervalMs;

                // hold the read back so it lands on the deadline instead of running right after the last broadcast
                double readStartMs = Math.Max(0, intervalMs - PredictReadDurationMs() - ReadScheduleMarginMs);
                var outcome = await WaitSinceAsync(lastBroadcastTimestamp, readStartMs, token);

                if (outcome == WaitOutcome.Cancelled) break;

                // the rate changed while we were idle, which leaves the schedule above stale; recompute it
                if (outcome == WaitOutcome.RateChanged) continue;

                long readStartTimestamp = Stopwatch.GetTimestamp();

                // update hardware (lhm fetches new values from the sensor)
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

                // this is the exact list for the big event HardwareDataUpdated, we create a new list
                // and every iteration fill it with the current values of all the sensors we want to monitor
                var payload = new List<SensorData>();

                lock (_sensorLock)
                {
                    foreach (var sensor in _activeSensors)
                    {
                        string id = sensor.Identifier.ToString();

                        // skip sensors that were excluded by the user (e.g. hidden in the UI); no payload entry means no
                        // UI update and no widget graph update for this tick
                        // TEMP: temporarily disabled to allow all sensors to be visible in PerformancePage
                        //if (_excludedSensorIds.Contains(id)) continue;

                        // skip sensors belonging to network adapters that are not currently active; Windows creates a huge
                        // amount of virtual/filter pseudo-adapters alongside every real one (QoS, WFP, Wi-Fi Direct, etc.),
                        // and those never carry meaningful data
                        if (sensor.Hardware.HardwareType == HardwareType.Network &&
                            !activeNetworkAdapters.Contains(sensor.Hardware.Name))
                        {
                            continue;
                        }

                        if (sensor.Value.HasValue) // some sensors might not have a value at the moment
                        {
                            double value = sensor.Value.Value;

                            // some sensors report NaN/Infinity instead of leaving Value unset; never broadcast garbage values
                            if (double.IsNaN(value) || double.IsInfinity(value))
                            {
                                continue;
                            }

                            // LHM reports throughput in raw bytes/s; normalized to MB/s here so every consumer sees a sane unit
                            if (sensor.SensorType == SensorType.Throughput)
                            {
                                value /= 1_048_576.0; // bytes/s -> MB/s
                            }

                            // some NVMe controllers report a name padded with non-printable control characters instead of a real
                            // string; IsNullOrWhiteSpace does not catch those, so they get stripped out first
                            string cleanedName = new string(sensor.Hardware.Name.Where(c => !char.IsControl(c)).ToArray()).Trim();

                            // falls back to hardware type + LHMs internal identifier so the UI never shows a blank group name
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

                // extra guard: skip the broadcast entirely if a shutdown was requested while we were building the payload above
                if (token.IsCancellationRequested) break;

                // hold the finished payload until the deadline; a rate change while holding moves that deadline, so
                // re-evaluate against the new one instead of firing on the old
                // the wait is already over when the read alone outran the interval, and the tick is then simply late
                do
                {
                    outcome = await WaitSinceAsync(lastBroadcastTimestamp, UpdateIntervalMs, token);
                }
                while (outcome == WaitOutcome.RateChanged);

                if (outcome == WaitOutcome.Cancelled) break;

                // real time since the previous broadcast, the actual cadence consumers see
                long broadcastTimestamp = Stopwatch.GetTimestamp();
                ActualUpdateIntervalMs = Stopwatch.GetElapsedTime(lastBroadcastTimestamp, broadcastTimestamp).TotalMilliseconds;
                lastBroadcastTimestamp = broadcastTimestamp;

                // we fire the event with the new list of sensor data
                HardwareDataUpdated?.Invoke(payload);

                // the gap between this broadcast and the next read is the one place where a rebuild costs nothing,
                // so the adapter snapshot gets refreshed here rather than in the middle of a read
                RefreshNetworkAdapters();
            }
        }

        // outcome of a wait in the polling loop above; RateChanged means the wait was cut short because the polling
        // rate changed, so whatever it was waiting for has to be recomputed against the new rate
        private enum WaitOutcome
        {
            Reached,
            RateChanged,
            Cancelled
        }

        // waits until targetMs have passed since anchorTimestamp, coming back early when the rate changes or when
        // monitoring is cancelled
        //
        // re-checks against the anchor in a loop on purpose: waits are bound to the ~15.6ms Windows scheduler
        // granularity and can come back short of what they were asked for, and coming back short is the one thing this
        // loop must not do; the re-check turns that into a second short wait instead of an early broadcast
        private async Task<WaitOutcome> WaitSinceAsync(long anchorTimestamp, double targetMs, CancellationToken token)
        {
            while (true)
            {
                double remainingMs = targetMs - Stopwatch.GetElapsedTime(anchorTimestamp).TotalMilliseconds;
                if (remainingMs <= 0) return WaitOutcome.Reached;

                // rounded up because the timeout is taken in whole milliseconds: a fractional remainder truncates to a
                // zero timeout, comes straight back, and leaves the re-check above spinning through the rest of it
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
                    // StopMonitoring cancelled the token while we were waiting; the caller exits the loop cleanly on
                    // this, so the task completes normally instead of ending up in the Canceled state
                    return WaitOutcome.Cancelled;
                }
            }
        }

        // the slowest of the recent reads, which is what the schedule has to survive
        // an average would leave every spike broadcasting late, a permanent worst case would never recover from a
        // single one
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

        // only flags the snapshot as due, the rebuild itself runs on the polling loop; see RefreshNetworkAdapters
        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            _networkAdaptersDirty = true;
        }

        // rebuilds the "up" network adapter snapshot, which has to stay current because Wi-Fi/Ethernet can connect or
        // disconnect while the app is running
        // rebuilt only when an address change flagged it, or when the last rebuild got old enough that a transition
        // NetworkAddressChanged does not raise would start to show
        //
        // called from the polling loops idle window on purpose: GetAllNetworkInterfaces plus GetIPProperties per
        // adapter is a multi-millisecond roundtrip whose cost spikes, and rebuilding it per tick put that spike
        // straight into the read window the broadcast schedule has to plan against
        private void RefreshNetworkAdapters()
        {
            bool isStale = Stopwatch.GetElapsedTime(_networkAdapterRefreshTimestamp).TotalMilliseconds >= NetworkAdapterSnapshotMaxAgeMs;
            if (!_networkAdaptersDirty && !isStale) return;

            _networkAdaptersDirty = false;
            _networkAdapterRefreshTimestamp = Stopwatch.GetTimestamp();

            // keyed by NetworkInterface.Name
            // (matches LHM's Hardware.Name 1:1)
            // filter layers (QoS Packet Scheduler, WFP, Native/Virtual WiFi Filter Driver) and WAN Miniport stubs as "Up"
            // even though they carry no real traffic; requiring at least one assigned IP address filters those out, since
            // only the actual physical/virtual adapter above them gets an address
            _activeNetworkAdapters = new HashSet<string>(
                NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                        nic.GetIPProperties().UnicastAddresses.Count > 0)
                    .Select(nic => nic.Name));
        }

        // sensor discovery:
        // goes through the discovered hardware tree and registers relevant sensors into the flat list
        // this process is protected by _sensorLock to ensure thread-safety, preventing collection modification crashes if the
        // background polling loop is preparing to run simultaneously
        private void InitAllSensors()
        {
            lock (_sensorLock)
            {
                _activeSensors.Clear();

                // we go through every sensor that lhm detects
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

            // some hardware (like motherboards or big GPUs) have sub-hardware
            // we traverse them recursively here
            foreach (var subHardware in hardware.SubHardware)
            {
                DiscoverSensors(subHardware);
            }
        }
    }
}