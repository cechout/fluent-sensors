using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;


namespace FluentSensors.Core
{
    // one snapshot of the apps self status: how many sensors LHM found and how many of them are currently
    // rendering, plus this processes own CPU/RAM/handle/GC footprint
    public record AppStatusData(
        int SensorsFound,
        int SensorsRendered,
        double CpuUsagePercent,
        long RamUsageBytes,
        int HandleCount,
        long GcMemoryBytes
    );


    // self monitoring: polls this processes own resource usage plus the LHM sensor counts on a fixed interval
    // feeds the title bar status readout for now, the future App Status page reuses the same data
    public class AppStatusService
    {
        // === fields ===

        private const int UpdateIntervalMs = 1000;

        private readonly Process _process = Process.GetCurrentProcess();
        private Timer _timer;
        private TimeSpan _lastCpuTime;
        private DateTime _lastSampleTime;

        // LhmHardwareTreeService.HardwareGroups is only ever mutated on the UI thread; reading it from Tick()s
        // background timer thread directly would race with that, see Tick() below
        private DispatcherQueue _dispatcherQueue;


        // === singleton instance ===

        private static readonly AppStatusService _instance = new AppStatusService();
        public static AppStatusService Instance => _instance;

        private AppStatusService() { }


        // === public api ===

        // fires once per tick with a fresh snapshot, from the UI thread (see Tick(), needed for the safe
        // LhmHardwareTreeService read); consumers can still marshal defensively if they want, its a no-op cost
        // from an already-UI thread
        public event Action<AppStatusData> StatusUpdated;

        // starts the polling timer; safe to call more than once, later calls are a no-op
        public void Start()
        {
            if (_timer != null) return;

            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _lastCpuTime = _process.TotalProcessorTime;
            _lastSampleTime = DateTime.UtcNow;

            _timer = new Timer(_ => Tick(), null, UpdateIntervalMs, UpdateIntervalMs);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }


        // === private helpers ===

        private void Tick()
        {
            // Process caches its own snapshot until Refresh is called, without this CpuTime/WorkingSet64/
            // HandleCount below would all keep returning the values from process start
            _process.Refresh();

            var now = DateTime.UtcNow;
            var cpuTime = _process.TotalProcessorTime;

            double elapsedMs = (now - _lastSampleTime).TotalMilliseconds;
            double cpuUsedMs = (cpuTime - _lastCpuTime).TotalMilliseconds;

            // percent relative to the whole machine (all cores), matches how the modern Task Manager shows it
            double cpuPercent = elapsedMs > 0
                ? cpuUsedMs / (elapsedMs * Environment.ProcessorCount) * 100.0
                : 0;

            _lastCpuTime = cpuTime;
            _lastSampleTime = now;

            // LhmHardwareTreeService.HardwareGroups only ever contains sensors that actually made it through
            // HardwareMonitorServices own filtering (active network adapters only, valid values only), the exact
            // same set the Sensors page itself is built from;
            // counting _activeSensors on HardwareMonitorService instead used to count LHMs raw pre-filter discovery,
            // including every never-shown sensor belonging to the many virtual/inactive network pseudo-adapters Windows
            // creates
            //
            // read on the UI thread since the collection is only ever mutated there, a background-thread read could
            // race an in-progress Add and throw
            _dispatcherQueue.TryEnqueue(() =>
            {
                int sensorsFound = LhmHardwareTreeService.Instance.HardwareGroups.Sum(g => g.Sensors.Count);

                var data = new AppStatusData(
                    SensorsFound: sensorsFound,
                    SensorsRendered: SensorGraphControl.ActiveRenderingCount,
                    CpuUsagePercent: Math.Clamp(cpuPercent, 0, 100),
                    RamUsageBytes: _process.WorkingSet64,
                    HandleCount: _process.HandleCount,
                    GcMemoryBytes: GC.GetTotalMemory(false)
                );

                StatusUpdated?.Invoke(data);
            });
        }
    }
}
